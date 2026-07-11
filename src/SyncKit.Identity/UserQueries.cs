using Npgsql;
using SyncKit.Identity.Models;

namespace SyncKit.Identity;

public sealed class UserQueries(NpgsqlDataSource dataSource) {
    public async Task<User?> GetAsync(Guid userId, CancellationToken ct) {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT user_id, discord_id, username, avatar, role, created_at, last_login_at FROM users WHERE user_id = $1",
            conn);
        cmd.Parameters.AddWithValue(userId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return Read(reader);
    }

    public async Task<IReadOnlyList<User>> ListAsync(CancellationToken ct) {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT user_id, discord_id, username, avatar, role, created_at, last_login_at FROM users ORDER BY created_at",
            conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<User>();
        while (await reader.ReadAsync(ct)) results.Add(Read(reader));
        return results;
    }

    public async Task<bool> SetRoleAsync(Guid userId, UserRole role, CancellationToken ct) {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand("UPDATE users SET role = $2 WHERE user_id = $1", conn);
        cmd.Parameters.AddWithValue(userId);
        cmd.Parameters.AddWithValue(UserRoles.ToName(role));
        var affected = await cmd.ExecuteNonQueryAsync(ct);
        return affected > 0;
    }

    private static User Read(NpgsqlDataReader reader) => new() {
        UserId = reader.GetGuid(0),
        DiscordId = reader.IsDBNull(1) ? null : reader.GetString(1),
        Username = reader.GetString(2),
        Avatar = reader.IsDBNull(3) ? null : reader.GetString(3),
        Role = reader.GetString(4),
        CreatedAt = reader.GetFieldValue<DateTimeOffset>(5),
        LastLoginAt = reader.GetFieldValue<DateTimeOffset>(6),
    };
}
