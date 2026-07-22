namespace SyncKit.Auth;

public sealed class SessionCookieOptions {
    public required string SigningSecret { get; init; }
    public string CookieName { get; init; } = "synckit_session";
    public string? CookieDomain { get; init; }
    public string Issuer { get; init; } = "eggidentity";
    public string Audience { get; init; } = "synckit-apps";
    public TimeSpan Ttl { get; init; } = TimeSpan.FromMinutes(480);

    public static SessionCookieOptions? FromEnvironment() {
        var secret = Environment.GetEnvironmentVariable("SYNCKIT_SESSION_SECRET");
        if (string.IsNullOrEmpty(secret)) return null;

        var ttlMinutes = int.TryParse(Environment.GetEnvironmentVariable("SYNCKIT_SESSION_TTL_MINUTES"), out var m) && m > 0
            ? m : 480;

        return new SessionCookieOptions {
            SigningSecret = secret,
            CookieName = Fallback("SYNCKIT_SESSION_COOKIE_NAME", "synckit_session"),
            CookieDomain = NullIfEmpty(Environment.GetEnvironmentVariable("SYNCKIT_SESSION_COOKIE_DOMAIN")),
            Issuer = Fallback("SYNCKIT_SESSION_ISSUER", "eggidentity"),
            Audience = Fallback("SYNCKIT_SESSION_AUDIENCE", "synckit-apps"),
            Ttl = TimeSpan.FromMinutes(ttlMinutes),
        };
    }

    private static string Fallback(string name, string fallback) {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrEmpty(value) ? fallback : value;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
