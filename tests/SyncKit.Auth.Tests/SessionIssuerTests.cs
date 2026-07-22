using Microsoft.AspNetCore.Http;
using SyncKit.Auth;
using Xunit;

namespace SyncKit.Auth.Tests;

public class SessionIssuerTests {
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static SessionCookieOptions Options() => new() {
        SigningSecret = "super-secret-signing-key-of-sufficient-length",
        CookieName = "synckit_session",
        CookieDomain = "davidarthurcole.me",
        Ttl = TimeSpan.FromMinutes(480),
    };

    private static SessionUser User() =>
        new(UserId: "8d2f0e94-1c3a-4b6e-9f11-2a7c5d0e1234", Sid: "sess-123", Role: "admin");

    [Fact]
    public void IssueCookie_SetsScopedSecureCookie() {
        var ctx = new DefaultHttpContext();

        SessionIssuer.IssueCookie(ctx.Response, Options(), User(), Now);

        var setCookie = ctx.Response.Headers.SetCookie.ToString();
        Assert.Contains("synckit_session=", setCookie, StringComparison.Ordinal);
        Assert.Contains("domain=davidarthurcole.me", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IssueCookie_ValueValidatesBack() {
        var ctx = new DefaultHttpContext();
        var options = Options();

        SessionIssuer.IssueCookie(ctx.Response, options, User(), Now);

        var setCookie = ctx.Response.Headers.SetCookie.ToString();
        var value = setCookie["synckit_session=".Length..].Split(';', 2)[0];
        Assert.NotNull(SessionToken.Validate(options, Uri.UnescapeDataString(value), Now));
    }

    [Fact]
    public void ClearCookie_ExpiresInPast() {
        var ctx = new DefaultHttpContext();

        SessionIssuer.ClearCookie(ctx.Response, Options());

        var setCookie = ctx.Response.Headers.SetCookie.ToString();
        Assert.Contains("synckit_session=", setCookie, StringComparison.Ordinal);
        Assert.Contains("expires=Thu, 01 Jan 1970", setCookie, StringComparison.OrdinalIgnoreCase);
    }
}
