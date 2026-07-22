namespace SyncKit.Auth;

public sealed record SessionUser(
    string UserId,
    string? Sid,
    string Role,
    string? Name = null,
    string? Avatar = null,
    string? DiscordId = null);
