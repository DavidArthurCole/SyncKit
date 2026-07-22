using System.Text.Json.Serialization;

namespace SyncKit.Contract;


public sealed class IdentityResolveRequest {
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

public sealed class IdentityResolveResponse {
    [JsonPropertyName("userId")]
    public Guid UserId { get; set; }

    [JsonPropertyName("role")]
    public string Role { get; set; } = "";

    [JsonPropertyName("discordId")]
    public string? DiscordId { get; set; }

    [JsonPropertyName("isNew")]
    public bool IsNew { get; set; }
}

public sealed class IdentityUserResponse {
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

public sealed class RevokeSessionRequest {
    [JsonPropertyName("sid")]
    public string Sid { get; set; } = "";
}

public sealed class MergeUsersRequest {
    [JsonPropertyName("keepUserId")]
    public Guid KeepUserId { get; set; }

    [JsonPropertyName("mergeUserId")]
    public Guid MergeUserId { get; set; }
}

public sealed class SetRoleRequest {
    [JsonPropertyName("role")]
    public string Role { get; set; } = "";
}

public sealed class RedeemLoginCodeRequest {
    [JsonPropertyName("code")]
    public string Code { get; set; } = "";
}

public sealed class RedeemLoginCodeResponse {
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

    [JsonPropertyName("isNew")]
    public bool IsNew { get; set; }
}

public sealed class LoginSourceResponse {
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("iconUrl")]
    public string? IconUrl { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";
}

public sealed class LoginSourcesResponse {
    [JsonPropertyName("sources")]
    public List<LoginSourceResponse> Sources { get; set; } = [];
}
