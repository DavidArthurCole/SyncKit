using Npgsql;

namespace SyncKit.Identity.Tools;

public static class SourceReader {
    public static async Task<SourceSnapshot> ReadEggIncognitoAsync(string connString, CancellationToken ct) {
        await using var db = NpgsqlDataSource.Create(connString);
        await using var conn = await db.OpenConnectionAsync(ct);

        var users = new List<SourceUser>();
        await using (var cmd = new NpgsqlCommand(
            "SELECT user_id, discord_id, username, avatar, role, created_at, last_login_at FROM users", conn))
        await using (var reader = await cmd.ExecuteReaderAsync(ct)) {
            while (await reader.ReadAsync(ct)) {
                users.Add(new SourceUser(
                    reader.GetGuid(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetString(4),
                    reader.GetFieldValue<DateTimeOffset>(5),
                    reader.GetFieldValue<DateTimeOffset>(6)));
            }
        }

        var identities = await ReadIdentitiesAsync(conn, ct);
        return new SourceSnapshot(users, identities);
    }

    public static async Task<SourceSnapshot> ReadEggLedgerAsync(string connString, CancellationToken ct) {
        await using var db = NpgsqlDataSource.Create(connString);
        await using var conn = await db.OpenConnectionAsync(ct);

        var users = new List<SourceUser>();
        await using (var cmd = new NpgsqlCommand(
            "SELECT user_id, discord_id, username, avatar_url, created_at FROM users", conn))
        await using (var reader = await cmd.ExecuteReaderAsync(ct)) {
            while (await reader.ReadAsync(ct)) {
                var avatarUrl = reader.IsDBNull(3) ? "" : reader.GetString(3);
                users.Add(new SourceUser(
                    reader.GetGuid(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.GetString(2),
                    string.IsNullOrEmpty(avatarUrl) ? null : avatarUrl,
                    Role: null,
                    DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(4)),
                    LastLoginAt: null));
            }
        }

        var identities = await ReadIdentitiesAsync(conn, ct);
        return new SourceSnapshot(users, identities);
    }

    private static async Task<List<SourceIdentity>> ReadIdentitiesAsync(NpgsqlConnection conn, CancellationToken ct) {
        var identities = new List<SourceIdentity>();
        await using var cmd = new NpgsqlCommand("SELECT user_id, provider, subject, linked_at FROM identities", conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) {
            identities.Add(new SourceIdentity(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                reader.GetFieldValue<DateTimeOffset>(3)));
        }
        return identities;
    }
}
