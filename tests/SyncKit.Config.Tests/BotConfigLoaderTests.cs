using SyncKit.Config;

namespace SyncKit.Config.Tests;

public class BotConfigLoaderTests {
    [Fact]
    public void Load_FileAbsent_FallsBackToEnvEntirely() {
        var cfg = BotConfigLoader.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".env"),
            key => key == "DISCORD_TOKEN" ? "env-token" : null);

        Assert.Equal("env-token", cfg.Token);
        Assert.Null(cfg.AppId);
    }

    [Fact]
    public void Load_FilePresent_UsesFileValues() {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".env");
        File.WriteAllText(path, "DISCORD_TOKEN=file-token\nDISCORD_APP_ID=file-app-id\n");
        try {
            var cfg = BotConfigLoader.Load(path, _ => "env-fallback-should-not-be-used");

            Assert.Equal("file-token", cfg.Token);
            Assert.Equal("file-app-id", cfg.AppId);
        } finally { File.Delete(path); }
    }

    [Fact]
    public void Load_PartialFile_FallsBackPerMissingKey() {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".env");
        File.WriteAllText(path, "DISCORD_TOKEN=file-token\n");
        try {
            var cfg = BotConfigLoader.Load(path, key => key == "DISCORD_APP_ID" ? "env-app-id" : null);

            Assert.Equal("file-token", cfg.Token);
            Assert.Equal("env-app-id", cfg.AppId);
        } finally { File.Delete(path); }
    }

    [Fact]
    public void Load_IgnoresBlankLinesAndComments() {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".env");
        File.WriteAllText(path, "# comment\n\nDISCORD_TOKEN=file-token\n   \n# DISCORD_APP_ID=ignored\n");
        try {
            var cfg = BotConfigLoader.Load(path, key => key == "DISCORD_APP_ID" ? "env-app-id" : null);

            Assert.Equal("file-token", cfg.Token);
            Assert.Equal("env-app-id", cfg.AppId);
        } finally { File.Delete(path); }
    }

    [Fact]
    public void Load_StripsSurroundingQuotes() {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".env");
        File.WriteAllText(path, "DISCORD_TOKEN=\"quoted-token\"\nREPO_URL='single-quoted'\n");
        try {
            var cfg = BotConfigLoader.Load(path, _ => null);

            Assert.Equal("quoted-token", cfg.Token);
            Assert.Equal("single-quoted", cfg.RepoUrl);
        } finally { File.Delete(path); }
    }

    [Fact]
    public void Load_AllFieldsMapToExpectedEnvVars() {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".env");
        File.WriteAllText(path, string.Join('\n', new[] {
            "DISCORD_TOKEN=t", "DISCORD_APP_ID=a", "DISCORD_GUILD_ID=g", "REPO_URL=r",
            "SHARED_ROLE_ID=sr", "SUPPORTER_ROLE_ID=pr", "DEPLOY_AGENT_URL=du",
            "DEPLOY_AGENT_SECRET=ds", "POSTGRES_CONNECTION_STRING=pg", "DASHBOARD_CHANNEL_ID=dc",
        }));
        try {
            var cfg = BotConfigLoader.Load(path, _ => null);

            Assert.Equal("t", cfg.Token);
            Assert.Equal("a", cfg.AppId);
            Assert.Equal("g", cfg.GuildId);
            Assert.Equal("r", cfg.RepoUrl);
            Assert.Equal("sr", cfg.SharedRoleId);
            Assert.Equal("pr", cfg.SupporterRoleId);
            Assert.Equal("du", cfg.DeployAgentUrl);
            Assert.Equal("ds", cfg.DeployAgentSecret);
            Assert.Equal("pg", cfg.PostgresConnectionString);
            Assert.Equal("dc", cfg.DashboardChannelId);
        } finally { File.Delete(path); }
    }

    [Fact]
    public void Load_AdminOAuthKeys_ReadFromEnvFallback() {
        var values = BotConfigLoader.Load(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".env"),
            key => key switch {
                "DISCORD_ADMIN_CLIENT_ID" => "client-id-1",
                "DISCORD_ADMIN_CLIENT_SECRET" => "client-secret-1",
                "ADMIN_CALLBACK_URL" => "https://example.test/admin/callback",
                _ => null,
            });

        Assert.Equal("client-id-1", values.DiscordAdminClientId);
        Assert.Equal("client-secret-1", values.DiscordAdminClientSecret);
        Assert.Equal("https://example.test/admin/callback", values.AdminCallbackUrl);
    }

    [Fact]
    public void Load_AdminOAuthKeys_NoEnvFallback_AreNull() {
        var values = BotConfigLoader.Load(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".env"),
            _ => null);

        Assert.Null(values.DiscordAdminClientId);
        Assert.Null(values.DiscordAdminClientSecret);
        Assert.Null(values.AdminCallbackUrl);
    }
}
