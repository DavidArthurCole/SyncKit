using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace SyncKit.Auth;

public static class SessionToken {
    private static readonly TimeSpan Skew = TimeSpan.FromSeconds(60);

    private static readonly string[] CarriedClaims = [
        JwtRegisteredClaimNames.Sub,
        SessionClaims.Role,
        SessionClaims.SessionId,
        SessionClaims.Name,
        SessionClaims.Avatar,
        SessionClaims.DiscordId,
    ];

    public static string Issue(SessionCookieOptions options, SessionUser user, DateTimeOffset now) {
        var claims = new List<Claim> {
            new(JwtRegisteredClaimNames.Sub, user.UserId),
            new(SessionClaims.Role, user.Role),
        };
        if (!string.IsNullOrEmpty(user.Sid)) claims.Add(new Claim(SessionClaims.SessionId, user.Sid));
        if (!string.IsNullOrEmpty(user.Name)) claims.Add(new Claim(SessionClaims.Name, user.Name));
        if (!string.IsNullOrEmpty(user.Avatar)) claims.Add(new Claim(SessionClaims.Avatar, user.Avatar));
        if (!string.IsNullOrEmpty(user.DiscordId)) claims.Add(new Claim(SessionClaims.DiscordId, user.DiscordId));

        return Write(options, claims, now);
    }

    public static string Renew(SessionCookieOptions options, ClaimsPrincipal principal, DateTimeOffset now) {
        var claims = new List<Claim>();
        foreach (var type in CarriedClaims) {
            var value = principal.FindFirstValue(type);
            if (!string.IsNullOrEmpty(value)) claims.Add(new Claim(type, value));
        }
        return Write(options, claims, now);
    }

    public static bool ShouldRenew(ClaimsPrincipal principal, SessionCookieOptions options, DateTimeOffset now) {
        var expValue = principal.FindFirstValue(JwtRegisteredClaimNames.Exp);
        if (!long.TryParse(expValue, out var expUnix)) return false;
        var expires = DateTimeOffset.FromUnixTimeSeconds(expUnix);
        return expires - now < options.Ttl / 2;
    }

    public static ClaimsPrincipal? Validate(SessionCookieOptions options, string token, DateTimeOffset now) {
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var parameters = new TokenValidationParameters {
            ValidIssuer = options.Issuer,
            ValidAudience = options.Audience,
            IssuerSigningKeys = ValidationKeys(options),
            ValidateIssuerSigningKey = true,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            LifetimeValidator = (notBefore, expires, _, _) => {
                if (notBefore.HasValue && now.UtcDateTime + Skew < notBefore.Value) return false;
                if (expires.HasValue && now.UtcDateTime - Skew > expires.Value) return false;
                return true;
            },
        };

        try {
            return handler.ValidateToken(token, parameters, out _);
        } catch (Exception) {
            return null;
        }
    }

    private static string Write(SessionCookieOptions options, List<Claim> claims, DateTimeOffset now) {
        var creds = new SigningCredentials(KeyFrom(options.SigningSecret), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: (now + options.Ttl).UtcDateTime,
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static IEnumerable<SecurityKey> ValidationKeys(SessionCookieOptions options) {
        yield return KeyFrom(options.SigningSecret);
        if (!string.IsNullOrEmpty(options.PreviousSigningSecret))
            yield return KeyFrom(options.PreviousSigningSecret);
    }

    private static SymmetricSecurityKey KeyFrom(string secret) => new(Encoding.UTF8.GetBytes(secret));
}
