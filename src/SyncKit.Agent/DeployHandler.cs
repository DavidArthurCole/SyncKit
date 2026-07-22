using SyncKit.Contract;

namespace SyncKit.Agent;

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
