using SyncKit.Contract;

namespace SyncKit.Bot;

public sealed class DeployVersionTracker(DeployStateStore store, DeployNotifier notifier) {
    public async Task CheckAndNotifyAsync(string appName, string gitSha, string semver, CancellationToken ct) {
        if (string.IsNullOrEmpty(gitSha)) {
            Console.WriteLine("synckit: deploy-self-report: GIT_SHA not set, skipping deploy self-report");
            return;
        }

        var existing = await store.GetAsync(appName, ct);
        if (existing is null) {
            await store.UpsertAsync(appName, gitSha, semver, ct);
            return;
        }

        if (existing.GitSha == gitSha) return;

        await notifier.NotifyAsync(
            new DeployResponse { Ok = true, AlreadyUpToDate = false, FromHash = existing.GitSha, ToHash = gitSha }, ct);
        await store.UpsertAsync(appName, gitSha, semver, ct);
    }
}
