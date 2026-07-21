using SyncKit.Contract;

namespace SyncKit.Metrics;

public sealed class RequestRateBuffer(TimeProvider clock) {
    private const int Minutes = 60;
    private readonly Bucket[] _buckets = new Bucket[Minutes];
    private readonly Lock _gate = new();

    public void Record(bool limited) {
        var minute = CurrentMinute();
        lock (_gate) {
            ref var slot = ref _buckets[(int)(minute % Minutes)];
            if (slot.Minute != minute) {
                slot.Minute = minute;
                slot.Total = 0;
                slot.Limited = 0;
            }
            slot.Total++;
            if (limited) slot.Limited++;
        }
    }

    public IReadOnlyList<RateMinute> Snapshot() {
        var now = CurrentMinute();
        var result = new List<RateMinute>(Minutes);
        lock (_gate) {
            for (var age = Minutes - 1; age >= 0; age--) {
                var minute = now - age;
                if (minute < 0) continue;
                ref var slot = ref _buckets[(int)(minute % Minutes)];
                var total = slot.Minute == minute ? slot.Total : 0;
                var limited = slot.Minute == minute ? slot.Limited : 0;
                result.Add(new RateMinute(minute * 60, total, limited));
            }
        }
        return result;
    }

    private long CurrentMinute() => clock.GetUtcNow().ToUnixTimeSeconds() / 60;

    private struct Bucket {
        public long Minute;
        public int Total;
        public int Limited;
    }
}
