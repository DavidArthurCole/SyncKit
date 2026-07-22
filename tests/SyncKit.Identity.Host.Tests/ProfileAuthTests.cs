using Microsoft.AspNetCore.Http;
using SyncKit.Auth;
using SyncKit.Identity.Host;
using Xunit;

namespace SyncKit.Identity.Host.Tests;

public class ProfileAuthTests {
    private static readonly SessionCookieOptions Cookie = new() { SigningSecret = "test-secret-at-least-32-bytes-long!!" };

    [Fact]
    public async Task TryGetUserIdAsync_NoTokenAnywhere_ReturnsNull() {
        var ctx = new DefaultHttpContext();

        var result = await ProfileAuth.TryGetUserIdAsync(ctx, Cookie, (_, _) => Task.FromResult(false), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task TryGetUserIdAsync_ValidHeaderToken_ReturnsUserId() {
        var userId = Guid.NewGuid();
        var token = SessionToken.Issue(Cookie, new SessionUser(UserId: userId.ToString(), Sid: "sid-1", Role: "viewer", Name: "alice", Avatar: null, DiscordId: null), DateTimeOffset.UtcNow);
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-SyncKit-Session"] = token;

        var result = await ProfileAuth.TryGetUserIdAsync(ctx, Cookie, (_, _) => Task.FromResult(false), CancellationToken.None);

        Assert.Equal(userId, result);
    }

    [Fact]
    public async Task TryGetUserIdAsync_RevokedSid_ReturnsNull() {
        var userId = Guid.NewGuid();
        var token = SessionToken.Issue(Cookie, new SessionUser(UserId: userId.ToString(), Sid: "sid-revoked", Role: "viewer", Name: "alice", Avatar: null, DiscordId: null), DateTimeOffset.UtcNow);
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-SyncKit-Session"] = token;

        var result = await ProfileAuth.TryGetUserIdAsync(ctx, Cookie, (_, _) => Task.FromResult(true), CancellationToken.None);

        Assert.Null(result);
    }
}
