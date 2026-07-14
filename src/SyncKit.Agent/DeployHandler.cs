using SyncKit.Contract;

namespace SyncKit.Agent;

// Single-flight deploy runner shared by the HTTP endpoint and the watcher, so a manual and an auto
// deploy never overlap. TryRun returns (result, ran): ran=false means another deploy held the lock.
public sealed class DeployHandler(Func<DeployResponse> run) {
    private readonly Lock _gate = new();
    private bool _inProgress;

    public (DeployResponse Result, bool Ran) TryRun() {
        lock (_gate) {
            if (_inProgress) {
                Console.WriteLine("deploy: skipped, another deploy is already in progress");
                return (new DeployResponse(), false);
            }
            _inProgress = true;
        }
        try { return (run(), true); } finally {
            lock (_gate) { _inProgress = false; }
        }
    }
}
