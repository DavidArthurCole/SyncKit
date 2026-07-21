using SyncKit.Contract;

namespace SyncKit.Metrics.AdminUi;

public interface ITrafficSource {
    Task<TrafficSnapshot> GetSnapshotAsync(CancellationToken ct);
}
