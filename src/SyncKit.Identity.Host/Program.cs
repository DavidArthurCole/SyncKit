using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using SyncKit.Auth;
using SyncKit.Contract;
using SyncKit.Db;
using SyncKit.Identity;
using SyncKit.Identity.Host;
using SyncKit.Identity.Models;

var connString = Environment.GetEnvironmentVariable("IDENTITY_DB_CONNECTION")
    ?? throw new InvalidOperationException("IDENTITY_DB_CONNECTION is required");
var apiSecret = Environment.GetEnvironmentVariable("IDENTITY_API_SECRET")
    ?? throw new InvalidOperationException("IDENTITY_API_SECRET is required");
var port = Environment.GetEnvironmentVariable("IDENTITY_API_PORT") ?? "8090";
var adminIds = Environment.GetEnvironmentVariable("IDENTITY_ADMIN_DISCORD_IDS");
var sweepIntervalMinutes = int.TryParse(Environment.GetEnvironmentVariable("IDENTITY_LOGIN_SWEEP_INTERVAL_MINUTES"), out var m) ? m : 10;

var authentikAuthority = Environment.GetEnvironmentVariable("AUTHENTIK_AUTHORITY");
var authentikAppsDir = Environment.GetEnvironmentVariable("AUTHENTIK_APPS_DIR");
var loginWidgetEnabled = !string.IsNullOrEmpty(authentikAuthority) && !string.IsNullOrEmpty(authentikAppsDir);
var appConfigs = loginWidgetEnabled
    ? AppAuthConfigLoader.LoadFromDirectory(authentikAppsDir!, authentikAuthority!)
    : [];
var sessionOptions = SessionCookieOptions.FromEnvironment();

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://*:{port}");

var dataSource = NpgsqlDataSource.Create(connString);
builder.Services.AddSingleton(dataSource);
builder.Services.AddSingleton(AdminAllowlist.FromConfig(adminIds));
builder.Services.AddSingleton<IdentityResolver>();
builder.Services.AddSingleton<RevocationStore>();
builder.Services.AddSingleton<UserQueries>();
builder.Services.AddSingleton<LoginCodeStore>();
builder.Services.AddSingleton<OAuthStateStore>();
builder.Services.AddHttpClient();
if (loginWidgetEnabled)
    builder.Services.AddSingleton(sp => new IconCache(sp.GetRequiredService<IHttpClientFactory>(), authentikAuthority!));

// Backs /login/backchannel-logout's logout_token signature check: fetches and caches Authentik's
// discovery doc/JWKS independently of AuthentikOAuth's own authorization-code exchange.
if (loginWidgetEnabled) {
    builder.Services.AddSingleton(new ConfigurationManager<OpenIdConnectConfiguration>(
        $"{authentikAuthority!.TrimEnd('/')}/.well-known/openid-configuration",
        new OpenIdConnectConfigurationRetriever()));
}

var app = builder.Build();
app.UseStaticFiles();

await using (var conn = await dataSource.OpenConnectionAsync())
    await Migrator.MigrateAsync(conn, Path.Combine(AppContext.BaseDirectory, "Migrations"));

var sweeper = new ExpiredRowSweeper(dataSource, TimeSpan.FromMinutes(sweepIntervalMinutes));
_ = sweeper.RunAsync(app.Lifetime.ApplicationStopping);

// Unauthenticated browser-facing routes MUST be reachable without the bearer secret below,
// since browsers can't send IDENTITY_API_SECRET.
var loginRoutes = app.MapGroup("/login");

loginRoutes.MapGet("/sources", (HttpContext ctx) => {
    if (!loginWidgetEnabled) return Results.NotFound();
    var returnUrl = ctx.Request.Query["returnUrl"].ToString();
    var app = Program.ResolveApp(returnUrl, appConfigs);
    if (app is null) return Results.BadRequest("returnUrl not allowed");

    var mode = Program.ValidateMode(ctx.Request.Query["mode"].ToString());
    var sources = Program.KnownProviders.Select(provider => new LoginSourceResponse {
        Name = char.ToUpperInvariant(provider[0]) + provider[1..],
        IconUrl = $"/login/icons/{provider}",
        Url = $"/login/go/{provider}?returnUrl={Uri.EscapeDataString(returnUrl)}&mode={Uri.EscapeDataString(mode)}",
    }).ToList();
    return Results.Ok(new LoginSourcesResponse { Sources = sources });
});

loginRoutes.MapGet("/icons/{provider}", async (HttpContext ctx, string provider, IconCache icons) => {
    if (!loginWidgetEnabled) return Results.NotFound();
    if (!Program.KnownProviders.Contains(provider)) return Results.NotFound();

    var icon = await icons.GetAsync(provider, ctx.RequestAborted);
    if (icon is null)
        return Results.Redirect($"{authentikAuthority!.TrimEnd('/')}/static/authentik/sources/{provider}.svg");

    ctx.Response.Headers.CacheControl = "public, max-age=86400";
    return Results.Bytes(icon.Bytes, icon.ContentType);
});

loginRoutes.MapGet("/go/{provider}", async (HttpContext ctx, string provider, OAuthStateStore states) => {
    if (!loginWidgetEnabled) return Results.NotFound();
    var returnUrl = ctx.Request.Query["returnUrl"].ToString();
    var app = Program.ResolveApp(returnUrl, appConfigs);
    if (app is null) return Results.BadRequest("returnUrl not allowed");
    if (!Program.KnownProviders.Contains(provider)) return Results.BadRequest("unknown provider");

    var mode = Program.ValidateMode(ctx.Request.Query["mode"].ToString());
    var (query, state, verifier) = app.OAuth.BuildAuthParams();
    await states.SaveAsync(state, verifier, returnUrl, mode, ctx.RequestAborted);

    var authorizeUrl = $"{app.OAuth.Authority}/application/o/authorize/?{query}";
    var flowUrl = Program.BuildFlowUrl(app.OAuth.Authority, provider, authorizeUrl);
    return Results.Redirect(flowUrl);
});

loginRoutes.MapGet("/callback", async (HttpContext ctx, OAuthStateStore states, IdentityResolver resolver, LoginCodeStore codes) => {
    if (!loginWidgetEnabled) return Results.NotFound();
    var code = ctx.Request.Query["code"].ToString();
    var state = ctx.Request.Query["state"].ToString();
    if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
        return Results.BadRequest("missing code or state");

    var saved = await states.ConsumeAsync(state, ctx.RequestAborted);
    if (saved is null)
        return Results.BadRequest("unknown or expired state");

    var app = Program.ResolveApp(saved.ReturnUrl, appConfigs);
    if (app is null)
        return Results.BadRequest("returnUrl not allowed");

    string loginCode;
    try {
        var token = await app.OAuth.HandleCallbackAsync(code, saved.CodeVerifier, ctx.RequestAborted);
        var resolved = await resolver.ResolveAsync("authentik", token.Sub, token.DiscordId, token.Username, token.Avatar, ctx.RequestAborted);
        loginCode = await codes.IssueAsync(resolved.UserId, resolved.IsNew, ctx.RequestAborted);
        if (sessionOptions is not null) {
            SessionIssuer.IssueCookie(ctx.Response, sessionOptions, new SessionUser(
                UserId: resolved.UserId.ToString(),
                Sid: token.Sid,
                Role: resolved.Role,
                Name: token.Username,
                Avatar: token.Avatar,
                DiscordId: resolved.DiscordId),
                DateTimeOffset.UtcNow);
        }
    } catch (Exception) {
        if (saved.Mode == "redirect")
            return Results.Redirect(Program.BuildRedirectCallbackUrl(saved.ReturnUrl, code: null, error: "login_failed"));

        var errorPayloadJson = System.Text.Json.JsonSerializer.Serialize(new { source = "synckit-auth", error = "login_failed" });
        var errorOriginJson = System.Text.Json.JsonSerializer.Serialize(app.Origin);
        var errorHtml = $"""
            <!DOCTYPE html><html><body><script>
            var target = window.opener || window.parent;
            target && target.postMessage({errorPayloadJson}, {errorOriginJson});
            {(saved.Mode == "inline" ? "" : "window.close();")}
            </script></body></html>
            """;
        return Results.Content(errorHtml, "text/html");
    }

    if (saved.Mode == "redirect")
        return Results.Redirect(Program.BuildRedirectCallbackUrl(saved.ReturnUrl, code: loginCode, error: null));

    var payloadJson = System.Text.Json.JsonSerializer.Serialize(new { source = "synckit-auth", code = loginCode });
    var originJson = System.Text.Json.JsonSerializer.Serialize(app.Origin);
    var html = $"""
        <!DOCTYPE html><html><body><script>
        var target = window.opener || window.parent;
        target && target.postMessage({payloadJson}, {originJson});
        {(saved.Mode == "inline" ? "" : "window.close();")}
        </script></body></html>
        """;
    return Results.Content(html, "text/html");
});

// Authentik back-channel logout: server-to-server POST with a signed logout_token, no cookies/session context.
// Per OIDC Back-Channel Logout 1.0 sec 2.6: verify signature + iss/aud, require an "events" claim carrying backchannel-logout, forbid "nonce", then revoke the sid.
loginRoutes.MapPost("/backchannel-logout", async (HttpContext ctx, RevocationStore revocations) => {
    if (!loginWidgetEnabled) return Results.NotFound();
    var form = await ctx.Request.ReadFormAsync(ctx.RequestAborted);
    var logoutToken = form["logout_token"].ToString();
    if (string.IsNullOrWhiteSpace(logoutToken))
        return Results.BadRequest();

    var configManager = ctx.RequestServices.GetRequiredService<ConfigurationManager<OpenIdConnectConfiguration>>();
    OpenIdConnectConfiguration discovery;
    try {
        discovery = await configManager.GetConfigurationAsync(ctx.RequestAborted);
    } catch (Exception) {
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }

    var validationParams = new TokenValidationParameters {
        ValidIssuer = discovery.Issuer,
        IssuerSigningKeys = discovery.SigningKeys,
        ValidateAudience = false,
        ValidateLifetime = true,
    };

    System.Security.Claims.ClaimsPrincipal principal;
    try {
        principal = new JwtSecurityTokenHandler().ValidateToken(logoutToken, validationParams, out _);
    } catch (Exception) {
        return Results.BadRequest();
    }

    if (principal.FindFirst("nonce") is not null)
        return Results.BadRequest();

    var events = principal.FindFirst("events")?.Value;
    if (string.IsNullOrEmpty(events) || !events.Contains("backchannel-logout"))
        return Results.BadRequest();

    var sid = principal.FindFirst("sid")?.Value;
    if (string.IsNullOrEmpty(sid))
        return Results.BadRequest();

    await revocations.RevokeAsync(sid, ctx.RequestAborted);
    return Results.Ok();
});

loginRoutes.MapGet("/logout", async (HttpContext ctx) => {
    var returnUrlRaw = ctx.Request.Query["returnUrl"].ToString();
    var returnUrl = Program.ResolveApp(returnUrlRaw, appConfigs) is not null ? returnUrlRaw : null;

    if (sessionOptions is not null)
        SessionIssuer.ClearCookie(ctx.Response, sessionOptions);

    var configManager = ctx.RequestServices.GetService<ConfigurationManager<OpenIdConnectConfiguration>>();
    if (configManager is not null) {
        try {
            var discovery = await configManager.GetConfigurationAsync(ctx.RequestAborted);
            if (!string.IsNullOrEmpty(discovery.EndSessionEndpoint)) {
                var endSession = discovery.EndSessionEndpoint;
                if (!string.IsNullOrEmpty(returnUrl)) {
                    var sep = endSession.Contains('?', StringComparison.Ordinal) ? "&" : "?";
                    endSession = $"{endSession}{sep}post_logout_redirect_uri={Uri.EscapeDataString(returnUrl)}";
                }
                return Results.Redirect(endSession);
            }
        } catch (Exception) {
            return string.IsNullOrEmpty(returnUrl) ? Results.Ok() : Results.Redirect(returnUrl);
        }
    }

    return string.IsNullOrEmpty(returnUrl) ? Results.Ok() : Results.Redirect(returnUrl);
});

app.Use(async (ctx, next) => {
    if (ctx.Request.Path.StartsWithSegments("/login") || ctx.Request.Path.StartsWithSegments("/synckit-login.js")) {
        await next();
        return;
    }
    var auth = ctx.Request.Headers.Authorization.ToString();
    if (auth != $"Bearer {apiSecret}") {
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await ctx.Response.WriteAsync("unauthorized");
        return;
    }
    await next();
});

app.MapPost("/identity/resolve", async (IdentityResolveRequest req, IdentityResolver resolver, CancellationToken ct) => {
    var result = await resolver.ResolveAsync(req.Provider, req.Subject, req.DiscordId, req.Username, req.Avatar, ct);
    return Results.Ok(new IdentityResolveResponse {
        UserId = result.UserId,
        Role = result.Role,
        DiscordId = result.DiscordId,
        IsNew = result.IsNew,
    });
});

app.MapGet("/identity/{userId:guid}", async (Guid userId, UserQueries users, CancellationToken ct) => {
    var user = await users.GetAsync(userId, ct);
    if (user is null) return Results.NotFound();
    return Results.Ok(ToResponse(user));
});

// Session revocation is sid-keyed only (revoked_sessions has no user_id column) - no userId in
// these routes; back-channel logout tokens only ever carry a sid, never a user_id, so requiring
// one here would force every caller into an unnecessary extra lookup.
app.MapPost("/identity/revoke-session", async (RevokeSessionRequest req, RevocationStore store, CancellationToken ct) => {
    await store.RevokeAsync(req.Sid, ct);
    return Results.NoContent();
});

app.MapGet("/identity/sessions/{sid}/revoked", async (string sid, RevocationStore store, CancellationToken ct) =>
    Results.Ok(await store.IsRevokedAsync(sid, ct)));

app.MapPost("/identity/merge", async (MergeUsersRequest req, IdentityResolver resolver, CancellationToken ct) => {
    var winner = await resolver.MergeAsync(req.KeepUserId, req.MergeUserId, ct);
    return Results.Ok(new { userId = winner });
});

app.MapGet("/identity/admin/users", async (UserQueries users, CancellationToken ct) =>
    Results.Ok((await users.ListAsync(ct)).Select(ToResponse)));

app.MapPost("/identity/{userId:guid}/role", async (Guid userId, SetRoleRequest req, UserQueries users, CancellationToken ct) => {
    var ok = await users.SetRoleAsync(userId, UserRoles.Parse(req.Role), ct);
    return ok ? Results.NoContent() : Results.NotFound();
});

app.MapPost("/identity/redeem", async (RedeemLoginCodeRequest req, LoginCodeStore codes, UserQueries users, CancellationToken ct) => {
    var redeemed = await codes.RedeemAsync(req.Code, ct);
    if (redeemed is null) return Results.NotFound();
    var user = await users.GetAsync(redeemed.UserId, ct);
    if (user is null) return Results.NotFound();
    return Results.Ok(new RedeemLoginCodeResponse {
        UserId = user.UserId,
        DiscordId = user.DiscordId,
        Username = user.Username,
        Avatar = user.Avatar,
        Role = user.Role,
        IsNew = redeemed.IsNew,
    });
});

app.Run();

static IdentityUserResponse ToResponse(User u) => new() {
    UserId = u.UserId,
    DiscordId = u.DiscordId,
    Username = u.Username,
    Avatar = u.Avatar,
    Role = u.Role,
    CreatedAt = u.CreatedAt,
    LastLoginAt = u.LastLoginAt,
};

public partial class Program {
    public static readonly string[] KnownProviders = ["discord", "google", "microsoft", "github"];

    public static string ValidateMode(string? raw) =>
        raw is "inline" or "redirect" ? raw : "popup";

    public static AppAuthConfig? ResolveApp(string returnUrl, Dictionary<string, AppAuthConfig> appConfigs) {
        if (string.IsNullOrEmpty(returnUrl) || !Uri.TryCreate(returnUrl, UriKind.Absolute, out var parsed))
            return null;
        var origin = $"{parsed.Scheme}://{parsed.Authority}";
        return appConfigs.TryGetValue(origin, out var app) ? app : null;
    }

    public static string BuildFlowUrl(string authority, string provider, string authorizeUrl) {
        var flowSlug = $"{provider}-only-auth";
        return $"{authority}/if/flow/{flowSlug}/?next={Uri.EscapeDataString(authorizeUrl)}";
    }

    public static string BuildRedirectCallbackUrl(string returnUrl, string? code, string? error) {
        var param = code is not null ? $"code={Uri.EscapeDataString(code)}" : $"error={Uri.EscapeDataString(error!)}";
        var separator = returnUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{returnUrl}{separator}{param}";
    }
}
