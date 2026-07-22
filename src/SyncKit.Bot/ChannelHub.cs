using Discord;
using Discord.WebSocket;
using SyncKit.Contract;

namespace SyncKit.Bot;

public sealed class ChannelHub(
    SocketGuild guild, ulong dashboardChannelId, string appName,
    ChannelStateStore store, ChannelConfigStore configStore) {
    private const string DashboardKind = "dashboard";
    private static string ThreadStateKind(ThreadKind kind) => $"thread:{ThreadKinds.ToName(kind)}";

    private string? _lastSignature;

    public async Task UpdateDashboardAsync(DashboardSnapshot snapshot, CancellationToken ct) {
        if (guild.GetChannel(dashboardChannelId) is not ITextChannel channel) return;

        var config = await configStore.GetAsync(guild.Id.ToString(), appName, ct);
        var spec = MessageSpecs.ParseEmbed(config?.DashboardEmbedJson) ?? DashboardEmbedDefaults.Default;
        var signature = DashboardSignature.Of(snapshot) + "|spec|" + (config?.DashboardEmbedJson ?? "");

        var existing = await store.GetAsync(guild.Id.ToString(), appName, DashboardKind, ct);
        var hasMessage = existing is not null && ulong.TryParse(existing.DiscordId, out _);
        if (hasMessage && signature == _lastSignature) return;

        var embed = EmbedRenderer.Render(spec, DashboardVars.Build(snapshot));
        if (existing is not null && ulong.TryParse(existing.DiscordId, out var messageId)) {
            try {
                await channel.ModifyMessageAsync(messageId, m => m.Embed = embed);
                _lastSignature = signature;
                return;
            } catch (Discord.Net.HttpException ex) when (ex.HttpCode == System.Net.HttpStatusCode.NotFound) {
            }
        }

        var posted = await channel.SendMessageAsync(embed: embed);
        await store.UpsertAsync(guild.Id.ToString(), appName, DashboardKind, posted.Id.ToString(), null, ct);
        _lastSignature = signature;
    }

    public async Task<string?> EnsureWebhookForThreadAsync(ThreadKind kind, ulong threadId, CancellationToken ct) {
        var stateKind = ThreadStateKind(kind);
        await store.UpsertAsync(guild.Id.ToString(), appName, stateKind, threadId.ToString(), null, ct);

        if (ResolveParentTextChannel(threadId) is not ITextChannel parent) return null;

        var webhookKind = $"{stateKind}:webhook";
        var existing = await store.GetAsync(guild.Id.ToString(), appName, webhookKind, ct);
        if (existing is { WebhookToken: { Length: > 0 } token })
            return $"https://discord.com/api/webhooks/{existing.DiscordId}/{token}?thread_id={threadId}";

        var webhook = await parent.CreateWebhookAsync($"{appName}-{ThreadKinds.ToName(kind)}");
        await store.UpsertAsync(guild.Id.ToString(), appName, webhookKind, webhook.Id.ToString(), webhook.Token, ct);
        return $"https://discord.com/api/webhooks/{webhook.Id}/{webhook.Token}?thread_id={threadId}";
    }

    public async Task TeardownWebhookForThreadAsync(ThreadKind kind, CancellationToken ct) {
        var stateKind = ThreadStateKind(kind);
        var threadState = await store.GetAsync(guild.Id.ToString(), appName, stateKind, ct);
        var parent = threadState is not null && ulong.TryParse(threadState.DiscordId, out var threadId)
            ? ResolveParentTextChannel(threadId)
            : null;

        var webhookKind = $"{stateKind}:webhook";
        var webhookState = await store.GetAsync(guild.Id.ToString(), appName, webhookKind, ct);
        if (webhookState is not null && ulong.TryParse(webhookState.DiscordId, out var webhookId) && parent is not null) {
            try {
                var webhook = await parent.GetWebhookAsync(webhookId);
                if (webhook is not null) await webhook.DeleteAsync();
            } catch (Discord.Net.HttpException ex) when (ex.HttpCode == System.Net.HttpStatusCode.NotFound) { }
        }
        await store.DeleteAsync(guild.Id.ToString(), appName, webhookKind, ct);
        await store.DeleteAsync(guild.Id.ToString(), appName, stateKind, ct);
    }

    private ITextChannel? ResolveParentTextChannel(ulong threadId) =>
        guild.GetChannel(threadId) is SocketThreadChannel thread ? thread.ParentChannel as ITextChannel : null;
}
