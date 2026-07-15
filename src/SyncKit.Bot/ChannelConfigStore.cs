using Npgsql;

namespace SyncKit.Bot;

public sealed record ChannelConfig(
    string GuildId,
    string AppName,
    string? DashboardChannelId,
    string? GithubFeedThreadId,
    string? DeployNotificationsThreadId,
    string? SuccessEmbedJson,
    string? FailureEmbedJson,
    string? UptodateEmbedJson);

// Raw Npgsql over bot_channel_config, matching ChannelStateStore's shape. Null columns mean
// "no override" - callers fall back to BotConfig/hardcoded defaults.
public sealed class ChannelConfigStore(NpgsqlDataSource dataSource) {
    public async Task<ChannelConfig?> GetAsync(string guildId, string appName, CancellationToken ct) {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT guild_id, app_name, dashboard_channel_id, github_feed_thread_id,
                   deploy_notifications_thread_id, success_embed_json, failure_embed_json,
                   uptodate_embed_json
            FROM bot_channel_config WHERE guild_id = $1 AND app_name = $2
            """, conn);
        cmd.Parameters.AddWithValue(guildId);
        cmd.Parameters.AddWithValue(appName);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return Read(reader);
    }

    public async Task UpsertAsync(
        string guildId, string appName, string? dashboardChannelId, string? githubFeedThreadId,
        string? deployNotificationsThreadId, string? successEmbedJson, string? failureEmbedJson,
        string? uptodateEmbedJson, CancellationToken ct) {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO bot_channel_config
                (guild_id, app_name, dashboard_channel_id, github_feed_thread_id,
                 deploy_notifications_thread_id, success_embed_json, failure_embed_json,
                 uptodate_embed_json, updated_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, now())
            ON CONFLICT (guild_id, app_name) DO UPDATE SET
                dashboard_channel_id = EXCLUDED.dashboard_channel_id,
                github_feed_thread_id = EXCLUDED.github_feed_thread_id,
                deploy_notifications_thread_id = EXCLUDED.deploy_notifications_thread_id,
                success_embed_json = EXCLUDED.success_embed_json,
                failure_embed_json = EXCLUDED.failure_embed_json,
                uptodate_embed_json = EXCLUDED.uptodate_embed_json,
                updated_at = now()
            """, conn);
        cmd.Parameters.AddWithValue(guildId);
        cmd.Parameters.AddWithValue(appName);
        cmd.Parameters.AddWithValue((object?)dashboardChannelId ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)githubFeedThreadId ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)deployNotificationsThreadId ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)successEmbedJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)failureEmbedJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)uptodateEmbedJson ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static ChannelConfig Read(NpgsqlDataReader reader) => new(
        reader.GetString(0), reader.GetString(1),
        reader.IsDBNull(2) ? null : reader.GetString(2),
        reader.IsDBNull(3) ? null : reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        reader.IsDBNull(7) ? null : reader.GetString(7));
}
