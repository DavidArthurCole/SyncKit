using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using SyncKit.Contract;
using SyncKit.Identity.Client;

namespace SyncKit.Auth;

// Options for AuthentikAspNetAuth.AddIfConfigured. CookieScheme/claim-type strings stay
// per-app so an app's existing cookie name and claim types never change on cutover.
public sealed record AuthentikAspNetAuthOptions {
    public required string CookieScheme { get; init; }
    public required string Authority { get; init; }
    public required string ClientId { get; init; }
    public required string ClientSecret { get; init; }
    public required string CallbackPath { get; init; }
    public required string UserIdClaim { get; init; }
    public required string RoleClaim { get; init; }
    public string? DiscordIdClaim { get; init; }
    public Func<IdentityResolveResponse, ClaimsIdentity, HttpContext, Task>? OnResolved { get; init; }
}

// Shared Authentik OIDC challenge-scheme wiring for EggIncognito and EggLedger. Both apps
// previously hand-duplicated this AddOpenIdConnect setup; this is the single source now.
// Not the same as AuthentikOAuth.cs (hand-rolled PKCE flow used only by Identity.Host's own
// embedded-login-widget exchange, no ASP.NET auth scheme involved there).
public static class AuthentikAspNetAuth {
    public static bool AddIfConfigured(AuthenticationBuilder builder, AuthentikAspNetAuthOptions? options) {
        if (options is null) return false;

        builder.AddOpenIdConnect(o => {
            o.Authority = options.Authority;
            o.ClientId = options.ClientId;
            o.ClientSecret = options.ClientSecret;
            o.ResponseType = OpenIdConnectResponseType.Code;
            o.SignInScheme = options.CookieScheme;
            o.CallbackPath = options.CallbackPath;
            o.CorrelationCookie.SameSite = SameSiteMode.None;
            o.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
            o.NonceCookie.SameSite = SameSiteMode.None;
            o.NonceCookie.SecurePolicy = CookieSecurePolicy.Always;
            o.Scope.Clear();
            o.Scope.Add("openid");
            o.Scope.Add("profile");
            o.Scope.Add("email");
            o.Scope.Add("discord_id");
            o.MapInboundClaims = false;
            o.SaveTokens = true;
            o.GetClaimsFromUserInfoEndpoint = true;
            o.Events.OnTicketReceived = async ctx => {
                var principal = ctx.Principal!;
                var sub = principal.FindFirstValue("sub");
                if (string.IsNullOrEmpty(sub)) {
                    var logger = ctx.HttpContext.RequestServices
                        .GetRequiredService<ILoggerFactory>().CreateLogger("AuthentikAspNetAuth");
                    logger.LogWarning("Authentik ticket missing sub claim; claims: {Claims}",
                        string.Join(", ", principal.Claims.Select(c => c.Type)));
                    ctx.Response.Redirect("/?login=failed");
                    ctx.HandleResponse();
                    return;
                }
                var discordId = principal.FindFirstValue("discord_id");
                var username = principal.FindFirstValue("preferred_username") ?? principal.FindFirstValue(ClaimTypes.Name);
                // Resolved lazily per-request, not captured at registration time: EggIncognito
                // registers IdentityApiClient via AddHttpClient<T> (typed client, only resolvable
                // through DI, never by eagerly building a throwaway ServiceProvider mid-registration).
                var identityClient = ctx.HttpContext.RequestServices.GetRequiredService<IdentityApiClient>();
                var result = await identityClient.ResolveAsync(
                    "authentik", sub, discordId, username, avatar: null, ctx.HttpContext.RequestAborted);

                var identity = (ClaimsIdentity)principal.Identity!;
                identity.AddClaim(new Claim(options.UserIdClaim, result.UserId.ToString()));
                identity.AddClaim(new Claim(options.RoleClaim, result.Role));
                if (options.DiscordIdClaim is not null && !string.IsNullOrEmpty(discordId)) {
                    identity.AddClaim(new Claim(options.DiscordIdClaim, discordId));
                }

                if (options.OnResolved is not null) {
                    await options.OnResolved(result, identity, ctx.HttpContext);
                }
            };
            o.Events.OnRemoteFailure = ctx => {
                var logger = ctx.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>().CreateLogger("AuthentikAspNetAuth");
                logger.LogWarning(ctx.Failure, "Authentik remote auth failure");
                ctx.Response.Redirect("/?login=failed");
                ctx.HandleResponse();
                return Task.CompletedTask;
            };
        });
        return true;
    }

    // Shared cookie OnValidatePrincipal hook: rejects and signs out a principal whose sid claim
    // is revoked (Authentik back-channel logout), and refreshes the role claim from SyncKit's
    // live users.role column so a role change takes effect on the next request instead of next login.
    public static async Task OnValidatePrincipalCheckRevoked(
        CookieValidatePrincipalContext ctx, IdentityApiClient identity, string userIdClaimType, string roleClaimType) {
        var sid = ctx.Principal?.FindFirstValue("sid");
        if (!string.IsNullOrEmpty(sid) && await identity.IsRevokedAsync(sid, ctx.HttpContext.RequestAborted)) {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(ctx.Scheme.Name);
            return;
        }

        var userIdClaim = ctx.Principal?.FindFirstValue(userIdClaimType);
        if (userIdClaim is null || !Guid.TryParse(userIdClaim, out var userId)) return;

        var user = await identity.GetAsync(userId, ctx.HttpContext.RequestAborted);
        if (user is null) return;

        var currentRole = ctx.Principal!.FindFirstValue(roleClaimType);
        if (currentRole == user.Role) return;

        var claimsIdentity = (ClaimsIdentity)ctx.Principal!.Identity!;
        var existing = claimsIdentity.FindFirst(roleClaimType);
        if (existing is not null) claimsIdentity.RemoveClaim(existing);
        claimsIdentity.AddClaim(new Claim(roleClaimType, user.Role));
        ctx.ShouldRenew = true;
    }
}
