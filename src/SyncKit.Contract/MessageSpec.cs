using System.Text.Json.Serialization;

namespace SyncKit.Contract;

[JsonConverter(typeof(JsonStringEnumConverter<MessageKind>))]
public enum MessageKind {
    [JsonStringEnumMemberName("embed")] Embed,
    [JsonStringEnumMemberName("components")] Components,
}

// A configurable message: either a classic embed or a ComponentsV2 layout, plus an optional
// mention allowlist. Embeds and components can't coexist in one Discord message, so Kind picks one.
public sealed record MessageSpec(
    [property: JsonPropertyName("kind")] MessageKind Kind,
    [property: JsonPropertyName("embed")] EmbedSpec? Embed,
    [property: JsonPropertyName("components")] ComponentSpec? Components,
    [property: JsonPropertyName("mentions")] MentionSpec? Mentions) {

    public static MessageSpec FromEmbed(EmbedSpec embed) =>
        new(MessageKind.Embed, embed, null, null);
}

// A pragmatic ComponentsV2 subset: an accent-colored container holding an ordered block list.
public sealed record ComponentSpec(
    [property: JsonPropertyName("accentColor")] uint? AccentColor,
    [property: JsonPropertyName("blocks")] IReadOnlyList<ComponentBlock> Blocks);

// Block Kind: "text" (TextDisplay markdown, templatable), "section" (Text + ThumbnailUrl accessory),
// "separator" (spacing, Divider draws a line). Text is a Scriban template.
public sealed record ComponentBlock(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("text")] string? Text,
    [property: JsonPropertyName("thumbnailUrl")] string? ThumbnailUrl,
    [property: JsonPropertyName("divider")] bool Divider = true);

// allowed_mentions allowlist. Users/Roles are explicit snowflake ids allowed to ping; Everyone
// permits @everyone/@here. Empty = no pings. Mentions only ping on the components path.
public sealed record MentionSpec(
    [property: JsonPropertyName("users")] IReadOnlyList<string> Users,
    [property: JsonPropertyName("roles")] IReadOnlyList<string> Roles,
    [property: JsonPropertyName("everyone")] bool Everyone);
