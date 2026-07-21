using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SyncKit.Metrics;

namespace SyncKit.Metrics.Tests;

public class RequestMetricsMiddlewareTests {
    private static readonly DateTimeOffset Start = new(2026, 7, 21, 12, 0, 0, TimeSpan.Zero);

    private sealed class CapturingSink : IRequestAuditSink {
        public AuditEntry? Last;
        public Task RecordAsync(AuditEntry entry, CancellationToken ct) {
            Last = entry;
            return Task.CompletedTask;
        }
    }

    private static (RequestMetricsMiddleware mw, RequestRateBuffer rate, RequestAuditLog audit) Build(
        RequestDelegate next, RequestMetricsOptions? opts = null) {
        opts ??= new RequestMetricsOptions();
        var clock = new FixedClock(Start);
        var rate = new RequestRateBuffer(clock);
        var audit = new RequestAuditLog(clock, opts);
        var classifier = new RequestBucketClassifier(opts);
        return (new RequestMetricsMiddleware(next, rate, audit, classifier, opts), rate, audit);
    }

    [Fact]
    public async Task RecordsRequestUnderPrefixAndFlags429() {
        var (mw, rate, audit) = Build(c => {
            c.Response.StatusCode = 429;
            return Task.CompletedTask;
        });
        var ctx = new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() };
        ctx.Request.Path = "/api/thing";
        ctx.Request.Method = "GET";

        await mw.InvokeAsync(ctx);

        var last = rate.Snapshot()[^1];
        Assert.Equal(1, last.Total);
        Assert.Equal(1, last.Limited);
        Assert.Equal("/api/thing", Assert.Single(audit.Paths()).Path);
    }

    [Fact]
    public async Task IgnoresRequestsOutsidePrefix() {
        var (mw, rate, audit) = Build(c => Task.CompletedTask);
        var ctx = new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() };
        ctx.Request.Path = "/health";
        ctx.Request.Method = "GET";

        await mw.InvokeAsync(ctx);

        Assert.All(rate.Snapshot(), m => Assert.Equal(0, m.Total));
        Assert.Empty(audit.Paths());
    }

    [Fact]
    public async Task FiresRegisteredSink() {
        var sink = new CapturingSink();
        var (mw, _, _) = Build(c => Task.CompletedTask);
        var services = new ServiceCollection();
        services.AddSingleton<IRequestAuditSink>(sink);
        var ctx = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        ctx.Request.Path = "/api/keyed";
        ctx.Request.Method = "POST";

        await mw.InvokeAsync(ctx);

        Assert.NotNull(sink.Last);
        Assert.Equal("/api/keyed", sink.Last!.Path);
        Assert.Equal("POST", sink.Last.Method);
    }
}
