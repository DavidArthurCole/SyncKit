using Discord;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using SyncKit.Auth;
using SyncKit.Config;
using SyncKit.Contract;
using SyncKit.Db;

namespace SyncKit.Bot;

// Barebones-bot-flavored-by-config entry point
public sealed class SyncKitBotBuilder {
    private string _configFilePath = "/etc/synckit/config.env";
    private Func<string, string?> _envFallback = Environment.GetEnvironmentVariable;
    private string _name = "";
    private VerifyInfo _build = new();
    private bool _globalCommands;
    private bool _guildCommandMirror;
    private readonly List<BotCommand> _commands = [];
    private EmbedOptions? _verifyOptions, _successOptions, _failureOptions, _alreadyUpToDateOptions;
    private Func<BotConfig, Embed>? _verifyBuilder;
    private Func<BotConfig, string, Embed>? _alreadyUpToDateBuilder;
    private Func<BotConfig, string, string, Embed>? _successBuilder;
    private Func<string, Embed>? _failureBuilder;
    private string? _dbConnStr;
    private string? _dbMigrationsDir;
    private Func<NewVersionEvent, Task>? _newVersionHandler;
    private string _eventSecret = "";
    private string? _adminClientId;
    private string? _adminClientSecret;
    private string? _adminCallbackUrl;

    public SyncKitBotBuilder WithConfigFile(string path) { _configFilePath = path; return this; }
    // Test-only escape hatch so unit tests don't depend on real process env vars.
    public SyncKitBotBuilder WithEnvFallback(Func<string, string?> envFallback) { _envFallback = envFallback; return this; }
    public SyncKitBotBuilder WithName(string name) { _name = name; return this; }
    public SyncKitBotBuilder WithBuild(VerifyInfo build) { _build = build; return this; }
    public SyncKitBotBuilder WithGlobalCommands(bool enabled = true) { _globalCommands = enabled; return this; }
    public SyncKitBotBuilder WithGuildCommandMirror(bool enabled = true) { _guildCommandMirror = enabled; return this; }
    public SyncKitBotBuilder WithCommand(BotCommand command) { _commands.Add(command); return this; }
    public SyncKitBotBuilder WithVerifyEmbed(EmbedOptions options) { _verifyOptions = options; return this; }
    public SyncKitBotBuilder WithSuccessEmbed(EmbedOptions options) { _successOptions = options; return this; }
    public SyncKitBotBuilder WithFailureEmbed(EmbedOptions options) { _failureOptions = options; return this; }
    public SyncKitBotBuilder WithAlreadyUpToDateEmbed(EmbedOptions options) { _alreadyUpToDateOptions = options; return this; }
    public SyncKitBotBuilder WithVerifyEmbedBuilder(Func<BotConfig, Embed> build) { _verifyBuilder = build; return this; }
    public SyncKitBotBuilder WithAlreadyUpToDateEmbedBuilder(Func<BotConfig, string, Embed> build) { _alreadyUpToDateBuilder = build; return this; }
    public SyncKitBotBuilder WithSuccessEmbedBuilder(Func<BotConfig, string, string, Embed> build) { _successBuilder = build; return this; }
    public SyncKitBotBuilder WithFailureEmbedBuilder(Func<string, Embed> build) { _failureBuilder = build; return this; }
    public SyncKitBotBuilder WithDb(string connStr, string migrationsDir) { _dbConnStr = connStr; _dbMigrationsDir = migrationsDir; return this; }
    public SyncKitBotBuilder WithNewVersionHandler(Func<NewVersionEvent, Task> handler, string eventSecret) { _newVersionHandler = handler; _eventSecret = eventSecret; return this; }

    public SyncKitBotBuilder WithAdminUi(string clientId, string clientSecret, string callbackUrl) {
        _adminClientId = clientId;
        _adminClientSecret = clientSecret;
        _adminCallbackUrl = callbackUrl;
        return this;
    }

    public BotConfig BuildConfig() {
        var values = BotConfigLoader.Load(_configFilePath, _envFallback);
        return new BotConfig {
            Name = _name,
            Token = values.Token ?? "",
            AppId = values.AppId ?? "",
            GuildId = values.GuildId ?? "",
            RepoUrl = values.RepoUrl ?? "",
            SharedRoleId = values.SharedRoleId ?? "",
            SupporterRoleId = values.SupporterRoleId ?? "",
            DeployAgentUrl = values.DeployAgentUrl ?? "",
            DeployAgentSecret = values.DeployAgentSecret ?? "",
            PostgresConnectionString = values.PostgresConnectionString ?? "",
            DashboardChannelId = values.DashboardChannelId ?? "",
            EnabledThreads = values.EnabledThreads ?? "",
            Build = _build,
            GlobalCommands = _globalCommands,
            GuildCommandMirror = _guildCommandMirror,
            Extra = _commands,
            VerifyEmbedOptions = _verifyOptions,
            SuccessEmbedOptions = _successOptions,
            FailureEmbedOptions = _failureOptions,
            AlreadyUpToDateEmbedOptions = _alreadyUpToDateOptions,
            VerifyEmbedBuilder = _verifyBuilder,
            AlreadyUpToDateEmbedBuilder = _alreadyUpToDateBuilder,
            SuccessEmbedBuilder = _successBuilder,
            FailureEmbedBuilder = _failureBuilder,
        };
    }

    // Delegate always wins over options when both are set for the same state.
    public Embed ResolveVerifyEmbed(BotConfig cfg) =>
        cfg.VerifyEmbedBuilder is not null ? cfg.VerifyEmbedBuilder(cfg)
        : cfg.VerifyEmbedOptions is not null ? cfg.VerifyEmbedOptions.Apply(DefaultEmbeds.Verify(cfg))
        : DefaultEmbeds.Verify(cfg);

    public Embed ResolveAlreadyUpToDateEmbed(BotConfig cfg, string hash) =>
        cfg.AlreadyUpToDateEmbedBuilder is not null ? cfg.AlreadyUpToDateEmbedBuilder(cfg, hash)
        : cfg.AlreadyUpToDateEmbedOptions is not null ? cfg.AlreadyUpToDateEmbedOptions.Apply(DefaultEmbeds.AlreadyUpToDate(cfg, hash))
        : DefaultEmbeds.AlreadyUpToDate(cfg, hash);

    public Embed ResolveSuccessEmbed(BotConfig cfg, string fromHash, string toHash) =>
        cfg.SuccessEmbedBuilder is not null ? cfg.SuccessEmbedBuilder(cfg, fromHash, toHash)
        : cfg.SuccessEmbedOptions is not null ? cfg.SuccessEmbedOptions.Apply(DefaultEmbeds.Success(cfg, fromHash, toHash))
        : DefaultEmbeds.Success(cfg, fromHash, toHash);

    public Embed ResolveFailureEmbed(BotConfig cfg, string tail) =>
        cfg.FailureEmbedBuilder is not null ? cfg.FailureEmbedBuilder(tail)
        : cfg.FailureEmbedOptions is not null ? cfg.FailureEmbedOptions.Apply(DefaultEmbeds.Failure(tail))
        : DefaultEmbeds.Failure(tail);

    public async Task RunAsync(Action<WebApplication>? configureRoutes = null) {
        var cfg = BuildConfig();
        var webBuilder = WebApplication.CreateBuilder();
        var app = webBuilder.Build();

        Npgsql.NpgsqlConnection? conn = null;
        if (!string.IsNullOrEmpty(_dbConnStr)) {
            conn = await Database.InitAsync(_dbConnStr);
            if (!string.IsNullOrEmpty(_dbMigrationsDir))
                await Migrator.MigrateAsync(conn, _dbMigrationsDir);
        }

        SyncKitBot? bot = null;
        try { bot = await SyncKitBot.StartAsync(cfg, this); } catch (Exception ex) { app.Logger.LogWarning(ex, "synckit: bot start failed, continuing"); }

        if (_newVersionHandler is not null)
            app.MapPost("/events/new-version", NewVersionHandler.Build(_eventSecret, _newVersionHandler));

        var values = BotConfigLoader.Load(_configFilePath, _envFallback);
        var adminClientId = _adminClientId ?? values.DiscordAdminClientId;
        var adminClientSecret = _adminClientSecret ?? values.DiscordAdminClientSecret;
        var adminCallbackUrl = _adminCallbackUrl ?? values.AdminCallbackUrl;
        if (!string.IsNullOrEmpty(adminClientId) && !string.IsNullOrEmpty(adminClientSecret) &&
            !string.IsNullOrEmpty(adminCallbackUrl) && !string.IsNullOrEmpty(cfg.PostgresConnectionString) &&
            bot is not null) {
            DiscordOAuth.Init(adminClientId, adminClientSecret, adminCallbackUrl);
            var adminDataSource = Npgsql.NpgsqlDataSource.Create(cfg.PostgresConnectionString);
            await using (var adminConn = await adminDataSource.OpenConnectionAsync())
                await Migrator.MigrateAsync(adminConn, Path.Combine(AppContext.BaseDirectory, "Migrations"));
            var configStore = new ChannelConfigStore(adminDataSource);
            var sessionStore = new AdminSessionStore(adminDataSource);
            AdminRoutes.Map(app, cfg, configStore, sessionStore, bot.Client);
        }

        configureRoutes?.Invoke(app);

        var addr = Environment.GetEnvironmentVariable("LISTEN_ADDR");
        if (string.IsNullOrEmpty(addr)) addr = ":8080";
        var urls = addr.StartsWith(':') ? $"http://0.0.0.0{addr}" : $"http://{addr}";

        app.Logger.LogInformation("synckit: {Name} listening on {Addr}", cfg.Name, addr);
        try { await app.RunAsync(urls); } finally {
            if (bot is not null) await bot.DisposeAsync();
            if (conn is not null) await conn.DisposeAsync();
        }
    }
}
