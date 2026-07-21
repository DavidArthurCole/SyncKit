using System.Text.Json;
using SyncKit.Contract;

namespace SyncKit.Bot;

// Read-precedence resolution for a configurable message slot: an explicit MessageSpec wins, else a
// legacy EmbedSpec (pre-0.7.0-preview.4 storage) wrapped as an embed message, else the default embed.
public static class MessageSpecs {
    public static MessageSpec Resolve(string? messageJson, string? embedJson, EmbedSpec defaultEmbed) {
        if (ParseMessage(messageJson) is { } message) return message;
        if (ParseEmbed(embedJson) is { } embed) return MessageSpec.FromEmbed(embed);
        return MessageSpec.FromEmbed(defaultEmbed);
    }

    public static MessageSpec? ParseMessage(string? json) {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try {
            return JsonSerializer.Deserialize<MessageSpec>(json);
        } catch (JsonException) {
            return null;
        }
    }

    public static EmbedSpec? ParseEmbed(string? json) {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try {
            return JsonSerializer.Deserialize<EmbedSpec>(json);
        } catch (JsonException) {
            return null;
        }
    }
}
