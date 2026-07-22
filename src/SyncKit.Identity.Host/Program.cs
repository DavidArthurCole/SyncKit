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
var avatarStorageDir = Environment.GetEnvironmentVariable("AVATAR_STORAGE_DIR");
var profileEnabled = loginWidgetEnabled && sessionOptions is not null && !string.IsNullOrEmpty(avatarStorageDir);

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://*:{port}");

var dataSource = NpgsqlDataSource.Create(connString);
builder.Services.AddSingleton(dataSource);
builder.Services.AddSingleton(AdminAllowlist.FromConfig(adminIds));
builder.Services.AddSingleton<IdentityResolver>();
builder.Services.AddSingleton<RevocationStore>();
builder.Services.AddSingleton<UserQueries>();
builder.Services.AddSingleton<ProfileService>();
builder.Services.AddSingleton<LoginCodeStore>();
builder.Services.AddSingleton<OAuthStateStore>();
builder.Services.AddHttpClient();
if (loginWidgetEnabled)
    builder.Services.AddSingleton(sp => new IconCache(sp.GetRequiredService<IHttpClientFactory>(), authentikAuthority!));

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

loginRoutes.MapGet("/callback", async (HttpContext ctx, OAuthStateStore states, IdentityResolver resolver, LoginCodeStore codes, UserQueries users) => {
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

        if (saved.Mode.StartsWith("link:", StringComparison.Ordinal)) {
            var targetUserId = Guid.Parse(saved.Mode["link:".Length..]);
            var linkOutcome = await resolver.TryLinkAsync(targetUserId, "authentik", token.Sub, token.DiscordId, token.Username, token.Avatar, ctx.RequestAborted);
            var linkFlag = linkOutcome.Conflict ? "linkConflict=1" : linkOutcome.Linked ? "linked=ok" : "linkError=1";
            return Results.Redirect(Program.AppendQuery(saved.ReturnUrl, linkFlag));
        }

        var resolved = await resolver.ResolveAsync("authentik", token.Sub, token.DiscordId, token.Username, token.Avatar, ctx.RequestAborted);
        loginCode = await codes.IssueAsync(resolved.UserId, resolved.IsNew, ctx.RequestAborted);
        if (sessionOptions is not null) {
            var issuedAt = DateTimeOffset.UtcNow;
            var user = await users.GetAsync(resolved.UserId, ctx.RequestAborted);
            SessionIssuer.IssueCookie(ctx.Response, sessionOptions, new SessionUser(
                UserId: resolved.UserId.ToString(),
                Sid: token.Sid,
                Role: resolved.Role,
                Name: user?.Username ?? token.Username,
                Avatar: user?.Avatar ?? token.Avatar,
                DiscordId: user?.DiscordId ?? resolved.DiscordId),
                issuedAt);
            if (!string.IsNullOrEmpty(token.IdToken))
                ctx.Response.Cookies.Append(Program.IdHintCookie, token.IdToken, new CookieOptions {
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Lax,
                    Path = "/login",
                    Expires = issuedAt + sessionOptions.Ttl,
                });
        }
    } catch (Exception) {
        if (saved.Mode.StartsWith("link:", StringComparison.Ordinal))
            return Results.Redirect(Program.AppendQuery(saved.ReturnUrl, "linkError=1"));

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

    var idTokenHint = ctx.Request.Cookies.TryGetValue(Program.IdHintCookie, out var hint) ? hint : null;

    if (sessionOptions is not null)
        SessionIssuer.ClearCookie(ctx.Response, sessionOptions);
    ctx.Response.Cookies.Delete(Program.IdHintCookie, new CookieOptions { Path = "/login" });

    var configManager = ctx.RequestServices.GetService<ConfigurationManager<OpenIdConnectConfiguration>>();
    if (configManager is not null) {
        try {
            var discovery = await configManager.GetConfigurationAsync(ctx.RequestAborted);
            if (!string.IsNullOrEmpty(discovery.EndSessionEndpoint))
                return Results.Redirect(Program.BuildEndSessionUrl(discovery.EndSessionEndpoint, idTokenHint, returnUrl));
        } catch (Exception) {
            return string.IsNullOrEmpty(returnUrl) ? Results.Ok() : Results.Redirect(returnUrl);
        }
    }

    return string.IsNullOrEmpty(returnUrl) ? Results.Ok() : Results.Redirect(returnUrl);
});

if (profileEnabled) {
    ProfileRoutes.Map(app, sessionOptions!, avatarStorageDir!, app.Services.GetRequiredService<RevocationStore>(),
        app.Services.GetRequiredService<ProfileService>(), app.Services.GetRequiredService<UserQueries>());

    app.MapGet("/profile/link/{provider}/start", async (HttpContext ctx, string provider, OAuthStateStore states) => {
        var userId = await ProfileAuth.TryGetUserIdAsync(ctx, sessionOptions!, app.Services.GetRequiredService<RevocationStore>().IsRevokedAsync, ctx.RequestAborted);
        if (userId is null) return Results.Unauthorized();

        var returnUrl = ctx.Request.Query["returnUrl"].ToString();
        var linkApp = Program.ResolveApp(returnUrl, appConfigs);
        if (linkApp is null) return Results.BadRequest("returnUrl not allowed");
        if (!Program.KnownProviders.Contains(provider)) return Results.BadRequest("unknown provider");

        var (query, state, verifier) = linkApp.OAuth.BuildAuthParams();
        await states.SaveAsync(state, verifier, returnUrl, $"link:{userId}", ctx.RequestAborted);

        var authorizeUrl = $"{linkApp.OAuth.Authority}/application/o/authorize/?{query}";
        var flowUrl = Program.BuildFlowUrl(linkApp.OAuth.Authority, provider, authorizeUrl);
        return Results.Redirect(flowUrl);
    });
}

app.Use(async (ctx, next) => {
    if (ctx.Request.Path.StartsWithSegments("/login") || ctx.Request.Path.StartsWithSegments("/synckit-login.js")
        || ctx.Request.Path.StartsWithSegments("/profile") || ctx.Request.Path.StartsWithSegments("/avatars")) {
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
    public const string IdHintCookie = "synckit_idhint";

    public static readonly string[] KnownProviders = ["discord", "google", "microsoft", "github"];

    public static string BuildEndSessionUrl(string endSessionEndpoint, string? idTokenHint, string? returnUrl) {
        var url = endSessionEndpoint;
        if (!string.IsNullOrEmpty(idTokenHint))
            url = Append(url, $"id_token_hint={Uri.EscapeDataString(idTokenHint)}");
        if (!string.IsNullOrEmpty(returnUrl))
            url = Append(url, $"post_logout_redirect_uri={Uri.EscapeDataString(returnUrl)}");
        return url;

        static string Append(string target, string param) {
            var sep = target.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            return $"{target}{sep}{param}";
        }
    }

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

    public static string AppendQuery(string returnUrl, string param) {
        var separator = returnUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{returnUrl}{separator}{param}";
    }
}
