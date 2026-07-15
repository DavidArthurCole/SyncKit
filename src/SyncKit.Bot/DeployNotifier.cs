using System.Text.Json;
using Discord;
using Discord.WebSocket;
using SyncKit.Contract;

namespace SyncKit.Bot;

// Renders a DeployResponse into the configured DeployNotifications thread as a real bot message.
// Missing/unresolvable thread is a silent no-op; a genuine send failure propagates so the caller
// can surface a 500.
public sealed class DeployNotifier(
    ChannelConfigStore configStore, DiscordSocketClient client, ulong guildId, string appName) {

    public async Task NotifyAsync(DeployResponse res, CancellationToken ct) {
        var cfg = await configStore.GetAsync(guildId.ToString(), appName, ct);
        if (cfg is null || string.IsNullOrEmpty(cfg.DeployNotificationsThreadId)) return;
        if (!ulong.TryParse(cfg.DeployNotificationsThreadId, out var threadId)) return;

        var guild = client.GetGuild(guildId);
        if (guild?.GetChannel(threadId) is not IMessageChannel channel) return;

        var (json, fallback) = res.Ok && res.AlreadyUpToDate
            ? (cfg.UptodateEmbedJson, DeployEmbedDefaults.AlreadyUpToDate)
            : res.Ok
                ? (cfg.SuccessEmbedJson, DeployEmbedDefaults.Success)
                : (cfg.FailureEmbedJson, DeployEmbedDefaults.Failure);

        var spec = ParseSpec(json) ?? fallback;
        var embed = EmbedRenderer.Render(spec, res, appName);
        await channel.SendMessageAsync(embed: embed);
    }

    private static EmbedSpec? ParseSpec(string? json) {
        if (string.IsNullOrEmpty(json)) return null;
        try {
            return JsonSerializer.Deserialize<EmbedSpec>(json);
        } catch (JsonException) {
            return null;
        }
    }
}
