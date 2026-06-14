using Discord;
using Discord.WebSocket;

namespace Synckit.Bot;

// Ports Go bot.Start + registerCommands + handleInteraction + ensureSharedRole.
// Built-ins: /verify (public), /updateserver (Administrator-gated). Extra commands whose
// names collide with built-ins are dropped. Guild-scoped registration.
public sealed class SynckitBot : IAsyncDisposable
{
    public static readonly string[] BuiltinCommandNames = { "verify", "updateserver" };

    private readonly BotConfig _cfg;
    private readonly DiscordSocketClient _client;
    private readonly Dictionary<string, Func<SocketSlashCommandContext, Task>> _extra;

    private SynckitBot(BotConfig cfg, DiscordSocketClient client)
    {
        _cfg = cfg;
        _client = client;
        _extra = FilterExtras(cfg.Extra).ToDictionary(c => c.Name, c => c.Handler);
    }

    // Returns null (a no-op bot) when Token is empty, mirroring Go's (noop, nil).
    public static async Task<SynckitBot?> StartAsync(BotConfig cfg)
    {
        if (string.IsNullOrEmpty(cfg.Token))
            return null;

        var client = new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds,
        });
        var bot = new SynckitBot(cfg, client);

        client.SlashCommandExecuted += bot.OnSlashCommandAsync;
        client.Ready += bot.OnReadyAsync;

        await client.LoginAsync(TokenType.Bot, cfg.Token);
        await client.StartAsync();
        await client.SetGameAsync(cfg.Name);
        return bot;
    }

    public static IEnumerable<BotCommand> FilterExtras(IReadOnlyList<BotCommand> extras)
    {
        var builtin = new HashSet<string>(BuiltinCommandNames);
        return extras.Where(e => !builtin.Contains(e.Name));
    }

    public static bool NeedsRole(IReadOnlyCollection<string> memberRoles, string roleId) =>
        !memberRoles.Contains(roleId);

    private async Task OnReadyAsync()
    {
        await RegisterCommandsAsync();
        await EnsureSharedRoleAsync();
    }

    private async Task RegisterCommandsAsync()
    {
        if (string.IsNullOrEmpty(_cfg.AppId) || string.IsNullOrEmpty(_cfg.GuildId))
            return;
        var guild = _client.GetGuild(ulong.Parse(_cfg.GuildId));
        if (guild is null) return;

        var verify = new SlashCommandBuilder()
            .WithName("verify").WithDescription("Show the running server's build identity.").Build();
        var update = new SlashCommandBuilder()
            .WithName("updateserver").WithDescription("Pull latest and redeploy (admin only).")
            .WithDefaultMemberPermissions(GuildPermission.Administrator).Build();

        await guild.CreateApplicationCommandAsync(verify);
        await guild.CreateApplicationCommandAsync(update);
        foreach (var e in FilterExtras(_cfg.Extra))
            await guild.CreateApplicationCommandAsync(e.Definition);
    }

    private async Task EnsureSharedRoleAsync()
    {
        if (string.IsNullOrEmpty(_cfg.GuildId) || string.IsNullOrEmpty(_cfg.SharedRoleId))
            return;
        var guild = _client.GetGuild(ulong.Parse(_cfg.GuildId));
        var self = guild?.CurrentUser;
        if (self is null) return;
        var roleId = ulong.Parse(_cfg.SharedRoleId);
        if (self.Roles.Any(r => r.Id == roleId)) return;
        var role = guild!.GetRole(roleId);
        if (role is not null)
            await self.AddRoleAsync(role);
    }

    private async Task OnSlashCommandAsync(SocketSlashCommand cmd)
    {
        switch (cmd.Data.Name)
        {
            case "verify":
                await cmd.RespondAsync(embed: Embeds.Verify(_cfg), ephemeral: true);
                break;
            case "updateserver":
                await HandleUpdateServerAsync(cmd);
                break;
            default:
                if (_extra.TryGetValue(cmd.Data.Name, out var h))
                    await h(new SocketSlashCommandContext(_client, cmd));
                break;
        }
    }

    // Ports Go handleUpdateServer: admin gate, configured gate, defer, call agent, edit/followup.
    private async Task HandleUpdateServerAsync(SocketSlashCommand cmd)
    {
        var isAdmin = cmd.User is SocketGuildUser gu && gu.GuildPermissions.Administrator;
        if (!isAdmin)
        {
            await cmd.RespondAsync("Not authorized.", ephemeral: true);
            return;
        }
        if (string.IsNullOrEmpty(_cfg.DeployAgentUrl) || string.IsNullOrEmpty(_cfg.DeployAgentSecret))
        {
            await cmd.RespondAsync("Deploy agent not configured.", ephemeral: true);
            return;
        }
        await cmd.DeferAsync();
        var res = await DeployAgentClient.CallAsync(_cfg.DeployAgentUrl, _cfg.DeployAgentSecret);
        Embed? embed = res.AlreadyUpToDate
            ? Embeds.AlreadyUpToDate(_cfg, res.FromHash ?? "")
            : res.Ok ? Embeds.Success(_cfg, res.FromHash ?? "", res.ToHash ?? "") : null;
        if (embed is not null)
        {
            await cmd.ModifyOriginalResponseAsync(m => m.Embed = embed);
            return;
        }
        await cmd.DeleteOriginalResponseAsync();
        await cmd.FollowupAsync(embed: Embeds.Failure(res.Tail ?? ""), ephemeral: true);
    }

    public async ValueTask DisposeAsync()
    {
        if (!string.IsNullOrEmpty(_cfg.AppId) && !string.IsNullOrEmpty(_cfg.GuildId))
        {
            var guild = _client.GetGuild(ulong.Parse(_cfg.GuildId));
            if (guild is not null)
                foreach (var c in await guild.GetApplicationCommandsAsync())
                    await c.DeleteAsync();
        }
        await _client.StopAsync();
        await _client.DisposeAsync();
    }
}
