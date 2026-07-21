namespace SyncKit.Metrics.Tests;

public sealed class FixedClock(DateTimeOffset start) : TimeProvider {
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
