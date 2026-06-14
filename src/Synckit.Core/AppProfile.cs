using Synckit.Bot;
using Synckit.Contract;

namespace Synckit.Core;

// Ports Go synckit.AppProfile + DiscordConfig + AgentRef + DBConfig + EventHandlers.
public sealed class AppProfile
{
    public string Name { get; init; } = "";
    public string RepoUrl { get; init; } = "";
    public DiscordConfig Discord { get; init; } = new();
    public VerifyInfo Build { get; init; } = new();
    public AgentRef DeployAgent { get; init; } = new();
    public DbConfig? Db { get; init; }
    public IReadOnlyList<BotCommand> Commands { get; init; } = Array.Empty<BotCommand>();
    public EventHandlers Events { get; init; } = new();
}

public sealed class DiscordConfig
{
    public string Token { get; init; } = "";
    public string AppId { get; init; } = "";
    public string GuildId { get; init; } = "";
    public string OAuthClientId { get; init; } = "";
    public string OAuthClientSecret { get; init; } = "";
    public string OAuthRedirectUrl { get; init; } = "";
    public string SharedRoleId { get; init; } = "";
}

public sealed class AgentRef
{
    public string Url { get; init; } = "";
    public string Secret { get; init; } = "";
}

// Nil Db => run DB-free.
public sealed class DbConfig
{
    public string ConnStr { get; init; } = "";
    public string MigrationsDir { get; init; } = "";
}

public sealed class EventHandlers
{
    public Func<NewVersionEvent, Task>? NewVersion { get; init; }
    public string EventSecret { get; init; } = "";
}
