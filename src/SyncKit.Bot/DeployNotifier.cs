using Discord;
using Discord.WebSocket;
using SyncKit.Contract;

namespace SyncKit.Bot;

// Renders a DeployResponse into the configured DeployNotifications thread as a real bot message.
// Resolves a per-event MessageSpec (embed or ComponentsV2 + mentions); missing/unresolvable thread
// is a silent no-op; a genuine send failure propagates so the caller can surface a 500.
public sealed class DeployNotifier(
    ChannelConfigStore configStore, DiscordSocketClient client, ulong guildId, string appName) {

    public async Task NotifyAsync(DeployResponse res, CancellationToken ct) {
        var cfg = await configStore.GetAsync(guildId.ToString(), appName, ct);
        if (cfg is null || string.IsNullOrEmpty(cfg.DeployNotificationsThreadId)) return;
        if (!ulong.TryParse(cfg.DeployNotificationsThreadId, out var threadId)) return;

        var guild = client.GetGuild(guildId);
        if (guild?.GetChannel(threadId) is not IMessageChannel channel) return;

        var (messageJson, embedJson, defaultEmbed) = res.Ok && res.AlreadyUpToDate
            ? (cfg.UptodateMessageJson, cfg.UptodateEmbedJson, DeployEmbedDefaults.AlreadyUpToDate)
            : res.Ok
                ? (cfg.SuccessMessageJson, cfg.SuccessEmbedJson, DeployEmbedDefaults.Success)
                : (cfg.FailureMessageJson, cfg.FailureEmbedJson, DeployEmbedDefaults.Failure);

        var spec = MessageSpecs.Resolve(messageJson, embedJson, defaultEmbed);
        var rendered = MessageRenderer.Render(spec, DeployVars.Build(res, appName));

        if (rendered.IsComponentsV2 && rendered.Components is not null) {
            await channel.SendMessageAsync(
                components: rendered.Components, flags: MessageFlags.ComponentsV2,
                allowedMentions: rendered.AllowedMentions);
        } else if (rendered.Embed is not null) {
            await channel.SendMessageAsync(embed: rendered.Embed, allowedMentions: rendered.AllowedMentions);
        }
    }
}
