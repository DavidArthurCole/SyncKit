using SyncKit.Contract;

namespace SyncKit.Contract.Tests;

public class UserRolesTests {
    [Theory]
    [InlineData("admin", UserRole.Admin)]
    [InlineData("Admin", UserRole.Admin)]
    [InlineData("contributor", UserRole.Contributor)]
    [InlineData("viewer", UserRole.Viewer)]
    [InlineData("", UserRole.Viewer)]
    [InlineData(null, UserRole.Viewer)]
    [InlineData("nonsense", UserRole.Viewer)]
    public void Parse_mapsKnownRolesAndDefaultsToViewer(string? input, UserRole expected) =>
        Assert.Equal(expected, UserRoles.Parse(input));

    [Theory]
    [InlineData(UserRole.Admin, "admin")]
    [InlineData(UserRole.Contributor, "contributor")]
    [InlineData(UserRole.Viewer, "viewer")]
    public void ToName_isLowercase(UserRole role, string expected) =>
        Assert.Equal(expected, UserRoles.ToName(role));

    [Fact]
    public void IsAtLeast_comparesTier() {
        Assert.True(UserRoles.IsAtLeast(UserRole.Admin, UserRole.Contributor));
        Assert.True(UserRoles.IsAtLeast(UserRole.Contributor, UserRole.Contributor));
        Assert.False(UserRoles.IsAtLeast(UserRole.Viewer, UserRole.Admin));
    }

    [Fact]
    public void Roundtrip_nameThenParse() =>
        Assert.Equal(UserRole.Contributor, UserRoles.Parse(UserRoles.ToName(UserRole.Contributor)));
}
