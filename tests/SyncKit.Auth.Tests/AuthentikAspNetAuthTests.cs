using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SyncKit.Auth;
using SyncKit.Identity.Client;
using Xunit;

namespace SyncKit.Auth.Tests;

public class AuthentikAspNetAuthTests {
    private static IdentityApiClient UnusedIdentityClient() =>
        new(new HttpClient { BaseAddress = new Uri("http://localhost:8090") });

    private static AuthentikAspNetAuthOptions Options(string cookieScheme = "test_cookie") => new() {
        CookieScheme = cookieScheme,
        Authority = "https://auth.example.com/application/o/test/",
        ClientId = "client123",
        ClientSecret = "secret",
        CallbackPath = "/auth-callback",
        UserIdClaim = "user_id",
        RoleClaim = "role",
    };

    [Fact]
    public async Task AddIfConfigured_registers_oidc_scheme_when_options_present() {
        var services = new ServiceCollection();
        var builder = services.AddAuthentication("test_cookie").AddCookie("test_cookie");
        var registered = AuthentikAspNetAuth.AddIfConfigured(builder, Options());
        Assert.True(registered);

        var provider = services.BuildServiceProvider();
        var schemeProvider = provider.GetRequiredService<IAuthenticationSchemeProvider>();
        var scheme = await schemeProvider.GetSchemeAsync(OpenIdConnectDefaults.AuthenticationScheme);
        Assert.NotNull(scheme);
    }

    [Fact]
    public async Task AddIfConfigured_noop_when_options_null() {
        var services = new ServiceCollection();
        var builder = services.AddAuthentication("test_cookie").AddCookie("test_cookie");
        var registered = AuthentikAspNetAuth.AddIfConfigured(builder, null);
        Assert.False(registered);

        var provider = services.BuildServiceProvider();
        var schemeProvider = provider.GetRequiredService<IAuthenticationSchemeProvider>();
        var scheme = await schemeProvider.GetSchemeAsync(OpenIdConnectDefaults.AuthenticationScheme);
        Assert.Null(scheme);
    }

    [Fact]
    public async Task OnValidatePrincipalCheckRevoked_noop_when_no_sid_claim() {
        var identity = UnusedIdentityClient();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("user_id", "abc")], "test_cookie"));
        var ctx = MakeContext(principal);

        await AuthentikAspNetAuth.OnValidatePrincipalCheckRevoked(ctx, identity);

        Assert.False(ctx.ShouldRenew == false && ctx.Principal is null); // principal untouched, no reject
        Assert.NotNull(ctx.Principal);
    }

    private static CookieValidatePrincipalContext MakeContext(ClaimsPrincipal principal) {
        var httpContext = new DefaultHttpContext();
        var scheme = new AuthenticationScheme("test_cookie", "test_cookie", typeof(CookieAuthenticationHandler));
        var ticket = new AuthenticationTicket(principal, "test_cookie");
        return new CookieValidatePrincipalContext(httpContext, scheme, new CookieAuthenticationOptions(), ticket);
    }
}
