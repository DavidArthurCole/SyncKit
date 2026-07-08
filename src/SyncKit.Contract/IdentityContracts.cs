using System.Text.Json.Serialization;

namespace SyncKit.Contract;

// Wire shapes for SyncKit.Identity.Host's HTTP API. Consumed by SyncKit.Identity.Client and,
// through it, by EggIncognito/EggLedger. Kept separate from the DB-facing models in
// SyncKit.Identity so the wire contract can stay frozen independent of internal schema changes.

public sealed class IdentityResolveRequest
{
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = "";

    [JsonPropertyName("subject")]
    public string Subject { get; set; } = "";

    [JsonPropertyName("discordId")]
    public string? DiscordId { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("avatar")]
    public string? Avatar { get; set; }
}

public sealed class IdentityResolveResponse
{
    [JsonPropertyName("userId")]
    public Guid UserId { get; set; }

    [JsonPropertyName("role")]
    public string Role { get; set; } = "";

    [JsonPropertyName("discordId")]
    public string? DiscordId { get; set; }

    [JsonPropertyName("isNew")]
    public bool IsNew { get; set; }
}

public sealed class IdentityUserResponse
{
    [JsonPropertyName("userId")]
    public Guid UserId { get; set; }

    [JsonPropertyName("discordId")]
    public string? DiscordId { get; set; }

    [JsonPropertyName("username")]
    public string Username { get; set; } = "";

    [JsonPropertyName("avatar")]
    public string? Avatar { get; set; }

    [JsonPropertyName("role")]
    public string Role { get; set; } = "";

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("lastLoginAt")]
    public DateTimeOffset LastLoginAt { get; set; }
}

public sealed class RevokeSessionRequest
{
    [JsonPropertyName("sid")]
    public string Sid { get; set; } = "";
}

public sealed class MergeUsersRequest
{
    [JsonPropertyName("keepUserId")]
    public Guid KeepUserId { get; set; }

    [JsonPropertyName("mergeUserId")]
    public Guid MergeUserId { get; set; }
}

public sealed class SetRoleRequest
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "";
}
