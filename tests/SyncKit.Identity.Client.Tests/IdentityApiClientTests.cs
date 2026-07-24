using System.Linq;
using System.Net;
using System.Net.Http.Json;
using SyncKit.Contract;

namespace SyncKit.Identity.Client.Tests;

public class IdentityApiClientTests {
    private static (IdentityApiClient client, StubHttpMessageHandler handler) MakeClient(HttpResponseMessage response) {
        var handler = new StubHttpMessageHandler(_ => response);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://identity.internal") };
        http.DefaultRequestHeaders.Add("Authorization", "Bearer test-secret");
        return (new IdentityApiClient(http), handler);
    }

    [Fact]
    public async Task ResolveAsync_PostsRequestBody_AndParsesResponse() {
        var expected = new IdentityResolveResponse { UserId = Guid.NewGuid(), Role = "viewer", DiscordId = "d1", IsNew = true };
        var (client, handler) = MakeClient(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(expected) });

        var result = await client.ResolveAsync("discord", "d1", "d1", "alice", null, CancellationToken.None);

        Assert.Equal(expected.UserId, result.UserId);
        Assert.Equal("viewer", result.Role);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/identity/resolve", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"provider\":\"discord\"", handler.LastRequestBody);
        Assert.Equal("Bearer test-secret", handler.LastRequest.Headers.Authorization?.ToString().Replace("Bearer ", "Bearer "));
    }

    [Fact]
    public async Task GetAsync_NotFound_ReturnsNull() {
        var (client, _) = MakeClient(new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await client.GetAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task IsRevokedAsync_ParsesBoolBody() {
        var (client, _) = MakeClient(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(true) });

        var result = await client.IsRevokedAsync("sid-1", CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task MergeAsync_ReturnsWinnerUserId() {
        var keepId = Guid.NewGuid();
        var (client, handler) = MakeClient(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { userId = keepId }) });

        var result = await client.MergeAsync(keepId, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(keepId, result);
        Assert.Equal("/identity/merge", handler.LastRequest!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task RedeemAsync_PostsCodeAndReturnsResponse() {
        var expected = new RedeemLoginCodeResponse { UserId = Guid.NewGuid(), Role = "viewer", Username = "alice", IsNew = false };
        var (client, handler) = MakeClient(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(expected) });

        var result = await client.RedeemAsync("abc123", CancellationToken.None);

        Assert.Equal(expected.UserId, result.UserId);
        Assert.Equal("alice", result.Username);
        Assert.Equal("/identity/redeem", handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("\"code\":\"abc123\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task GetProfileAsync_SetsSessionHeader_AndParsesResponse() {
        var expected = new ProfileResponse { UserId = Guid.NewGuid(), Username = "alice", Identities = [] };
        var (client, handler) = MakeClient(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(expected) });

        var result = await client.GetProfileAsync("tok-1", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("alice", result!.Username);
        Assert.Equal("/profile/me", handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Equal("tok-1", handler.LastRequest.Headers.GetValues("X-SyncKit-Session").Single());
    }

    [Fact]
    public async Task GetProfileAsync_Unauthorized_ReturnsNull() {
        var (client, _) = MakeClient(new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var result = await client.GetProfileAsync("tok-1", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public void StartLinkUrl_BuildsRelativeUrl_WithProviderAndReturnUrl() {
        var (client, _) = MakeClient(new HttpResponseMessage(HttpStatusCode.OK));

        var url = client.StartLinkUrl("github", "https://app.example.com/settings");

        Assert.Equal("/profile/link/github/start?returnUrl=https%3A%2F%2Fapp.example.com%2Fsettings", url);
    }

    [Fact]
    public void StartRelinkUrl_BuildsRelativeUrl_WithProviderAndReturnUrl() {
        var (client, _) = MakeClient(new HttpResponseMessage(HttpStatusCode.OK));

        var url = client.StartRelinkUrl("github", "https://app.example.com/settings");

        Assert.Equal("/login/relink/github?returnUrl=https%3A%2F%2Fapp.example.com%2Fsettings", url);
    }

    [Fact]
    public async Task UnlinkIdentityAsync_PostsToUnlinkRoute() {
        var (client, handler) = MakeClient(new HttpResponseMessage(HttpStatusCode.NoContent));

        var result = await client.UnlinkIdentityAsync("tok-1", "discord", "d1", CancellationToken.None);

        Assert.True(result);
        Assert.Equal("/profile/identities/discord/d1/unlink", handler.LastRequest!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task SelectAvatarAsync_PostsProviderAndSubject() {
        var (client, handler) = MakeClient(new HttpResponseMessage(HttpStatusCode.NoContent));

        var result = await client.SelectAvatarAsync("tok-1", "authentik", "sub-1", CancellationToken.None);

        Assert.True(result);
        Assert.Equal("/profile/avatar/select", handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("\"provider\":\"authentik\"", handler.LastRequestBody);
    }
}
