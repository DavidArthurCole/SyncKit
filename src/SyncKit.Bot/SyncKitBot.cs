using Discord;
using Discord.WebSocket;
using Npgsql;
using SyncKit.Contract;
using SyncKit.Db;

namespace SyncKit.Bot;

// Ports Go bot.Start + registerCommands + handleInteraction + ensureSharedRole.
// Built-ins: /verify (public), /updateserver (Administrator-gated). Extra commands whose
// names collide with built-ins are dropped. Guild-scoped registration.
//
// Requests the GuildMembers gateway intent to populate the member cache. GuildMembers is
// privileged: the bot application must have "Server Members Intent" enabled in the Discord
// Developer Portal (Bot tab), or client.StartAsync fails to connect - the portal toggle is
// required in addition to the code-side intent flag below, and is outside this repo's control.
public sealed class SyncKitBot : IAsyncDisposable {
    public static readonly string[] BuiltinCommandNames = ["verify", "updateserver"];

    private readonly BotConfig _cfg;
    private readonly Dictionary<string, Func<SocketSlashCommandContext, Task>> _extra;
    private readonly Dictionary<string, Func<SocketAutocompleteContext, Task>> _autocomplete;
    private readonly SyncKitBotBuilder? _builder;
    private ChannelHub? _channelHub;
    private ChannelConfigStore? _configStore;
    private ChannelStateStore? _stateStore;

    public Discord.WebSocket.DiscordSocketClient Client { get; }

    // Null until the channel hub is enabled (dashboard channel set + Postgres) and the guild is ready.
    public BotConfigService? ConfigService { get; private set; }

    private SyncKitBot(BotConfig cfg, DiscordSocketClient client, SyncKitBotBuilder? builder) {
        _cfg = cfg;
        Client = client;
        _builder = builder;
        _extra = FilterExtras(cfg.Extra).ToDictionary(c => c.Name, c => c.Handler);
        _autocomplete = FilterExtras(cfg.Extra)
            .Where(c => c.AutocompleteHandler is not null)
            .ToDictionary(c => c.Name, c => c.AutocompleteHandler!);
    }

    // Null until ChannelHub is enabled (DashboardChannelId set) and the guild is ready.
    public async Task UpdateDashboardAsync(DashboardSnapshot snapshot, CancellationToken ct = default) {
        if (_channelHub is not null) await _channelHub.UpdateDashboardAsync(snapshot, ct);
    }

    public async Task<string?> EnsureWebhookForThreadAsync(ThreadKind kind, ulong threadId, CancellationToken ct = default) =>
        _channelHub is null ? null : await _channelHub.EnsureWebhookForThreadAsync(kind, threadId, ct);

    public async Task TeardownWebhookForThreadAsync(ThreadKind kind, CancellationToken ct = default) {
        if (_channelHub is not null) await _channelHub.TeardownWebhookForThreadAsync(kind, ct);
    }

    // Returns null (a no-op bot) when Token is empty, mirroring Go's (noop, nil).
    public static async Task<SyncKitBot?> StartAsync(BotConfig cfg, SyncKitBotBuilder? builder = null) {
        if (string.IsNullOrEmpty(cfg.Token))
            return null;

        var client = new DiscordSocketClient(new DiscordSocketConfig {
            GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMembers,
        });
        var bot = new SyncKitBot(cfg, client, builder);

        client.SlashCommandExecuted += bot.OnSlashCommandAsync;
        client.AutocompleteExecuted += bot.OnAutocompleteAsync;
        client.Ready += bot.OnReadyAsync;

        await client.LoginAsync(TokenType.Bot, cfg.Token);
        await client.StartAsync();
        await client.SetGameAsync(cfg.Name);
        return bot;
    }

    public static IEnumerable<BotCommand> FilterExtras(IReadOnlyList<BotCommand> extras) {
        var builtin = new HashSet<string>(BuiltinCommandNames);
        return extras.Where(e => !builtin.Contains(e.Name));
    }

    public static bool NeedsRole(IReadOnlyCollection<string> memberRoles, string roleId) =>
        !memberRoles.Contains(roleId);

    // Go used the raw string; a malformed snowflake should log-and-skip, not throw out of the
    // best-effort Ready handlers. Returns false (and logs) when the id is empty or non-numeric.
    private static bool TryParseSnowflake(string value, string label, out ulong id) {
        if (ulong.TryParse(value, out id)) return true;
        Console.Error.WriteLine($"bot: invalid {label} snowflake: \"{value}\"");
        return false;
    }

    private async Task OnReadyAsync() {
        try { await RegisterCommandsAsync(); } catch (Exception ex) { Console.Error.WriteLine($"bot: register commands: {ex.Message}"); }
        try { await DownloadGuildMembersAsync(); } catch (Exception ex) { Console.Error.WriteLine($"bot: download guild members: {ex.Message}"); }
        try { await EnsureSharedRoleAsync(); } catch (Exception ex) { Console.Error.WriteLine($"bot: shared-role: {ex.Message}"); }
        try { await InitChannelHubAsync(); } catch (Exception ex) { Console.Error.WriteLine($"bot: channel hub: {ex.Message}"); }
    }

    // Requires GuildMembers intent (privileged - must be enabled in the Developer Portal under
    // Bot -> Privileged Gateway Intents -> Server Members Intent) or the gateway rejects the
    // connection. Populates the local member cache that IsGuildAdmin's guild.GetUser reads.
    private async Task DownloadGuildMembersAsync() {
        if (string.IsNullOrEmpty(_cfg.GuildId)) return;
        if (!TryParseSnowflake(_cfg.GuildId, "guild id", out var guildId)) return;
        var guild = Client.GetGuild(guildId);
        if (guild is null) return;
        await guild.DownloadUsersAsync();
    }

    // Additive/config-gated: DashboardChannelId unset (env or bot_channel_config override) means
    // _channelHub stays null and every dependent call becomes a no-op, matching today's behavior.
    private async Task InitChannelHubAsync() {
        if (string.IsNullOrEmpty(_cfg.GuildId)) return;
        if (!TryParseSnowflake(_cfg.GuildId, "guild id", out var guildId)) return;
        if (string.IsNullOrEmpty(_cfg.PostgresConnectionString)) return;

        var dataSource = NpgsqlDataSource.Create(_cfg.PostgresConnectionString);
        await using (var conn = await dataSource.OpenConnectionAsync())
            await Migrator.MigrateAsync(conn, Path.Combine(AppContext.BaseDirectory, "Migrations"));

        var configStore = new ChannelConfigStore(dataSource);
        _configStore = configStore;
        var configOverride = await configStore.GetAsync(_cfg.GuildId, _cfg.Name, CancellationToken.None);
        var dashboardChannelIdStr = configOverride?.DashboardChannelId ?? _cfg.DashboardChannelId;

        if (string.IsNullOrEmpty(dashboardChannelIdStr)) return;
        if (!TryParseSnowflake(dashboardChannelIdStr, "dashboard channel id", out var channelId)) return;
        var guild = Client.GetGuild(guildId);
        if (guild is null) return;

        var store = new ChannelStateStore(dataSource);
        _stateStore = store;
        _channelHub = new ChannelHub(guild, channelId, _cfg.Name, store);

        if (!string.IsNullOrEmpty(configOverride?.GithubFeedThreadId) &&
            ulong.TryParse(configOverride.GithubFeedThreadId, out var githubFeedThreadId))
            await _channelHub.EnsureWebhookForThreadAsync(ThreadKind.GithubFeed, githubFeedThreadId, CancellationToken.None);

        ConfigService = new BotConfigService(_cfg.GuildId, _cfg.Name, _configStore, _stateStore,
            EnsureWebhookForThreadAsync, TeardownWebhookForThreadAsync);
    }

    private async Task RegisterCommandsAsync() {
        if (string.IsNullOrEmpty(_cfg.AppId)) return;

        var builtins = BuildBuiltinCommands();
        var extras = FilterExtras(_cfg.Extra).Select(c => c.Definition).ToList();
        var desired = builtins.Concat(extras).ToList();

        if (_cfg.GlobalCommands) {
            await RegisterGlobalAsync(desired);
            if (_cfg.GuildCommandMirror && !string.IsNullOrEmpty(_cfg.GuildId))
                await RegisterGuildAsync(desired);
            return;
        }

        if (string.IsNullOrEmpty(_cfg.GuildId)) return;
        await RegisterGuildAsync(desired);
    }

    private List<ApplicationCommandProperties> BuildBuiltinCommands() {
        var verifyBuilder = new SlashCommandBuilder()
            .WithName("verify").WithDescription("Show the running server's build identity.");
        var updateBuilder = new SlashCommandBuilder()
            .WithName("updateserver").WithDescription("Pull latest and redeploy (admin only).")
            .WithDefaultMemberPermissions(GuildPermission.Administrator)
            .WithIntegrationTypes(ApplicationIntegrationType.GuildInstall)
            .WithContextTypes(InteractionContextType.Guild);

        if (_cfg.GlobalCommands) {
            verifyBuilder
                .WithIntegrationTypes(ApplicationIntegrationType.GuildInstall, ApplicationIntegrationType.UserInstall)
                .WithContextTypes(InteractionContextType.Guild, InteractionContextType.BotDm, InteractionContextType.PrivateChannel);
        }

        return [verifyBuilder.Build(), updateBuilder.Build()];
    }

    private async Task RegisterGlobalAsync(List<ApplicationCommandProperties> desired) {
        var desiredSig = CommandSignature.Compute(desired.Select(CommandSignature.FromProperties));
        string? currentSig = null;
        try {
            var current = await Client.GetGlobalApplicationCommandsAsync();
            currentSig = CommandSignature.Compute(current.Select(CommandSignature.FromCommand));
        } catch (Exception ex) {
            Console.Error.WriteLine($"bot: fetch global commands for diff: {ex.Message}");
        }

        if (currentSig == desiredSig) {
            Console.WriteLine("bot: global commands unchanged, skipping overwrite");
            return;
        }
        await Client.BulkOverwriteGlobalApplicationCommandsAsync([.. desired]);
    }

    private async Task RegisterGuildAsync(List<ApplicationCommandProperties> desired) {
        if (!TryParseSnowflake(_cfg.GuildId, "guild id", out var guildId)) return;
        var guild = Client.GetGuild(guildId);
        if (guild is null) return;

        var desiredSig = CommandSignature.Compute(desired.Select(CommandSignature.FromProperties));
        IReadOnlyCollection<IApplicationCommand>? current = null;
        try {
            current = await guild.GetApplicationCommandsAsync();
            var currentSig = CommandSignature.Compute(current.Select(CommandSignature.FromCommand));
            if (currentSig == desiredSig) {
                Console.WriteLine("bot: guild commands unchanged, skipping overwrite");
                return;
            }
        } catch (Exception ex) {
            Console.Error.WriteLine($"bot: fetch guild commands for diff: {ex.Message}");
        }

        var desiredNames = desired.Select(d => d.Name.IsSpecified ? d.Name.Value : "").ToHashSet();
        if (current is not null) {
            foreach (var existing in current.Where(c => !desiredNames.Contains(c.Name)))
                await existing.DeleteAsync();
        }

        foreach (var d in desired)
            await guild.CreateApplicationCommandAsync(d);
    }

    private async Task EnsureSharedRoleAsync() {
        if (string.IsNullOrEmpty(_cfg.GuildId) || string.IsNullOrEmpty(_cfg.SharedRoleId))
            return;
        if (!TryParseSnowflake(_cfg.GuildId, "guild id", out var guildId)) return;
        if (!TryParseSnowflake(_cfg.SharedRoleId, "shared role id", out var roleId)) return;
        var guild = Client.GetGuild(guildId);
        var self = guild?.CurrentUser;
        if (self is null) return;
        if (self.Roles.Any(r => r.Id == roleId)) return;
        var role = guild!.GetRole(roleId);
        if (role is not null)
            await self.AddRoleAsync(role);
    }

    private async Task OnSlashCommandAsync(SocketSlashCommand cmd) {
        switch (cmd.Data.Name) {
            case "verify":
                await cmd.RespondAsync(embed: _builder?.ResolveVerifyEmbed(_cfg) ?? DefaultEmbeds.Verify(_cfg), ephemeral: true);
                break;
            case "updateserver":
                await HandleUpdateServerAsync(cmd);
                break;
            default:
                if (_extra.TryGetValue(cmd.Data.Name, out var h))
                    await h(new SocketSlashCommandContext(Client, cmd));
                break;
        }
    }

    private async Task OnAutocompleteAsync(SocketAutocompleteInteraction ac) {
        if (_autocomplete.TryGetValue(ac.Data.CommandName, out var h)) {
            try { await h(new SocketAutocompleteContext(Client, ac)); } catch (Exception ex) {
                Console.Error.WriteLine($"bot: autocomplete /{ac.Data.CommandName} failed: {ex.Message}");
                try { await ac.RespondAsync(Array.Empty<AutocompleteResult>()); } catch { /* interaction already acked/expired */ }
            }
        } else {
            try { await ac.RespondAsync(Array.Empty<AutocompleteResult>()); } catch { /* interaction already acked/expired */ }
        }
    }

    // Ports Go handleUpdateServer: admin gate, configured gate, defer, call agent, edit/followup.
    private async Task HandleUpdateServerAsync(SocketSlashCommand cmd) {
        var isAdmin = cmd.User is SocketGuildUser gu && gu.GuildPermissions.Administrator;
        if (!isAdmin) {
            await cmd.RespondAsync("Not authorized.", ephemeral: true);
            return;
        }
        if (string.IsNullOrEmpty(_cfg.DeployAgentUrl) || string.IsNullOrEmpty(_cfg.DeployAgentSecret)) {
            await cmd.RespondAsync("Deploy agent not configured.", ephemeral: true);
            return;
        }
        await cmd.DeferAsync();
        var res = await DeployAgentClient.CallAsync(_cfg.DeployAgentUrl, _cfg.DeployAgentSecret);
        Embed? embed = res.AlreadyUpToDate
            ? _builder?.ResolveAlreadyUpToDateEmbed(_cfg, res.FromHash ?? "") ?? DefaultEmbeds.AlreadyUpToDate(_cfg, res.FromHash ?? "")
            : res.Ok
                ? _builder?.ResolveSuccessEmbed(_cfg, res.FromHash ?? "", res.ToHash ?? "") ?? DefaultEmbeds.Success(_cfg, res.FromHash ?? "", res.ToHash ?? "")
                : null;
        if (embed is not null) {
            await cmd.ModifyOriginalResponseAsync(m => m.Embed = embed);
            return;
        }
        await cmd.DeleteOriginalResponseAsync();
        await cmd.FollowupAsync(embed: _builder?.ResolveFailureEmbed(_cfg, res.Tail ?? "") ?? DefaultEmbeds.Failure(res.Tail ?? ""), ephemeral: true);
    }

    public async ValueTask DisposeAsync() {
        if (!string.IsNullOrEmpty(_cfg.AppId) && TryParseSnowflake(_cfg.GuildId, "guild id", out var guildId)) {
            var guild = Client.GetGuild(guildId);
            if (guild is not null) {
                foreach (var c in await guild.GetApplicationCommandsAsync())
                    await c.DeleteAsync();
            }
        }
        await Client.StopAsync();
        await Client.DisposeAsync();
    }
}
