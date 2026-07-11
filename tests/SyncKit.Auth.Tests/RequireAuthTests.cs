using Microsoft.AspNetCore.Http;
using SyncKit.Auth;
using Xunit;

namespace SyncKit.Auth.Tests;

public class RequireAuthTests {
    [Theory]
    [InlineData("Bearer abc123", "abc123")]
    [InlineData("abc123", "abc123")]      // Go TrimPrefix leaves bare token unchanged
    [InlineData("", "")]
    public void ExtractToken_StripsBearerPrefix(string header, string expected) {
        Assert.Equal(expected, RequireAuth.ExtractToken(header));
    }

    [Fact]
    public async Task Invoke_NoToken_Returns401() {
        var ctx = new DefaultHttpContext();
        var called = false;
        var mw = new RequireAuth(_ => { called = true; return Task.CompletedTask; }, new ThrowingSessionStore());
        await mw.Invoke(ctx);
        Assert.False(called);
        Assert.Equal(401, ctx.Response.StatusCode);
    }

    // Session store that must not be hit when there is no token.
    private sealed class ThrowingSessionStore : ISessionStore {
        public Task<(bool Found, string DiscordId, long ExpiresAt)> LookupAsync(string token, CancellationToken ct)
            => throw new Xunit.Sdk.XunitException("session store should not be queried without a token");
        public Task TouchAsync(string token, long newExpiresAt, CancellationToken ct)
            => Task.CompletedTask;
    }
}
