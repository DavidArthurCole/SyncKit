using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using SyncKit.Auth;
using SyncKit.Contract;
using Xunit;

namespace SyncKit.Auth.Tests;

public class ClaimsPrincipalExtensionsTests {
    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "test"));

    [Fact]
    public void SyncKitRole_ParsesRoleClaim() {
        var principal = Principal(new Claim(SessionClaims.Role, "admin"));

        Assert.Equal(UserRole.Admin, principal.SyncKitRole());
    }

    [Fact]
    public void SyncKitRole_MissingClaim_DefaultsToViewer() {
        Assert.Equal(UserRole.Viewer, Principal().SyncKitRole());
    }

    [Fact]
    public void IsAtLeast_AdminMeetsContributor() {
        var principal = Principal(new Claim(SessionClaims.Role, "admin"));

        Assert.True(principal.IsAtLeast(UserRole.Contributor));
        Assert.True(principal.IsAtLeast(UserRole.Admin));
    }

    [Fact]
    public void IsAtLeast_ViewerFailsContributor() {
        var principal = Principal(new Claim(SessionClaims.Role, "viewer"));

        Assert.False(principal.IsAtLeast(UserRole.Contributor));
    }

    [Fact]
    public void SyncKitUserId_ParsesSubClaim() {
        var id = Guid.NewGuid();
        var principal = Principal(new Claim(JwtRegisteredClaimNames.Sub, id.ToString()));

        Assert.Equal(id, principal.SyncKitUserId());
    }

    [Fact]
    public void SyncKitUserId_MissingClaim_ReturnsNull() {
        Assert.Null(Principal().SyncKitUserId());
    }
}
