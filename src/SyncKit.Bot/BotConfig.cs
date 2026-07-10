using Discord;
using SyncKit.Contract;

namespace SyncKit.Bot;

// Ports Go bot.Config. DeployAgentUrl/Secret enable /updateserver.
public sealed class BotConfig
{
    public string Name { get; init; } = "";
    public string Token { get; init; } = "";
    public string AppId { get; init; } = "";
    public string GuildId { get; init; } = "";
    public string RepoUrl { get; init; } = "";
    public VerifyInfo Build { get; init; } = new();
    public string DeployAgentUrl { get; init; } = "";
    public string DeployAgentSecret { get; init; } = "";
    public string SharedRoleId { get; init; } = "";
    public string SupporterRoleId { get; init; } = "";
    public IReadOnlyList<BotCommand> Extra { get; init; } = Array.Empty<BotCommand>();

    public EmbedOptions? VerifyEmbedOptions { get; init; }
    public EmbedOptions? SuccessEmbedOptions { get; init; }
    public EmbedOptions? FailureEmbedOptions { get; init; }
    public EmbedOptions? AlreadyUpToDateEmbedOptions { get; init; }
    public Func<BotConfig, Embed>? VerifyEmbedBuilder { get; init; }
    public Func<BotConfig, string, Embed>? AlreadyUpToDateEmbedBuilder { get; init; }
    public Func<BotConfig, string, string, Embed>? SuccessEmbedBuilder { get; init; }
    public Func<string, Embed>? FailureEmbedBuilder { get; init; }

    // false = today's per-command guild-scoped CreateApplicationCommandAsync behavior, unchanged.
    public bool GlobalCommands { get; init; } = false;
    // Dev flag: also register guild-scoped copies alongside global commands (faster local iteration).
    public bool GuildCommandMirror { get; init; } = false;

    // ChannelHub is enabled only when DashboardChannelId is set. EnabledThreads is a CSV of
    // ThreadKind names; unset/empty = all threads off (see ThreadKinds.ParseCsv).
    public string DashboardChannelId { get; init; } = "";
    public string EnabledThreads { get; init; } = "";
    public string PostgresConnectionString { get; init; } = "";

    public string CommitUrl(string version) => $"{RepoUrl}/commit/{version}";
}

// App-supplied slash command + handler. Set IntegrationTypes/ContextTypes on Definition
// yourself; SyncKitBot only controls this for its own builtins (verify, updateserver).
public sealed record BotCommand(
    ApplicationCommandProperties Definition,
    string Name,
    Func<SocketSlashCommandContext, Task> Handler,
    Func<SocketAutocompleteContext, Task>? AutocompleteHandler = null);

// Lightweight context wrapper so app handlers don't depend on the raw socket type shape.
public sealed record SocketSlashCommandContext(
    Discord.WebSocket.DiscordSocketClient Client,
    Discord.WebSocket.SocketSlashCommand Command);

public sealed record SocketAutocompleteContext(
    Discord.WebSocket.DiscordSocketClient Client,
    Discord.WebSocket.SocketAutocompleteInteraction Interaction);
