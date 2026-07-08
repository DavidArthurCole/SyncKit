using Npgsql;

namespace SyncKit.Identity;

public sealed class RevocationStore(NpgsqlDataSource dataSource)
{
    public async Task RevokeAsync(string sid, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO revoked_sessions (sid) VALUES ($1) ON CONFLICT (sid) DO NOTHING", conn);
        cmd.Parameters.AddWithValue(sid);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> IsRevokedAsync(string sid, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand("SELECT 1 FROM revoked_sessions WHERE sid = $1", conn);
        cmd.Parameters.AddWithValue(sid);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is not null;
    }
}
