using Discord;
using Discord.WebSocket;
using SyncKit.Contract;

namespace SyncKit.Bot;

// Owns the dashboard message + feature threads for one guild: upsert-in-place dashboard embed,
// lazy thread create/archive per ThreadKind, webhook create/teardown for webhook-backed kinds
// (currently just GithubFeed - GitHub posts straight to the returned execute URL).
public sealed class ChannelHub(SocketGuild guild, ulong dashboardChannelId, string appName, ChannelStateStore store)
{
    private const string DashboardKind = "dashboard";
    private static string ThreadStateKind(ThreadKind kind) => $"thread:{ThreadKinds.ToName(kind)}";

    public async Task UpdateDashboardAsync(DashboardSnapshot snapshot, CancellationToken ct)
    {
        if (guild.GetChannel(dashboardChannelId) is not ITextChannel channel) return;
        var embed = DefaultEmbeds.Dashboard(snapshot);

        var existing = await store.GetAsync(guild.Id.ToString(), appName, DashboardKind, ct);
        if (existing is not null && ulong.TryParse(existing.DiscordId, out var messageId))
        {
            try
            {
                await channel.ModifyMessageAsync(messageId, m => m.Embed = embed);
                return;
            }
            catch (Discord.Net.HttpException ex) when (ex.HttpCode == System.Net.HttpStatusCode.NotFound)
            {
                // Message was deleted out-of-band; fall through and re-create.
            }
        }

        var posted = await channel.SendMessageAsync(embed: embed);
        await store.UpsertAsync(guild.Id.ToString(), appName, DashboardKind, posted.Id.ToString(), null, ct);
    }

    // Creates/archives threads to match the enabled set. Never deletes a thread on disable -
    // archives it (history preserved) and tears down any webhook that kind owned.
    public async Task SyncEnabledThreadsAsync(IReadOnlyList<ThreadKind> enabled, CancellationToken ct)
    {
        if (guild.GetChannel(dashboardChannelId) is not SocketTextChannel channel) return;
        var enabledSet = enabled.ToHashSet();

        foreach (var kind in Enum.GetValues<ThreadKind>())
        {
            if (enabledSet.Contains(kind))
                await EnsureThreadAsync(channel, kind, ct);
            else
                await ArchiveThreadIfPresentAsync(channel, kind, ct);
        }
    }

    // Returns the webhook execute URL scoped to this thread, creating the webhook if missing.
    // Caller pastes this into GitHub's repo webhook settings; SyncKit.Bot cannot reach GitHub itself.
    public async Task<string?> GetOrCreateWebhookUrlAsync(ThreadKind kind, CancellationToken ct)
    {
        if (guild.GetChannel(dashboardChannelId) is not ITextChannel channel) return null;
        var stateKind = ThreadStateKind(kind);
        var threadState = await store.GetAsync(guild.Id.ToString(), appName, stateKind, ct);
        if (threadState is null || !ulong.TryParse(threadState.DiscordId, out var threadId)) return null;

        var webhookKind = $"{stateKind}:webhook";
        var existing = await store.GetAsync(guild.Id.ToString(), appName, webhookKind, ct);
        if (existing is { WebhookToken: { Length: > 0 } token })
            return $"https://discord.com/api/webhooks/{existing.DiscordId}/{token}?thread_id={threadId}";

        var webhook = await channel.CreateWebhookAsync($"{appName}-{ThreadKinds.ToName(kind)}");
        await store.UpsertAsync(guild.Id.ToString(), appName, webhookKind, webhook.Id.ToString(), webhook.Token, ct);
        return $"https://discord.com/api/webhooks/{webhook.Id}/{webhook.Token}?thread_id={threadId}";
    }

    private async Task EnsureThreadAsync(SocketTextChannel channel, ThreadKind kind, CancellationToken ct)
    {
        var stateKind = ThreadStateKind(kind);
        var existing = await store.GetAsync(guild.Id.ToString(), appName, stateKind, ct);
        if (existing is not null && ulong.TryParse(existing.DiscordId, out var existingId) &&
            guild.GetChannel(existingId) is SocketThreadChannel liveThread)
        {
            if (liveThread.IsArchived)
                await liveThread.ModifyAsync(t => t.Archived = false);
            return;
        }

        var thread = await channel.CreateThreadAsync(
            ThreadKinds.ToName(kind),
            ThreadType.PublicThread,
            autoArchiveDuration: ThreadArchiveDuration.OneWeek);
        await store.UpsertAsync(guild.Id.ToString(), appName, stateKind, thread.Id.ToString(), null, ct);
    }

    private async Task ArchiveThreadIfPresentAsync(SocketTextChannel channel, ThreadKind kind, CancellationToken ct)
    {
        var stateKind = ThreadStateKind(kind);
        var existing = await store.GetAsync(guild.Id.ToString(), appName, stateKind, ct);
        if (existing is null || !ulong.TryParse(existing.DiscordId, out var threadId)) return;

        if (guild.GetChannel(threadId) is SocketThreadChannel { IsArchived: false } liveThread)
            await liveThread.ModifyAsync(t => t.Archived = true);

        var webhookKind = $"{stateKind}:webhook";
        var webhookState = await store.GetAsync(guild.Id.ToString(), appName, webhookKind, ct);
        if (webhookState is not null && ulong.TryParse(webhookState.DiscordId, out var webhookId))
        {
            try
            {
                var webhook = await channel.GetWebhookAsync(webhookId);
                if (webhook is not null) await webhook.DeleteAsync();
            }
            catch (Discord.Net.HttpException ex) when (ex.HttpCode == System.Net.HttpStatusCode.NotFound) { }
            await store.DeleteAsync(guild.Id.ToString(), appName, webhookKind, ct);
        }
    }
}
