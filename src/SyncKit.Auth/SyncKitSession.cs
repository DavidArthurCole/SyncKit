using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SyncKit.Identity.Client;

namespace SyncKit.Auth;

public static class SyncKitSessionDefaults {
    public const string Scheme = "SyncKitSession";
}

public sealed class SyncKitSessionOptions : AuthenticationSchemeOptions {
    public SessionCookieOptions Cookie { get; set; } = null!;
}

public sealed class SyncKitSessionHandler(
    IOptionsMonitor<SyncKitSessionOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    TimeProvider clock,
    SessionRevocationCache revocations)
    : AuthenticationHandler<SyncKitSessionOptions>(options, logger, encoder) {

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync() {
        var cookie = Options.Cookie;
        if (!Request.Cookies.TryGetValue(cookie.CookieName, out var token) || string.IsNullOrEmpty(token))
            return AuthenticateResult.NoResult();

        var principal = SessionToken.Validate(cookie, token, clock.GetUtcNow());
        if (principal is null)
            return AuthenticateResult.Fail("invalid session token");

        var sid = principal.FindFirstValue(SessionClaims.SessionId);
        if (!string.IsNullOrEmpty(sid)) {
            var identity = Context.RequestServices.GetService<IdentityApiClient>();
            if (identity is not null) {
                bool revoked;
                try {
                    revoked = await revocations.IsRevokedAsync(
                        sid, ct => identity.IsRevokedAsync(sid, ct), Context.RequestAborted);
                } catch (Exception ex) {
                    Logger.LogWarning(ex, "SyncKit session revocation check failed; treating session as live");
                    revoked = false;
                }
                if (revoked)
                    return AuthenticateResult.Fail("session revoked");
            }
        }

        if (SessionToken.ShouldRenew(principal, cookie, clock.GetUtcNow())) {
            var renewed = SessionToken.Renew(cookie, principal, clock.GetUtcNow());
            SessionIssuer.WriteCookie(Response, cookie, renewed, clock.GetUtcNow() + cookie.Ttl);
        }

        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }
}

public static class SyncKitSessionExtensions {
    public static AuthenticationBuilder AddSyncKitSession(
        this AuthenticationBuilder builder,
        SessionCookieOptions cookie,
        TimeSpan? revocationCacheTtl = null,
        string scheme = SyncKitSessionDefaults.Scheme) {
        var ttl = revocationCacheTtl ?? TimeSpan.FromSeconds(30);
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton(sp =>
            new SessionRevocationCache(sp.GetRequiredService<TimeProvider>(), ttl));
        return builder.AddScheme<SyncKitSessionOptions, SyncKitSessionHandler>(scheme, o => o.Cookie = cookie);
    }
}
