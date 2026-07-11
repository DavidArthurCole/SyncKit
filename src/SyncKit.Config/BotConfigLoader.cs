namespace SyncKit.Config;

// Flat scalar/secret tier for SyncKit.Bot, loaded from a dotenv-format file with per-key
// env-var fallback. Behavior toggles (GlobalCommands etc.) are NOT here - those are
// SyncKitBotBuilder calls, see docs/bot-config.md.
public sealed record BotConfigValues(
    string? Token,
    string? AppId,
    string? GuildId,
    string? RepoUrl,
    string? SharedRoleId,
    string? SupporterRoleId,
    string? DeployAgentUrl,
    string? DeployAgentSecret,
    string? PostgresConnectionString,
    string? DashboardChannelId,
    string? EnabledThreads,
    string? DiscordAdminClientId,
    string? DiscordAdminClientSecret,
    string? AdminCallbackUrl);

public static class BotConfigLoader {
    public static BotConfigValues Load(string path, Func<string, string?> envFallback) {
        var raw = File.Exists(path) ? ParseDotenv(File.ReadAllText(path)) : [];

        string? Get(string key) => raw.TryGetValue(key, out var v) ? v : envFallback(key);

        return new BotConfigValues(
            Get("DISCORD_TOKEN"),
            Get("DISCORD_APP_ID"),
            Get("DISCORD_GUILD_ID"),
            Get("REPO_URL"),
            Get("SHARED_ROLE_ID"),
            Get("SUPPORTER_ROLE_ID"),
            Get("DEPLOY_AGENT_URL"),
            Get("DEPLOY_AGENT_SECRET"),
            Get("POSTGRES_CONNECTION_STRING"),
            Get("DASHBOARD_CHANNEL_ID"),
            Get("ENABLED_THREADS"),
            Get("DISCORD_ADMIN_CLIENT_ID"),
            Get("DISCORD_ADMIN_CLIENT_SECRET"),
            Get("ADMIN_CALLBACK_URL"));
    }

    private static Dictionary<string, string> ParseDotenv(string text) {
        var result = new Dictionary<string, string>();
        foreach (var rawLine in text.Split('\n')) {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var eq = line.IndexOf('=');
            if (eq < 0) continue;
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
                value = value[1..^1];
            result[key] = value;
        }
        return result;
    }
}
