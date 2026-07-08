namespace SyncKit.Identity.Tools;

// Raw shapes read from each app's own database, before any merge decision. Deliberately not
// SyncKit.Identity.Models.User - those source schemas have already-diverged quirks (EggLedger's
// avatar_url vs EggIncognito's avatar, EggLedger's created_at as unix-seconds bigint) that need
// normalizing, not reusing.
public sealed record SourceUser(
    Guid UserId, string? DiscordId, string Username, string? Avatar, string? Role,
    DateTimeOffset CreatedAt, DateTimeOffset? LastLoginAt);

public sealed record SourceIdentity(Guid UserId, string Provider, string Subject, DateTimeOffset LinkedAt);

public sealed record SourceSnapshot(IReadOnlyList<SourceUser> Users, IReadOnlyList<SourceIdentity> Identities);
