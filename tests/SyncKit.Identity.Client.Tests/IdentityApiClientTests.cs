using System.Net;
using System.Net.Http.Json;
using SyncKit.Contract;

namespace SyncKit.Identity.Client.Tests;

public class IdentityApiClientTests
{
    private static (IdentityApiClient client, StubHttpMessageHandler handler) MakeClient(HttpResponseMessage response)
    {
        var handler = new StubHttpMessageHandler(_ => response);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://identity.internal") };
        http.DefaultRequestHeaders.Add("Authorization", "Bearer test-secret");
        return (new IdentityApiClient(http), handler);
    }

    [Fact]
    public async Task ResolveAsync_PostsRequestBody_AndParsesResponse()
    {
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
    public async Task GetAsync_NotFound_ReturnsNull()
    {
        var (client, _) = MakeClient(new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await client.GetAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task IsRevokedAsync_ParsesBoolBody()
    {
        var (client, _) = MakeClient(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(true) });

        var result = await client.IsRevokedAsync("sid-1", CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task MergeAsync_ReturnsWinnerUserId()
    {
        var keepId = Guid.NewGuid();
        var (client, handler) = MakeClient(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { userId = keepId }) });

        var result = await client.MergeAsync(keepId, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(keepId, result);
        Assert.Equal("/identity/merge", handler.LastRequest!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task RedeemAsync_PostsCodeAndReturnsResponse()
    {
        var expected = new RedeemLoginCodeResponse { UserId = Guid.NewGuid(), Role = "viewer", Username = "alice", IsNew = false };
        var (client, handler) = MakeClient(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(expected) });

        var result = await client.RedeemAsync("abc123", CancellationToken.None);

        Assert.Equal(expected.UserId, result.UserId);
        Assert.Equal("alice", result.Username);
        Assert.Equal("/identity/redeem", handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("\"code\":\"abc123\"", handler.LastRequestBody);
    }
}
