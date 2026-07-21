using SyncKit.Metrics;

namespace SyncKit.Metrics.Tests;

public class RequestRateBufferTests {
    private static readonly DateTimeOffset Start = new(2026, 7, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Record_countsTotalAndLimitedInCurrentMinute() {
        var clock = new FixedClock(Start);
        var buffer = new RequestRateBuffer(clock);

        buffer.Record(false);
        buffer.Record(true);
        buffer.Record(false);

        var last = buffer.Snapshot()[^1];
        Assert.Equal(3, last.Total);
        Assert.Equal(1, last.Limited);
    }

    [Fact]
    public void Record_separatesMinutes() {
        var clock = new FixedClock(Start);
        var buffer = new RequestRateBuffer(clock);

        buffer.Record(false);
        clock.Advance(TimeSpan.FromMinutes(1));
        buffer.Record(false);
        buffer.Record(false);

        var snap = buffer.Snapshot();
        Assert.Equal(1, snap[^2].Total);
        Assert.Equal(2, snap[^1].Total);
    }

    [Fact]
    public void Snapshot_dropsBucketsOlderThanWindow() {
        var clock = new FixedClock(Start);
        var buffer = new RequestRateBuffer(clock);

        buffer.Record(false);
        clock.Advance(TimeSpan.FromMinutes(90));

        var snap = buffer.Snapshot();
        Assert.All(snap, m => Assert.Equal(0, m.Total));
    }

    [Fact]
    public void Snapshot_minuteEpochIsSixtySecondAligned() {
        var buffer = new RequestRateBuffer(new FixedClock(Start));
        buffer.Record(false);
        var last = buffer.Snapshot()[^1];
        Assert.Equal(0, last.MinuteEpochSeconds % 60);
    }
}
