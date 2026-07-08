using Npgsql;
using SyncKit.Identity.Models;

namespace SyncKit.Identity.Tools;

// Idempotent: ON CONFLICT DO NOTHING on both tables, so re-running after a partial failure
// (or re-running the whole tool after new source rows appear) never duplicates or clobbers.
// One transaction for the whole write.
public static class CutoverWriter
{
    public static async Task WriteAsync(string targetConnString, MergeResult merge, CancellationToken ct)
    {
        await using var db = NpgsqlDataSource.Create(targetConnString);
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        foreach (var user in merge.Users)
            await InsertUserAsync(conn, user, ct);
        foreach (var identity in merge.Identities)
            await InsertIdentityAsync(conn, identity, ct);

        await tx.CommitAsync(ct);
    }

    private static async Task InsertUserAsync(NpgsqlConnection conn, User user, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, discord_id, username, avatar, role, created_at, last_login_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7)
            ON CONFLICT (user_id) DO NOTHING
            """, conn);
        cmd.Parameters.AddWithValue(user.UserId);
        cmd.Parameters.AddWithValue((object?)user.DiscordId ?? DBNull.Value);
        cmd.Parameters.AddWithValue(user.Username);
        cmd.Parameters.AddWithValue((object?)user.Avatar ?? DBNull.Value);
        cmd.Parameters.AddWithValue(user.Role);
        cmd.Parameters.AddWithValue(user.CreatedAt);
        cmd.Parameters.AddWithValue(user.LastLoginAt);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task InsertIdentityAsync(NpgsqlConnection conn, Models.Identity identity, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO identities (user_id, provider, subject, linked_at)
            VALUES ($1, $2, $3, $4)
            ON CONFLICT (provider, subject) DO NOTHING
            """, conn);
        cmd.Parameters.AddWithValue(identity.UserId);
        cmd.Parameters.AddWithValue(identity.Provider);
        cmd.Parameters.AddWithValue(identity.Subject);
        cmd.Parameters.AddWithValue(identity.LinkedAt);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
