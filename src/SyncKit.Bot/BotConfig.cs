using Discord;
using SyncKit.Contract;

namespace SyncKit.Bot;

public sealed class BotConfig {
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

    public bool GlobalCommands { get; init; }
    public bool GuildCommandMirror { get; init; }

    public string DashboardChannelId { get; init; } = "";
    public string PostgresConnectionString { get; init; } = "";

    public Func<CancellationToken, Task<DashboardSnapshot>>? DashboardProvider { get; init; }
    public TimeSpan DashboardRefreshInterval { get; init; } = TimeSpan.FromMinutes(5);

    public string CommitUrl(string version) => $"{RepoUrl}/commit/{version}";
}

public sealed record BotCommand(
    ApplicationCommandProperties Definition,
    string Name,
    Func<SocketSlashCommandContext, Task> Handler,
    Func<SocketAutocompleteContext, Task>? AutocompleteHandler = null);

public sealed record SocketSlashCommandContext(
    Discord.WebSocket.DiscordSocketClient Client,
    Discord.WebSocket.SocketSlashCommand Command);

public sealed record SocketAutocompleteContext(
    Discord.WebSocket.DiscordSocketClient Client,
    Discord.WebSocket.SocketAutocompleteInteraction Interaction);
