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
    string? UptodateEmbedJson,
    string? DashboardEmbedJson,
    string? SuccessMessageJson = null,
    string? FailureMessageJson = null,
    string? UptodateMessageJson = null);

public sealed class ChannelConfigStore(NpgsqlDataSource dataSource) {
    public async Task<ChannelConfig?> GetAsync(string guildId, string appName, CancellationToken ct) {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT guild_id, app_name, dashboard_channel_id, github_feed_thread_id,
                   deploy_notifications_thread_id, success_embed_json, failure_embed_json,
                   uptodate_embed_json, dashboard_embed_json,
                   success_message_json, failure_message_json, uptodate_message_json
            FROM bot_channel_config WHERE guild_id = $1 AND app_name = $2
            """, conn);
        cmd.Parameters.AddWithValue(guildId);
        cmd.Parameters.AddWithValue(appName);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return Read(reader);
    }

    public async Task UpsertAsync(ChannelConfig config, CancellationToken ct) {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO bot_channel_config
                (guild_id, app_name, dashboard_channel_id, github_feed_thread_id,
                 deploy_notifications_thread_id, success_embed_json, failure_embed_json,
                 uptodate_embed_json, dashboard_embed_json,
                 success_message_json, failure_message_json, uptodate_message_json, updated_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, now())
            ON CONFLICT (guild_id, app_name) DO UPDATE SET
                dashboard_channel_id = EXCLUDED.dashboard_channel_id,
                github_feed_thread_id = EXCLUDED.github_feed_thread_id,
                deploy_notifications_thread_id = EXCLUDED.deploy_notifications_thread_id,
                success_embed_json = EXCLUDED.success_embed_json,
                failure_embed_json = EXCLUDED.failure_embed_json,
                uptodate_embed_json = EXCLUDED.uptodate_embed_json,
                dashboard_embed_json = EXCLUDED.dashboard_embed_json,
                success_message_json = EXCLUDED.success_message_json,
                failure_message_json = EXCLUDED.failure_message_json,
                uptodate_message_json = EXCLUDED.uptodate_message_json,
                updated_at = now()
            """, conn);
        cmd.Parameters.AddWithValue(config.GuildId);
        cmd.Parameters.AddWithValue(config.AppName);
        cmd.Parameters.AddWithValue((object?)config.DashboardChannelId ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)config.GithubFeedThreadId ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)config.DeployNotificationsThreadId ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)config.SuccessEmbedJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)config.FailureEmbedJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)config.UptodateEmbedJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)config.DashboardEmbedJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)config.SuccessMessageJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)config.FailureMessageJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)config.UptodateMessageJson ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static ChannelConfig Read(NpgsqlDataReader reader) => new(
        reader.GetString(0), reader.GetString(1),
        reader.IsDBNull(2) ? null : reader.GetString(2),
        reader.IsDBNull(3) ? null : reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        reader.IsDBNull(7) ? null : reader.GetString(7),
        reader.IsDBNull(8) ? null : reader.GetString(8),
        reader.IsDBNull(9) ? null : reader.GetString(9),
        reader.IsDBNull(10) ? null : reader.GetString(10),
        reader.IsDBNull(11) ? null : reader.GetString(11));
}
