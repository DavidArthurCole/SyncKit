using Npgsql;

namespace SyncKit.Bot;

public sealed record ChannelConfig(
    string GuildId,
    string AppName,
    string? DashboardChannelId,
    string? EnabledThreads,
    string? SuccessTemplate,
    string? FailureTemplate,
    string? AlreadyUpToDateTemplate);

// Raw Npgsql over bot_channel_config, matching ChannelStateStore's shape. Null columns mean
// "no override" - callers fall back to BotConfig/hardcoded defaults.
public sealed class ChannelConfigStore(NpgsqlDataSource dataSource) {
    public async Task<ChannelConfig?> GetAsync(string guildId, string appName, CancellationToken ct) {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT guild_id, app_name, dashboard_channel_id, enabled_threads,
                   success_template, failure_template, already_up_to_date_template
            FROM bot_channel_config WHERE guild_id = $1 AND app_name = $2
            """, conn);
        cmd.Parameters.AddWithValue(guildId);
        cmd.Parameters.AddWithValue(appName);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return Read(reader);
    }

    public async Task UpsertAsync(
        string guildId, string appName, string? dashboardChannelId, string? enabledThreads,
        string? successTemplate, string? failureTemplate, string? alreadyUpToDateTemplate,
        CancellationToken ct) {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO bot_channel_config
                (guild_id, app_name, dashboard_channel_id, enabled_threads,
                 success_template, failure_template, already_up_to_date_template, updated_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, now())
            ON CONFLICT (guild_id, app_name) DO UPDATE SET
                dashboard_channel_id = EXCLUDED.dashboard_channel_id,
                enabled_threads = EXCLUDED.enabled_threads,
                success_template = EXCLUDED.success_template,
                failure_template = EXCLUDED.failure_template,
                already_up_to_date_template = EXCLUDED.already_up_to_date_template,
                updated_at = now()
            """, conn);
        cmd.Parameters.AddWithValue(guildId);
        cmd.Parameters.AddWithValue(appName);
        cmd.Parameters.AddWithValue((object?)dashboardChannelId ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)enabledThreads ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)successTemplate ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)failureTemplate ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)alreadyUpToDateTemplate ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static ChannelConfig Read(NpgsqlDataReader reader) => new(
        reader.GetString(0), reader.GetString(1),
        reader.IsDBNull(2) ? null : reader.GetString(2),
        reader.IsDBNull(3) ? null : reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        reader.IsDBNull(6) ? null : reader.GetString(6));
}
