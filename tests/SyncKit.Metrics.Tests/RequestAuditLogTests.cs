using SyncKit.Metrics;

namespace SyncKit.Metrics.Tests;

public class RequestAuditLogTests {
    private static readonly DateTimeOffset Start = new(2026, 7, 21, 12, 0, 0, TimeSpan.Zero);

    private static RequestAuditLog New(RequestMetricsOptions? opts = null) =>
        new(new FixedClock(Start), opts ?? new RequestMetricsOptions());

    [Fact]
    public void Paths_aggregatesAndSortsDescending() {
        var log = New();
        log.Record("GET", "/api/a", 200, RequestBucket.External, "1.1.1.1", null);
        log.Record("GET", "/api/a", 200, RequestBucket.External, "1.1.1.1", null);
        log.Record("GET", "/api/b", 200, RequestBucket.External, "1.1.1.1", null);

        var paths = log.Paths();
        Assert.Equal("/api/a", paths[0].Path);
        Assert.Equal(2, paths[0].Count);
        Assert.Equal(1, paths[1].Count);
    }

    [Fact]
    public void Ips_aggregatesCountAndLastSeen() {
        var log = New();
        log.Record("GET", "/api/a", 200, RequestBucket.External, "9.9.9.9", null);
        log.Record("GET", "/api/a", 200, RequestBucket.External, "9.9.9.9", null);

        var ip = Assert.Single(log.Ips());
        Assert.Equal("9.9.9.9", ip.Ip);
        Assert.Equal(2, ip.Count);
        Assert.Equal(Start.ToUnixTimeSeconds(), ip.LastSeenEpochSeconds);
    }

    [Fact]
    public void Buckets_countsByClassification() {
        var log = New();
        log.Record("GET", "/api/a", 200, RequestBucket.Internal, "1.1.1.1", null);
        log.Record("GET", "/api/a", 200, RequestBucket.Cross, "1.1.1.1", null);
        log.Record("GET", "/api/a", 200, RequestBucket.External, "1.1.1.1", null);
        log.Record("GET", "/api/a", 429, RequestBucket.External, "1.1.1.1", null);

        var b = log.Buckets();
        Assert.Equal(1, b.Internal);
        Assert.Equal(1, b.Cross);
        Assert.Equal(2, b.External);
        Assert.False(b.KeysCapped);
    }

    [Fact]
    public void Paths_capAtMaxKeysAndFlagCapped() {
        var log = New(new RequestMetricsOptions { MaxKeys = 2 });
        log.Record("GET", "/api/a", 200, RequestBucket.External, "1.1.1.1", null);
        log.Record("GET", "/api/b", 200, RequestBucket.External, "1.1.1.1", null);
        log.Record("GET", "/api/c", 200, RequestBucket.External, "1.1.1.1", null);

        var paths = log.Paths();
        Assert.Equal(2, paths.Count);
        Assert.DoesNotContain(paths, p => p.Path == "/api/c");
        Assert.True(log.Buckets().KeysCapped);
    }

    [Fact]
    public void Recent_returnsNewestFirstWithinCapacity() {
        var log = New(new RequestMetricsOptions { RecentCapacity = 2 });
        log.Record("GET", "/api/1", 200, RequestBucket.External, "1.1.1.1", null);
        log.Record("GET", "/api/2", 200, RequestBucket.External, "1.1.1.1", null);
        log.Record("GET", "/api/3", 200, RequestBucket.External, "1.1.1.1", null);

        var recent = log.Recent(10);
        Assert.Equal(2, recent.Count);
        Assert.Equal("/api/3", recent[0].Path);
        Assert.Equal("/api/2", recent[1].Path);
    }

    [Fact]
    public void Recent_bucketNameIsLowercase() {
        var log = New();
        log.Record("GET", "/api/a", 200, RequestBucket.Internal, "1.1.1.1", null);
        Assert.Equal("internal", log.Recent(1)[0].Bucket);
    }
}
