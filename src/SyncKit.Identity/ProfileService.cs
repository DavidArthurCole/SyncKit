using Npgsql;

namespace SyncKit.Identity;

public enum UnlinkResult { Unlinked, NotFound, LastIdentity }

public sealed class ProfileService(NpgsqlDataSource dataSource) {
    public async Task<IReadOnlyList<Models.Identity>> ListIdentitiesAsync(Guid userId, CancellationToken ct) {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT user_id, provider, subject, username, avatar, linked_at FROM identities WHERE user_id = $1 ORDER BY linked_at",
            conn);
        cmd.Parameters.AddWithValue(userId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<Models.Identity>();
        while (await reader.ReadAsync(ct)) {
            results.Add(new Models.Identity {
                UserId = reader.GetGuid(0),
                Provider = reader.GetString(1),
                Subject = reader.GetString(2),
                Username = reader.IsDBNull(3) ? null : reader.GetString(3),
                Avatar = reader.IsDBNull(4) ? null : reader.GetString(4),
                LinkedAt = reader.GetFieldValue<DateTimeOffset>(5),
            });
        }
        return results;
    }

    public async Task<UnlinkResult> UnlinkAsync(Guid userId, string provider, string subject, CancellationToken ct) {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        int count;
        await using (var countCmd = new NpgsqlCommand("SELECT COUNT(*) FROM identities WHERE user_id = $1", conn)) {
            countCmd.Parameters.AddWithValue(userId);
            count = (int)(long)(await countCmd.ExecuteScalarAsync(ct))!;
        }
        if (count <= 1) {
            await tx.CommitAsync(ct);
            return UnlinkResult.LastIdentity;
        }

        await using var deleteCmd = new NpgsqlCommand(
            "DELETE FROM identities WHERE user_id = $1 AND provider = $2 AND subject = $3", conn);
        deleteCmd.Parameters.AddWithValue(userId);
        deleteCmd.Parameters.AddWithValue(provider);
        deleteCmd.Parameters.AddWithValue(subject);
        var affected = await deleteCmd.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        return affected > 0 ? UnlinkResult.Unlinked : UnlinkResult.NotFound;
    }

    public async Task SetCustomAvatarAsync(Guid userId, string avatarUrl, CancellationToken ct) {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "UPDATE users SET avatar = $2, avatar_is_custom = true WHERE user_id = $1", conn);
        cmd.Parameters.AddWithValue(userId);
        cmd.Parameters.AddWithValue(avatarUrl);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> SelectIdentityAvatarAsync(Guid userId, string provider, string subject, CancellationToken ct) {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        string? avatar;
        await using (var lookupCmd = new NpgsqlCommand(
            "SELECT avatar FROM identities WHERE user_id = $1 AND provider = $2 AND subject = $3", conn)) {
            lookupCmd.Parameters.AddWithValue(userId);
            lookupCmd.Parameters.AddWithValue(provider);
            lookupCmd.Parameters.AddWithValue(subject);
            var result = await lookupCmd.ExecuteScalarAsync(ct);
            if (result is null) {
                await tx.CommitAsync(ct);
                return false;
            }
            avatar = result as string;
        }

        await using (var updateCmd = new NpgsqlCommand(
            "UPDATE users SET avatar = $2, avatar_is_custom = false WHERE user_id = $1", conn)) {
            updateCmd.Parameters.AddWithValue(userId);
            updateCmd.Parameters.AddWithValue((object?)avatar ?? DBNull.Value);
            await updateCmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return true;
    }
}
