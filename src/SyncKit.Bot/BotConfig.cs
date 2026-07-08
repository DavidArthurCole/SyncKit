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
    public IReadOnlyList<BotCommand> Extra { get; init; } = Array.Empty<BotCommand>();

    // ChannelHub is enabled only when DashboardChannelId is set. EnabledThreads is a CSV of
    // ThreadKind names; unset/empty = all threads off (see ThreadKinds.ParseCsv).
    public string DashboardChannelId { get; init; } = "";
    public string EnabledThreads { get; init; } = "";
    public string PostgresConnectionString { get; init; } = "";

    public string CommitUrl(string version) => $"{RepoUrl}/commit/{version}";
}

// App-supplied slash command + handler. Handler receives the interaction to respond to.
public sealed record BotCommand(
    ApplicationCommandProperties Definition,
    string Name,
    Func<SocketSlashCommandContext, Task> Handler);

// Lightweight context wrapper so app handlers don't depend on the raw socket type shape.
public sealed record SocketSlashCommandContext(
    Discord.WebSocket.DiscordSocketClient Client,
    Discord.WebSocket.SocketSlashCommand Command);
