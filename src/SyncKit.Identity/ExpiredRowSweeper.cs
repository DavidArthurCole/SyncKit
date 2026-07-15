using Npgsql;

namespace SyncKit.Identity;

// Periodically deletes expired rows from oauth_states and login_codes. Both tables are written
// by unauthenticated routes (/login/go, /login/callback) with no delete on the abandoned-flow
// path, so without this sweep they grow unbounded. Mirrors SyncKit.Agent.Watcher's
// PeriodicTimer + RunAsync(CancellationToken) shape.
public sealed class ExpiredRowSweeper(NpgsqlDataSource dataSource, TimeSpan interval) {
    public async Task RunAsync(CancellationToken ct) {
        using var timer = new PeriodicTimer(interval);
        try {
            while (await timer.WaitForNextTickAsync(ct))
                await SweepAsync(ct);
        } catch (OperationCanceledException) { /* shutdown */ }
    }

    public async Task SweepAsync(CancellationToken ct) {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using (var cmd = new NpgsqlCommand("DELETE FROM oauth_states WHERE expires_at < now()", conn))
            await cmd.ExecuteNonQueryAsync(ct);
        await using (var cmd = new NpgsqlCommand("DELETE FROM login_codes WHERE expires_at < now()", conn))
            await cmd.ExecuteNonQueryAsync(ct);
    }
}
