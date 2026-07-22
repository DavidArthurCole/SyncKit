using Npgsql;

namespace SyncKit.Bot;

public sealed record ChannelState(string GuildId, string AppName, string Kind, string DiscordId, string? WebhookToken);

public sealed class ChannelStateStore(NpgsqlDataSource dataSource) {
    public async Task<ChannelState?> GetAsync(string guildId, string appName, string kind, CancellationToken ct) {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT guild_id, app_name, kind, discord_id, webhook_token FROM bot_channel_state WHERE guild_id = $1 AND app_name = $2 AND kind = $3",
            conn);
        cmd.Parameters.AddWithValue(guildId);
        cmd.Parameters.AddWithValue(appName);
        cmd.Parameters.AddWithValue(kind);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return Read(reader);
    }

    public async Task<IReadOnlyList<ChannelState>> ListAsync(string guildId, string appName, CancellationToken ct) {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT guild_id, app_name, kind, discord_id, webhook_token FROM bot_channel_state WHERE guild_id = $1 AND app_name = $2",
            conn);
        cmd.Parameters.AddWithValue(guildId);
        cmd.Parameters.AddWithValue(appName);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<ChannelState>();
        while (await reader.ReadAsync(ct)) results.Add(Read(reader));
        return results;
    }

    public async Task UpsertAsync(string guildId, string appName, string kind, string discordId, string? webhookToken, CancellationToken ct) {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO bot_channel_state (guild_id, app_name, kind, discord_id, webhook_token)
            VALUES ($1, $2, $3, $4, $5)
            ON CONFLICT (guild_id, app_name, kind) DO UPDATE SET
                discord_id = EXCLUDED.discord_id,
                webhook_token = EXCLUDED.webhook_token
            """, conn);
        cmd.Parameters.AddWithValue(guildId);
        cmd.Parameters.AddWithValue(appName);
        cmd.Parameters.AddWithValue(kind);
        cmd.Parameters.AddWithValue(discordId);
        cmd.Parameters.AddWithValue((object?)webhookToken ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(string guildId, string appName, string kind, CancellationToken ct) {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM bot_channel_state WHERE guild_id = $1 AND app_name = $2 AND kind = $3", conn);
        cmd.Parameters.AddWithValue(guildId);
        cmd.Parameters.AddWithValue(appName);
        cmd.Parameters.AddWithValue(kind);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static ChannelState Read(NpgsqlDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), reader.GetString(2),
        reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4));
}
