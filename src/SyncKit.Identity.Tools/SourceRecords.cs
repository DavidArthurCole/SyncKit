namespace SyncKit.Identity.Tools;

public sealed record SourceUser(
    Guid UserId, string? DiscordId, string Username, string? Avatar, string? Role,
    DateTimeOffset CreatedAt, DateTimeOffset? LastLoginAt);

public sealed record SourceIdentity(Guid UserId, string Provider, string Subject, DateTimeOffset LinkedAt);

public sealed record SourceSnapshot(IReadOnlyList<SourceUser> Users, IReadOnlyList<SourceIdentity> Identities);
