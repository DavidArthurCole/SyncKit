using SyncKit.Contract;

namespace SyncKit.Metrics;

public sealed class TrafficReporter(RequestRateBuffer rate, RequestAuditLog audit) {
    public TrafficSnapshot Snapshot(int recentTake = 200) => new(
        rate.Snapshot(),
        audit.Paths(),
        audit.Ips(),
        audit.Recent(recentTake),
        audit.Buckets());
}
