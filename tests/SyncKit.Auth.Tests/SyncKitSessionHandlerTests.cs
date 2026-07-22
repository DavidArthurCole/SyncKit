using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SyncKit.Auth;
using Xunit;

namespace SyncKit.Auth.Tests;

public class SyncKitSessionHandlerTests {
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static SessionCookieOptions Cookie() => new() {
        SigningSecret = "super-secret-signing-key-of-sufficient-length",
        Ttl = TimeSpan.FromMinutes(480),
    };

    private static async Task<SyncKitSessionHandler> HandlerAsync(SyncKitSessionOptions options, HttpContext context) {
        var clock = new FixedClock(Now);
        var cache = new SessionRevocationCache(clock, TimeSpan.FromSeconds(30));
        var handler = new SyncKitSessionHandler(
            new StaticOptionsMonitor<SyncKitSessionOptions>(options), NullLoggerFactory.Instance, UrlEncoder.Default, clock, cache);
        await handler.InitializeAsync(
            new AuthenticationScheme(SyncKitSessionDefaults.Scheme, null, typeof(SyncKitSessionHandler)), context);
        return handler;
    }

    private static HttpContext ContextWithCookie(string? token) {
        var ctx = new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() };
        if (token is not null) ctx.Request.Headers.Cookie = $"synckit_session={token}";
        return ctx;
    }

    [Fact]
    public async Task NoCookie_ReturnsNoResult() {
        var handler = await HandlerAsync(new SyncKitSessionOptions { Cookie = Cookie() }, ContextWithCookie(null));

        var result = await handler.AuthenticateAsync();

        Assert.True(result.None);
    }

    [Fact]
    public async Task ValidCookie_Succeeds() {
        var cookie = Cookie();
        var token = SessionToken.Issue(cookie, new SessionUser("11111111-1111-1111-1111-111111111111", "sid", "admin"), Now);
        var handler = await HandlerAsync(new SyncKitSessionOptions { Cookie = cookie }, ContextWithCookie(token));

        var result = await handler.AuthenticateAsync();

        Assert.True(result.Succeeded);
        Assert.Equal("admin", result.Principal!.FindFirstValue(SessionClaims.Role));
    }

    [Fact]
    public async Task OnValidated_CanStampAppClaim() {
        var cookie = Cookie();
        var token = SessionToken.Issue(cookie, new SessionUser("11111111-1111-1111-1111-111111111111", "sid", "admin"), Now);
        var options = new SyncKitSessionOptions {
            Cookie = cookie,
            OnValidated = (principal, _, _) => {
                ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim("egi:supporter", "true"));
                return Task.CompletedTask;
            },
        };
        var handler = await HandlerAsync(options, ContextWithCookie(token));

        var result = await handler.AuthenticateAsync();

        Assert.True(result.Succeeded);
        Assert.Equal("true", result.Principal!.FindFirstValue("egi:supporter"));
    }

    [Fact]
    public async Task OnValidated_ThrowingIsSwallowed_SessionStillValid() {
        var cookie = Cookie();
        var token = SessionToken.Issue(cookie, new SessionUser("11111111-1111-1111-1111-111111111111", "sid", "admin"), Now);
        var options = new SyncKitSessionOptions {
            Cookie = cookie,
            OnValidated = (_, _, _) => throw new InvalidOperationException("benefit lookup down"),
        };
        var handler = await HandlerAsync(options, ContextWithCookie(token));

        var result = await handler.AuthenticateAsync();

        Assert.True(result.Succeeded);
    }
}
