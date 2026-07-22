using Npgsql;
using SyncKit.Contract;

namespace SyncKit.Identity;

public sealed record ResolveResult(Guid UserId, string Role, string? DiscordId, bool IsNew);
public sealed record LinkOutcome(bool Linked, bool Conflict, string? ConflictUsername, DateTimeOffset? ConflictCreatedAt);

public sealed class IdentityResolver(NpgsqlDataSource dataSource, AdminAllowlist allowlist) {
    public async Task<ResolveResult> ResolveAsync(
        string provider, string subject, string? discordId, string? username, string? avatar, CancellationToken ct) {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var existing = await LookupAsync(conn, provider, subject, ct);
        if (existing is { } foundUserId) {
            var existingRole = await TouchAndPromoteAsync(conn, foundUserId, discordId, username, avatar, ct);
            await UpdateIdentitySnapshotAsync(conn, provider, subject, username, avatar, ct);
            await tx.CommitAsync(ct);
            return new ResolveResult(foundUserId, existingRole, discordId, IsNew: false);
        }

        Guid userId;
        bool isNew;
        if (provider != "discord" && !string.IsNullOrEmpty(discordId) &&
            await LookupAsync(conn, "discord", discordId, ct) is { } linkedUserId) {
            userId = linkedUserId;
            isNew = false;
        } else if (provider == "discord" &&
              await LookupByDiscordIdAsync(conn, subject, ct) is { } discordUserId) {
            userId = discordUserId;
            isNew = false;
        } else {
            userId = Guid.NewGuid();
            isNew = true;
        }

        var effectiveDiscordId = provider == "discord" ? subject : discordId;
        var role = await UpsertUserAsync(conn, userId, effectiveDiscordId, username, avatar, isNew, ct);
        var winnerId = await InsertIdentityAsync(conn, userId, provider, subject, username, avatar, ct);

        await tx.CommitAsync(ct);
        return new ResolveResult(winnerId, role, effectiveDiscordId, isNew && winnerId == userId);
    }

    public async Task<Guid> MergeAsync(Guid keepUserId, Guid mergeUserId, CancellationToken ct) {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await using (var cmd = new NpgsqlCommand(
            "UPDATE identities SET user_id = $1 WHERE user_id = $2", conn)) {
            cmd.Parameters.AddWithValue(keepUserId);
            cmd.Parameters.AddWithValue(mergeUserId);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await using (var cmd = new NpgsqlCommand("DELETE FROM users WHERE user_id = $1", conn)) {
            cmd.Parameters.AddWithValue(mergeUserId);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return keepUserId;
    }

    public async Task<LinkOutcome> TryLinkAsync(
        Guid userId, string provider, string subject, string? discordId, string? username, string? avatar, CancellationToken ct) {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var existingOwner = await LookupAsync(conn, provider, subject, ct);
        if (existingOwner is { } ownerId && ownerId != userId) {
            await using var cmd = new NpgsqlCommand("SELECT username, created_at FROM users WHERE user_id = $1", conn);
            cmd.Parameters.AddWithValue(ownerId);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            string? ownerUsername = null;
            DateTimeOffset? ownerCreatedAt = null;
            if (await reader.ReadAsync(ct)) {
                ownerUsername = reader.GetString(0);
                ownerCreatedAt = reader.GetFieldValue<DateTimeOffset>(1);
            }
            await tx.CommitAsync(ct);
            return new LinkOutcome(Linked: false, Conflict: true, ownerUsername, ownerCreatedAt);
        }

        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO identities (user_id, provider, subject, username, avatar)
            VALUES ($1, $2, $3, $4, $5)
            ON CONFLICT (provider, subject) DO UPDATE SET username = EXCLUDED.username, avatar = EXCLUDED.avatar
            """, conn)) {
            cmd.Parameters.AddWithValue(userId);
            cmd.Parameters.AddWithValue(provider);
            cmd.Parameters.AddWithValue(subject);
            cmd.Parameters.AddWithValue((object?)username ?? DBNull.Value);
            cmd.Parameters.AddWithValue((object?)avatar ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        if (!string.IsNullOrEmpty(discordId)) {
            await using var cmd = new NpgsqlCommand(
                "UPDATE users SET discord_id = $2 WHERE user_id = $1 AND discord_id IS NULL", conn);
            cmd.Parameters.AddWithValue(userId);
            cmd.Parameters.AddWithValue(discordId);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return new LinkOutcome(Linked: true, Conflict: false, null, null);
    }

    private static async Task<Guid?> LookupAsync(NpgsqlConnection conn, string provider, string subject, CancellationToken ct) {
        await using var cmd = new NpgsqlCommand(
            "SELECT user_id FROM identities WHERE provider = $1 AND subject = $2", conn);
        cmd.Parameters.AddWithValue(provider);
        cmd.Parameters.AddWithValue(subject);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is Guid g ? g : null;
    }

    private static async Task<Guid?> LookupByDiscordIdAsync(NpgsqlConnection conn, string discordId, CancellationToken ct) {
        await using var cmd = new NpgsqlCommand("SELECT user_id FROM users WHERE discord_id = $1", conn);
        cmd.Parameters.AddWithValue(discordId);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is Guid g ? g : null;
    }

    private async Task<string> UpsertUserAsync(
        NpgsqlConnection conn, Guid userId, string? discordId, string? username, string? avatar, bool isNew, CancellationToken ct) {
        var role = isNew
            ? ResolveNewUserRole(discordId, allowlist)
            : await ExistingRoleOrDefaultAsync(conn, userId, discordId, allowlist, ct);

        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, discord_id, username, avatar, role, last_login_at)
            VALUES ($1, $2, $3, $4, $5, now())
            ON CONFLICT (user_id) DO UPDATE SET
                username = COALESCE(NULLIF(EXCLUDED.username, ''), users.username),
                avatar = CASE WHEN users.avatar_is_custom THEN users.avatar ELSE COALESCE(EXCLUDED.avatar, users.avatar) END,
                role = EXCLUDED.role,
                last_login_at = now()
            """, conn);
        cmd.Parameters.AddWithValue(userId);
        cmd.Parameters.AddWithValue((object?)discordId ?? DBNull.Value);
        cmd.Parameters.AddWithValue(username ?? "");
        cmd.Parameters.AddWithValue((object?)avatar ?? DBNull.Value);
        cmd.Parameters.AddWithValue(role);
        await cmd.ExecuteNonQueryAsync(ct);
        return role;
    }

    private async Task<string> TouchAndPromoteAsync(
        NpgsqlConnection conn, Guid userId, string? discordId, string? username, string? avatar, CancellationToken ct) {
        var role = await ExistingRoleOrDefaultAsync(conn, userId, discordId, allowlist, ct);
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE users SET
                username = COALESCE(NULLIF($2, ''), username),
                avatar = CASE WHEN avatar_is_custom THEN avatar ELSE COALESCE($3, avatar) END,
                role = $4,
                last_login_at = now()
            WHERE user_id = $1
            """, conn);
        cmd.Parameters.AddWithValue(userId);
        cmd.Parameters.AddWithValue(username ?? "");
        cmd.Parameters.AddWithValue((object?)avatar ?? DBNull.Value);
        cmd.Parameters.AddWithValue(role);
        await cmd.ExecuteNonQueryAsync(ct);
        return role;
    }

    private static async Task<string> ExistingRoleOrDefaultAsync(
        NpgsqlConnection conn, Guid userId, string? discordId, AdminAllowlist allowlist, CancellationToken ct) {
        if (!string.IsNullOrEmpty(discordId) && allowlist.Ids.Contains(discordId))
            return UserRoles.ToName(UserRole.Admin);

        await using var cmd = new NpgsqlCommand("SELECT role FROM users WHERE user_id = $1", conn);
        cmd.Parameters.AddWithValue(userId);
        var existing = await cmd.ExecuteScalarAsync(ct) as string;
        return existing ?? UserRoles.ToName(UserRole.Viewer);
    }

    private static string ResolveNewUserRole(string? discordId, AdminAllowlist allowlist) =>
        !string.IsNullOrEmpty(discordId) && allowlist.Ids.Contains(discordId)
            ? UserRoles.ToName(UserRole.Admin)
            : UserRoles.ToName(UserRole.Viewer);

    private static async Task<Guid> InsertIdentityAsync(
        NpgsqlConnection conn, Guid userId, string provider, string subject, string? username, string? avatar, CancellationToken ct) {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO identities (user_id, provider, subject, username, avatar)
            VALUES ($1, $2, $3, $4, $5)
            ON CONFLICT (provider, subject) DO NOTHING
            """, conn);
        cmd.Parameters.AddWithValue(userId);
        cmd.Parameters.AddWithValue(provider);
        cmd.Parameters.AddWithValue(subject);
        cmd.Parameters.AddWithValue((object?)username ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)avatar ?? DBNull.Value);
        var affected = await cmd.ExecuteNonQueryAsync(ct);
        if (affected > 0) return userId;

        var winner = await LookupAsync(conn, provider, subject, ct);
        return winner ?? userId;
    }

    private static async Task UpdateIdentitySnapshotAsync(
        NpgsqlConnection conn, string provider, string subject, string? username, string? avatar, CancellationToken ct) {
        await using var cmd = new NpgsqlCommand(
            "UPDATE identities SET username = COALESCE($3, username), avatar = COALESCE($4, avatar) WHERE provider = $1 AND subject = $2",
            conn);
        cmd.Parameters.AddWithValue(provider);
        cmd.Parameters.AddWithValue(subject);
        cmd.Parameters.AddWithValue((object?)username ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)avatar ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
