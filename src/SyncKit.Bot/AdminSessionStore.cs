using Npgsql;
using SyncKit.Auth;

namespace SyncKit.Bot;

// ISessionStore over admin_sessions, backing RequireAuth for /admin/* routes.
public sealed class AdminSessionStore(NpgsqlDataSource dataSource) : ISessionStore {
    public async Task CreateAsync(string token, string discordId, long expiresAt, CancellationToken ct) {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO admin_sessions (token, discord_id, expires_at) VALUES ($1, $2, $3)", conn);
        cmd.Parameters.AddWithValue(token);
        cmd.Parameters.AddWithValue(discordId);
        cmd.Parameters.AddWithValue(expiresAt);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<(bool Found, string DiscordId, long ExpiresAt)> LookupAsync(string token, CancellationToken ct) {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT discord_id, expires_at FROM admin_sessions WHERE token = $1", conn);
        cmd.Parameters.AddWithValue(token);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return (false, "", 0);
        return (true, reader.GetString(0), reader.GetInt64(1));
    }

    public async Task TouchAsync(string token, long newExpiresAt, CancellationToken ct) {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "UPDATE admin_sessions SET expires_at = $2 WHERE token = $1", conn);
        cmd.Parameters.AddWithValue(token);
        cmd.Parameters.AddWithValue(newExpiresAt);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(string token, CancellationToken ct) {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand("DELETE FROM admin_sessions WHERE token = $1", conn);
        cmd.Parameters.AddWithValue(token);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
