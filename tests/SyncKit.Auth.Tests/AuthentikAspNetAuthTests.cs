using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SyncKit.Auth;
using SyncKit.Contract;
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

        await AuthentikAspNetAuth.OnValidatePrincipalCheckRevoked(ctx, identity, "user_id", "role");

        Assert.False(ctx.ShouldRenew == false && ctx.Principal is null); // principal untouched, no reject
        Assert.NotNull(ctx.Principal);
    }

    private sealed class StubIdentityHandler(IdentityUserResponse? getResult, bool revoked) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.EndsWith("/revoked")) {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) {
                    Content = System.Net.Http.Json.JsonContent.Create(revoked)
                });
            }
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) {
                Content = System.Net.Http.Json.JsonContent.Create(getResult)
            });
        }
    }

    private static IdentityApiClient StubIdentityClient(IdentityUserResponse? getResult, bool revoked = false) =>
        new(new HttpClient(new StubIdentityHandler(getResult, revoked)) { BaseAddress = new Uri("http://localhost:8090") });

    [Fact]
    public async Task OnValidatePrincipalCheckRevoked_role_unchanged_does_not_replace_claim() {
        var userId = Guid.NewGuid();
        var identity = StubIdentityClient(new IdentityUserResponse { UserId = userId, Role = "contributor", Username = "alice" });
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("user_id", userId.ToString()), new Claim("sid", "sid-1"), new Claim("role", "contributor")],
            "test_cookie"));
        var ctx = MakeContext(principal);

        await AuthentikAspNetAuth.OnValidatePrincipalCheckRevoked(ctx, identity, "user_id", "role");

        Assert.Single(ctx.Principal!.FindAll("role"));
        Assert.Equal("contributor", ctx.Principal!.FindFirstValue("role"));
    }

    [Fact]
    public async Task OnValidatePrincipalCheckRevoked_role_changed_replaces_claim() {
        var userId = Guid.NewGuid();
        var identity = StubIdentityClient(new IdentityUserResponse { UserId = userId, Role = "admin", Username = "alice" });
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("user_id", userId.ToString()), new Claim("sid", "sid-1"), new Claim("role", "contributor")],
            "test_cookie"));
        var ctx = MakeContext(principal);

        await AuthentikAspNetAuth.OnValidatePrincipalCheckRevoked(ctx, identity, "user_id", "role");

        Assert.Single(ctx.Principal!.FindAll("role"));
        Assert.Equal("admin", ctx.Principal!.FindFirstValue("role"));
    }

    [Fact]
    public async Task OnValidatePrincipalCheckRevoked_no_user_id_claim_skips_role_refresh() {
        var identity = StubIdentityClient(new IdentityUserResponse { UserId = Guid.NewGuid(), Role = "admin" });
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sid", "sid-1"), new Claim("role", "contributor")], "test_cookie"));
        var ctx = MakeContext(principal);

        await AuthentikAspNetAuth.OnValidatePrincipalCheckRevoked(ctx, identity, "user_id", "role");

        Assert.Equal("contributor", ctx.Principal!.FindFirstValue("role"));
    }

    private static CookieValidatePrincipalContext MakeContext(ClaimsPrincipal principal) {
        var httpContext = new DefaultHttpContext();
        var scheme = new AuthenticationScheme("test_cookie", "test_cookie", typeof(CookieAuthenticationHandler));
        var ticket = new AuthenticationTicket(principal, "test_cookie");
        return new CookieValidatePrincipalContext(httpContext, scheme, new CookieAuthenticationOptions(), ticket);
    }
}
