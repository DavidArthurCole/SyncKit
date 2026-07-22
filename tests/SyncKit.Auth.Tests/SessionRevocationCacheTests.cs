using SyncKit.Auth;
using Xunit;

namespace SyncKit.Auth.Tests;

public class SessionRevocationCacheTests {
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task LiveResult_CachedWithinTtl_FetchesOnce() {
        var clock = new FixedClock(Now);
        var cache = new SessionRevocationCache(clock, TimeSpan.FromSeconds(30));
        var calls = 0;

        Task<bool> Fetch(CancellationToken ct) { calls++; return Task.FromResult(false); }

        Assert.False(await cache.IsRevokedAsync("sid", Fetch, default));
        Assert.False(await cache.IsRevokedAsync("sid", Fetch, default));

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task LiveResult_RefetchesAfterTtl() {
        var clock = new FixedClock(Now);
        var cache = new SessionRevocationCache(clock, TimeSpan.FromSeconds(30));
        var calls = 0;

        Task<bool> Fetch(CancellationToken ct) { calls++; return Task.FromResult(false); }

        Assert.False(await cache.IsRevokedAsync("sid", Fetch, default));
        clock.Advance(TimeSpan.FromSeconds(31));
        Assert.False(await cache.IsRevokedAsync("sid", Fetch, default));

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task RevokedResult_CachedLonger() {
        var clock = new FixedClock(Now);
        var cache = new SessionRevocationCache(clock, TimeSpan.FromSeconds(30));
        var calls = 0;

        Task<bool> Fetch(CancellationToken ct) { calls++; return Task.FromResult(true); }

        Assert.True(await cache.IsRevokedAsync("sid", Fetch, default));
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.True(await cache.IsRevokedAsync("sid", Fetch, default));

        Assert.Equal(1, calls);
    }
}
