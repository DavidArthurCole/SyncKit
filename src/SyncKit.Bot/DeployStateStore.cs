using Npgsql;

namespace SyncKit.Bot;

public sealed record DeployState(string AppName, string GitSha, string Semver, DateTimeOffset NotifiedAt);

public sealed class DeployStateStore(NpgsqlDataSource dataSource) {
    public async Task<DeployState?> GetAsync(string appName, CancellationToken ct) {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT app_name, git_sha, semver, notified_at
            FROM deploy_state WHERE app_name = $1
            """, conn);
        cmd.Parameters.AddWithValue(appName);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new DeployState(
            reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetFieldValue<DateTimeOffset>(3));
    }

    public async Task UpsertAsync(string appName, string gitSha, string semver, CancellationToken ct) {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO deploy_state (app_name, git_sha, semver, notified_at)
            VALUES ($1, $2, $3, now())
            ON CONFLICT (app_name) DO UPDATE SET
                git_sha = $2,
                semver = $3,
                notified_at = now()
            """, conn);
        cmd.Parameters.AddWithValue(appName);
        cmd.Parameters.AddWithValue(gitSha);
        cmd.Parameters.AddWithValue(semver);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
