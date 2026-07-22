using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using SyncKit.Auth;

namespace SyncKit.Identity.Host;

public static class ProfileAuth {
    public static async Task<Guid?> TryGetUserIdAsync(
        HttpContext ctx, SessionCookieOptions cookie, Func<string, CancellationToken, Task<bool>> isRevokedAsync, CancellationToken ct) {
        var token = ctx.Request.Headers.TryGetValue("X-SyncKit-Session", out var header) ? header.ToString()
            : ctx.Request.Cookies.TryGetValue(cookie.CookieName, out var cookieValue) ? cookieValue
            : null;
        if (string.IsNullOrEmpty(token)) return null;

        var principal = SessionToken.Validate(cookie, token, DateTimeOffset.UtcNow);
        if (principal is null) return null;

        var userId = principal.SyncKitUserId();
        if (userId is null) return null;

        var sid = principal.FindFirstValue(SessionClaims.SessionId);
        if (!string.IsNullOrEmpty(sid) && await isRevokedAsync(sid, ct)) return null;

        return userId;
    }
}
