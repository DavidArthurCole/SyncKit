using System.Text.Json.Serialization;

namespace SyncKit.Contract;

[JsonConverter(typeof(JsonStringEnumConverter<MessageKind>))]
public enum MessageKind {
    [JsonStringEnumMemberName("embed")] Embed,
    [JsonStringEnumMemberName("components")] Components,
}

public sealed record MessageSpec(
    [property: JsonPropertyName("kind")] MessageKind Kind,
    [property: JsonPropertyName("embed")] EmbedSpec? Embed,
    [property: JsonPropertyName("components")] ComponentSpec? Components,
    [property: JsonPropertyName("mentions")] MentionSpec? Mentions) {

    public static MessageSpec FromEmbed(EmbedSpec embed) =>
        new(MessageKind.Embed, embed, null, null);
}

public sealed record ComponentSpec(
    [property: JsonPropertyName("accentColor")] uint? AccentColor,
    [property: JsonPropertyName("blocks")] IReadOnlyList<ComponentBlock> Blocks);

public sealed record ComponentBlock(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("text")] string? Text,
    [property: JsonPropertyName("thumbnailUrl")] string? ThumbnailUrl,
    [property: JsonPropertyName("divider")] bool Divider = true);

public sealed record MentionSpec(
    [property: JsonPropertyName("users")] IReadOnlyList<string> Users,
    [property: JsonPropertyName("roles")] IReadOnlyList<string> Roles,
    [property: JsonPropertyName("everyone")] bool Everyone);
