using Npgsql;
using SyncKit.Identity.Models;

namespace SyncKit.Identity;

public sealed record ResolveResult(Guid UserId, string Role, string? DiscordId, bool IsNew);

// Resolves a user_id for any provider login: exact-match an existing (provider, subject)
// identity, else auto-link via a matching discord identity (when discordId is supplied and the
// provider itself isn't "discord"), else create a brand-new account. One DB transaction; the
// identities insert is `ON CONFLICT DO NOTHING` + re-select-on-conflict so two concurrent first
// logins for the same (provider, subject) always agree on one winning user_id.
public sealed class IdentityResolver(NpgsqlDataSource dataSource, AdminAllowlist allowlist)
{
    public async Task<ResolveResult> ResolveAsync(
        string provider, string subject, string? discordId, string? username, string? avatar, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var existing = await LookupAsync(conn, provider, subject, ct);
        if (existing is { } foundUserId)
        {
            var existingRole = await TouchAndPromoteAsync(conn, foundUserId, discordId, username, avatar, ct);
            await tx.CommitAsync(ct);
            return new ResolveResult(foundUserId, existingRole, discordId, IsNew: false);
        }

        Guid userId;
        bool isNew;
        if (provider != "discord" && !string.IsNullOrEmpty(discordId) &&
            await LookupAsync(conn, "discord", discordId, ct) is { } linkedUserId)
        {
            userId = linkedUserId;
            isNew = false;
        }
        else if (provider == "discord" &&
            await LookupByDiscordIdAsync(conn, subject, ct) is { } discordUserId)
        {
            // Discord-as-primary-provider re-login: the users row already exists keyed by
            // discord_id (upserted below), the identities row may just be missing.
            userId = discordUserId;
            isNew = false;
        }
        else
        {
            userId = Guid.NewGuid();
            isNew = true;
        }

        var effectiveDiscordId = provider == "discord" ? subject : discordId;
        var role = await UpsertUserAsync(conn, userId, effectiveDiscordId, username, avatar, isNew, ct);
        var winnerId = await InsertIdentityAsync(conn, userId, provider, subject, ct);

        await tx.CommitAsync(ct);
        return new ResolveResult(winnerId, role, effectiveDiscordId, isNew && winnerId == userId);
    }

    public async Task<Guid> MergeAsync(Guid keepUserId, Guid mergeUserId, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await using (var cmd = new NpgsqlCommand(
            "UPDATE identities SET user_id = $1 WHERE user_id = $2", conn))
        {
            cmd.Parameters.AddWithValue(keepUserId);
            cmd.Parameters.AddWithValue(mergeUserId);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await using (var cmd = new NpgsqlCommand("DELETE FROM users WHERE user_id = $1", conn))
        {
            cmd.Parameters.AddWithValue(mergeUserId);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return keepUserId;
    }

    private static async Task<Guid?> LookupAsync(NpgsqlConnection conn, string provider, string subject, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT user_id FROM identities WHERE provider = $1 AND subject = $2", conn);
        cmd.Parameters.AddWithValue(provider);
        cmd.Parameters.AddWithValue(subject);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is Guid g ? g : null;
    }

    private static async Task<Guid?> LookupByDiscordIdAsync(NpgsqlConnection conn, string discordId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT user_id FROM users WHERE discord_id = $1", conn);
        cmd.Parameters.AddWithValue(discordId);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is Guid g ? g : null;
    }

    private async Task<string> UpsertUserAsync(
        NpgsqlConnection conn, Guid userId, string? discordId, string? username, string? avatar, bool isNew, CancellationToken ct)
    {
        var role = isNew
            ? ResolveNewUserRole(discordId, allowlist)
            : await ExistingRoleOrDefaultAsync(conn, userId, discordId, allowlist, ct);

        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, discord_id, username, avatar, role, last_login_at)
            VALUES ($1, $2, $3, $4, $5, now())
            ON CONFLICT (user_id) DO UPDATE SET
                username = COALESCE(NULLIF(EXCLUDED.username, ''), users.username),
                avatar = COALESCE(EXCLUDED.avatar, users.avatar),
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
        NpgsqlConnection conn, Guid userId, string? discordId, string? username, string? avatar, CancellationToken ct)
    {
        var role = await ExistingRoleOrDefaultAsync(conn, userId, discordId, allowlist, ct);
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE users SET
                username = COALESCE(NULLIF($2, ''), username),
                avatar = COALESCE($3, avatar),
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
        NpgsqlConnection conn, Guid userId, string? discordId, AdminAllowlist allowlist, CancellationToken ct)
    {
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

    // Race-safe: if a concurrent resolve already won this exact (provider, subject) pair,
    // ON CONFLICT DO NOTHING makes this a no-op; re-select and return the winner's user_id
    // rather than the caller's freshly-built one.
    private static async Task<Guid> InsertIdentityAsync(
        NpgsqlConnection conn, Guid userId, string provider, string subject, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO identities (user_id, provider, subject) VALUES ($1, $2, $3) ON CONFLICT (provider, subject) DO NOTHING",
            conn);
        cmd.Parameters.AddWithValue(userId);
        cmd.Parameters.AddWithValue(provider);
        cmd.Parameters.AddWithValue(subject);
        var affected = await cmd.ExecuteNonQueryAsync(ct);
        if (affected > 0) return userId;

        var winner = await LookupAsync(conn, provider, subject, ct);
        return winner ?? userId;
    }
}
