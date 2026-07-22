using Microsoft.AspNetCore.Http;

namespace SyncKit.Auth;

public static class SessionIssuer {
    public static void IssueCookie(HttpResponse response, SessionCookieOptions options, SessionUser user, DateTimeOffset now) {
        var token = SessionToken.Issue(options, user, now);
        WriteCookie(response, options, token, now + options.Ttl);
    }

    public static void WriteCookie(HttpResponse response, SessionCookieOptions options, string token, DateTimeOffset expires) {
        response.Cookies.Append(options.CookieName, token, BuildCookieOptions(options, expires));
    }

    public static void ClearCookie(HttpResponse response, SessionCookieOptions options) {
        response.Cookies.Append(options.CookieName, "", BuildCookieOptions(options, DateTimeOffset.UnixEpoch));
    }

    private static CookieOptions BuildCookieOptions(SessionCookieOptions options, DateTimeOffset expires) => new() {
        Domain = options.CookieDomain,
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        Expires = expires,
    };
}
