using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace SyncKit.Auth;

public static class SessionToken {
    private static readonly TimeSpan Skew = TimeSpan.FromSeconds(60);

    public static string Issue(SessionCookieOptions options, SessionUser user, DateTimeOffset now) {
        var exp = now + options.Ttl;
        var creds = new SigningCredentials(SigningKey(options), SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim> {
            new(JwtRegisteredClaimNames.Sub, user.UserId),
            new(SessionClaims.Role, user.Role),
        };
        if (!string.IsNullOrEmpty(user.Sid)) claims.Add(new Claim(SessionClaims.SessionId, user.Sid));
        if (!string.IsNullOrEmpty(user.Name)) claims.Add(new Claim(SessionClaims.Name, user.Name));
        if (!string.IsNullOrEmpty(user.Avatar)) claims.Add(new Claim(SessionClaims.Avatar, user.Avatar));
        if (!string.IsNullOrEmpty(user.DiscordId)) claims.Add(new Claim(SessionClaims.DiscordId, user.DiscordId));

        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: exp.UtcDateTime,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static ClaimsPrincipal? Validate(SessionCookieOptions options, string token, DateTimeOffset now) {
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var parameters = new TokenValidationParameters {
            ValidIssuer = options.Issuer,
            ValidAudience = options.Audience,
            IssuerSigningKey = SigningKey(options),
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

    private static SymmetricSecurityKey SigningKey(SessionCookieOptions options) =>
        new(Encoding.UTF8.GetBytes(options.SigningSecret));
}
