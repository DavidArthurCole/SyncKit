using System.Text.Json.Serialization;

namespace SyncKit.Contract;

public sealed record EmbedSpec(
    [property: JsonPropertyName("color")] uint? Color,
    [property: JsonPropertyName("authorName")] string? AuthorName,
    [property: JsonPropertyName("authorUrl")] string? AuthorUrl,
    [property: JsonPropertyName("authorIconUrl")] string? AuthorIconUrl,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("titleUrl")] string? TitleUrl,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("fields")] IReadOnlyList<EmbedFieldSpec> Fields,
    [property: JsonPropertyName("imageUrl")] string? ImageUrl,
    [property: JsonPropertyName("thumbnailUrl")] string? ThumbnailUrl,
    [property: JsonPropertyName("footerText")] string? FooterText,
    [property: JsonPropertyName("footerIconUrl")] string? FooterIconUrl,
    [property: JsonPropertyName("timestamp")] bool Timestamp);

public sealed record EmbedFieldSpec(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("inline")] bool Inline);
