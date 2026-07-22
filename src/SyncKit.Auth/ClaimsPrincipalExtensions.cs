using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using SyncKit.Contract;

namespace SyncKit.Auth;

public static class ClaimsPrincipalExtensions {
    public static Guid? SyncKitUserId(this ClaimsPrincipal principal) {
        var raw = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    public static UserRole SyncKitRole(this ClaimsPrincipal principal) =>
        UserRoles.Parse(principal.FindFirstValue(SessionClaims.Role));

    public static bool IsAtLeast(this ClaimsPrincipal principal, UserRole need) =>
        UserRoles.IsAtLeast(principal.SyncKitRole(), need);
}
