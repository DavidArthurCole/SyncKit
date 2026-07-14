using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using SyncKit.Auth;
using SyncKit.Contract;
using SyncKit.Db;
using SyncKit.Identity;
using SyncKit.Identity.Models;

var connString = Environment.GetEnvironmentVariable("IDENTITY_DB_CONNECTION")
    ?? throw new InvalidOperationException("IDENTITY_DB_CONNECTION is required");
var apiSecret = Environment.GetEnvironmentVariable("IDENTITY_API_SECRET")
    ?? throw new InvalidOperationException("IDENTITY_API_SECRET is required");
var port = Environment.GetEnvironmentVariable("IDENTITY_API_PORT") ?? "8090";
var adminIds = Environment.GetEnvironmentVariable("IDENTITY_ADMIN_DISCORD_IDS");
var sweepIntervalMinutes = int.TryParse(Environment.GetEnvironmentVariable("IDENTITY_LOGIN_SWEEP_INTERVAL_MINUTES"), out var m) ? m : 10;

var authentikAuthority = Environment.GetEnvironmentVariable("AUTHENTIK_AUTHORITY");
var authentikLoginClientId = Environment.GetEnvironmentVariable("AUTHENTIK_LOGIN_CLIENT_ID");
var authentikLoginClientSecret = Environment.GetEnvironmentVariable("AUTHENTIK_LOGIN_CLIENT_SECRET");
var loginCallbackUrl = Environment.GetEnvironmentVariable("IDENTITY_LOGIN_CALLBACK_URL");
var allowedReturnOrigins = (Environment.GetEnvironmentVariable("IDENTITY_LOGIN_ALLOWED_ORIGINS") ?? "")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .ToHashSet(StringComparer.OrdinalIgnoreCase);
var loginWidgetEnabled = !string.IsNullOrEmpty(authentikAuthority)
    && !string.IsNullOrEmpty(authentikLoginClientId)
    && !string.IsNullOrEmpty(authentikLoginClientSecret)
    && !string.IsNullOrEmpty(loginCallbackUrl);
if (loginWidgetEnabled)
    AuthentikOAuth.Init(authentikAuthority!, authentikLoginClientId!, authentikLoginClientSecret!, loginCallbackUrl!);

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

loginRoutes.MapGet("/start", async (HttpContext ctx, OAuthStateStore states) => {
    if (!loginWidgetEnabled) return Results.NotFound();
    var returnOrigin = ctx.Request.Query["returnOrigin"].ToString();
    if (string.IsNullOrEmpty(returnOrigin) || !allowedReturnOrigins.Contains(returnOrigin))
        return Results.BadRequest("returnOrigin not allowed");
    var mode = Program.ValidateMode(ctx.Request.Query["mode"].ToString());

    var (url, state, verifier) = AuthentikOAuth.AuthUrl();
    await states.SaveAsync(state, verifier, returnOrigin, mode, ctx.RequestAborted);
    return Results.Redirect(url);
});

loginRoutes.MapGet("/sources", async (HttpContext ctx, OAuthStateStore states, HttpClient http) => {
    if (!loginWidgetEnabled) return Results.NotFound();
    var returnOrigin = ctx.Request.Query["returnOrigin"].ToString();
    if (string.IsNullOrEmpty(returnOrigin) || !allowedReturnOrigins.Contains(returnOrigin))
        return Results.BadRequest("returnOrigin not allowed");

    var (query, state, verifier) = AuthentikOAuth.BuildAuthParams();
    var mode = Program.ValidateMode(ctx.Request.Query["mode"].ToString());
    await states.SaveAsync(state, verifier, returnOrigin, mode, ctx.RequestAborted);

    var flowUrl = $"{authentikAuthority!.TrimEnd('/')}/api/v3/flows/executor/federated-authentication-flow/?query={Uri.EscapeDataString(query)}";
    var flowResp = await http.GetAsync(flowUrl, ctx.RequestAborted);
    if (!flowResp.IsSuccessStatusCode) return Results.StatusCode(StatusCodes.Status502BadGateway);

    using var flowDoc = JsonDocument.Parse(await flowResp.Content.ReadAsStringAsync(ctx.RequestAborted));
    var component = flowDoc.RootElement.TryGetProperty("component", out var c) ? c.GetString() : null;
    if (component != "ak-stage-identification") return Results.StatusCode(StatusCodes.Status502BadGateway);

    var sources = Program.ParseLoginSources(flowDoc.RootElement, authentikAuthority!);
    return Results.Ok(new LoginSourcesResponse { Sources = sources });
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

    string loginCode;
    try {
        var token = await AuthentikOAuth.HandleCallbackAsync(code, saved.CodeVerifier, ctx.RequestAborted);
        var resolved = await resolver.ResolveAsync("authentik", token.Sub, token.DiscordId, token.Username, token.Avatar, ctx.RequestAborted);
        loginCode = await codes.IssueAsync(resolved.UserId, resolved.IsNew, ctx.RequestAborted);
    } catch (Exception) {
        if (saved.Mode == "redirect")
            return Results.Redirect(Program.BuildRedirectCallbackUrl(saved.ReturnOrigin, code: null, error: "login_failed"));

        var errorPayloadJson = System.Text.Json.JsonSerializer.Serialize(new { source = "synckit-auth", error = "login_failed" });
        var errorOriginJson = System.Text.Json.JsonSerializer.Serialize(saved.ReturnOrigin);
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
        return Results.Redirect(Program.BuildRedirectCallbackUrl(saved.ReturnOrigin, code: loginCode, error: null));

    var payloadJson = System.Text.Json.JsonSerializer.Serialize(new { source = "synckit-auth", code = loginCode });
    var originJson = System.Text.Json.JsonSerializer.Serialize(saved.ReturnOrigin);
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
        ValidAudience = authentikLoginClientId,
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
    public static string ValidateMode(string? raw) =>
        raw is "inline" or "redirect" ? raw : "popup";

    public static string BuildRedirectCallbackUrl(string returnOrigin, string? code, string? error) {
        var param = code is not null ? $"code={Uri.EscapeDataString(code)}" : $"error={Uri.EscapeDataString(error!)}";
        return $"{returnOrigin}/auth/callback?{param}";
    }

    public static List<LoginSourceResponse> ParseLoginSources(JsonElement identificationStage, string authority) {
        var result = new List<LoginSourceResponse>();
        if (!identificationStage.TryGetProperty("sources", out var sourcesEl) || sourcesEl.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var source in sourcesEl.EnumerateArray()) {
            if (!source.TryGetProperty("challenge", out var challenge) || challenge.ValueKind != JsonValueKind.Object) continue;
            var component = challenge.TryGetProperty("component", out var c) ? c.GetString() : null;
            if (component != "xak-flow-redirect") continue;
            if (!challenge.TryGetProperty("to", out var toEl)) continue;

            result.Add(new LoginSourceResponse {
                Name = source.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                IconUrl = Absolutize(authority, source.TryGetProperty("icon_url", out var i) && i.ValueKind != JsonValueKind.Null ? i.GetString() : null),
                Url = Absolutize(authority, toEl.GetString()) ?? "",
            });
        }
        return result;
    }

    // Authentik returns challenge.to and icon_url relative to its own origin.
    private static string? Absolutize(string authority, string? url) =>
        string.IsNullOrEmpty(url) || url.StartsWith("http", StringComparison.Ordinal) ? url : $"{authority.TrimEnd('/')}{url}";
}
