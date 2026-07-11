using SyncKit.Contract;

namespace SyncKit.Agent;

// Single-flight deploy runner shared by the HTTP endpoint and the watcher, so a manual and an auto
// deploy never overlap. Mirrors the Go Handler. TryRun returns (result, ran): ran=false means another
// deploy held the lock and this call was skipped. Bearer-auth is enforced at the HTTP layer (Program).
public sealed class DeployHandler(Func<DeployResponse> run) {
    private readonly Lock _gate = new();
    private bool _inProgress;

    public (DeployResponse Result, bool Ran) TryRun() {
        lock (_gate) {
            if (_inProgress) return (new DeployResponse(), false);
            _inProgress = true;
        }
        try { return (run(), true); } finally {
            lock (_gate) { _inProgress = false; }
        }
    }
}
