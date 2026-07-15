using System.Text.Json;
using Scriban;
using SyncKit.Contract;

namespace SyncKit.Bot;

public sealed record BotConfigView(
    string? DashboardChannelId,
    string? GithubFeedThreadId,
    string? DeployNotificationsThreadId,
    string? GithubWebhookUrl,
    string? SuccessEmbedJson,
    string? FailureEmbedJson,
    string? UptodateEmbedJson,
    EmbedSpec DefaultSuccess,
    EmbedSpec DefaultFailure,
    EmbedSpec DefaultUptodate,
    IReadOnlyList<VariableDoc> Variables);

public sealed record BotConfigInput(
    string? DashboardChannelId,
    string? GithubFeedThreadId,
    string? DeployNotificationsThreadId,
    string? SuccessEmbedJson,
    string? FailureEmbedJson,
    string? UptodateEmbedJson);

public sealed record SaveResult(bool Ok, string? Error, string? GithubWebhookUrl);

public sealed record VariableDoc(string Name, string Desc);

// UI-agnostic bot config logic: no HTTP, no HTML, no Discord types. Consumers own the UI and
// supply ensure/teardown webhook delegates so this stays free of ChannelHub/SocketGuild.
public sealed class BotConfigService(
    string guildId, string appName,
    ChannelConfigStore configStore, ChannelStateStore stateStore,
    Func<ThreadKind, ulong, CancellationToken, Task<string?>> ensureWebhook,
    Func<ThreadKind, CancellationToken, Task> teardownWebhook) {

    public async Task<BotConfigView> GetAsync(CancellationToken ct) {
        var cc = await configStore.GetAsync(guildId, appName, ct);

        var githubFeedThreadId = cc?.GithubFeedThreadId ?? await ResolveThreadIdAsync(ThreadKind.GithubFeed, ct);
        var deployNotificationsThreadId = cc?.DeployNotificationsThreadId ?? await ResolveThreadIdAsync(ThreadKind.DeployNotifications, ct);

        return new BotConfigView(
            cc?.DashboardChannelId,
            githubFeedThreadId,
            deployNotificationsThreadId,
            null,
            cc?.SuccessEmbedJson,
            cc?.FailureEmbedJson,
            cc?.UptodateEmbedJson,
            DeployEmbedDefaults.Success,
            DeployEmbedDefaults.Failure,
            DeployEmbedDefaults.AlreadyUpToDate,
            Variables);
    }

    public async Task<SaveResult> SaveAsync(BotConfigInput input, CancellationToken ct) {
        if (ValidateEmbedJson(input.SuccessEmbedJson) is string se)
            return new SaveResult(false, $"Success embed invalid: {se}", null);
        if (ValidateEmbedJson(input.FailureEmbedJson) is string fe)
            return new SaveResult(false, $"Failure embed invalid: {fe}", null);
        if (ValidateEmbedJson(input.UptodateEmbedJson) is string ue)
            return new SaveResult(false, $"Already-up-to-date embed invalid: {ue}", null);

        var dashboardChannelId = Blank(input.DashboardChannelId);
        var githubFeedThreadId = Blank(input.GithubFeedThreadId);
        var deployNotificationsThreadId = Blank(input.DeployNotificationsThreadId);

        await configStore.UpsertAsync(guildId, appName,
            dashboardChannelId, githubFeedThreadId, deployNotificationsThreadId,
            Blank(input.SuccessEmbedJson), Blank(input.FailureEmbedJson), Blank(input.UptodateEmbedJson),
            ct);

        string? githubWebhookUrl = null;
        if (githubFeedThreadId is not null && ulong.TryParse(githubFeedThreadId, out var threadId)) {
            githubWebhookUrl = await ensureWebhook(ThreadKind.GithubFeed, threadId, ct);
        } else {
            await teardownWebhook(ThreadKind.GithubFeed, ct);
        }

        return new SaveResult(true, null, githubWebhookUrl);
    }

    public IReadOnlyList<VariableDoc> Variables { get; } = BuildVariables();

    public EmbedSpec DefaultSuccess { get; } = DeployEmbedDefaults.Success;
    public EmbedSpec DefaultFailure { get; } = DeployEmbedDefaults.Failure;
    public EmbedSpec DefaultAlreadyUpToDate { get; } = DeployEmbedDefaults.AlreadyUpToDate;

    // Returns null when valid (or absent), else a short message naming the first broken part.
    public static string? ValidateEmbedJson(string? json) {
        if (string.IsNullOrWhiteSpace(json)) return null;

        EmbedSpec? spec;
        try {
            spec = JsonSerializer.Deserialize<EmbedSpec>(json);
        } catch (JsonException ex) {
            return ex.Message;
        }
        if (spec is null) return "empty spec";

        foreach (var s in new[] { spec.AuthorName, spec.AuthorUrl, spec.AuthorIconUrl, spec.Title, spec.TitleUrl, spec.Description, spec.ImageUrl, spec.ThumbnailUrl, spec.FooterText, spec.FooterIconUrl }) {
            if (BadTemplate(s)) return "template parse error";
        }
        if (spec.Fields is not null) {
            foreach (var f in spec.Fields) {
                if (BadTemplate(f.Name) || BadTemplate(f.Value)) return "field template parse error";
            }
        }
        return null;
    }

    private async Task<string?> ResolveThreadIdAsync(ThreadKind kind, CancellationToken ct) {
        var state = await stateStore.GetAsync(guildId, appName, $"thread:{ThreadKinds.ToName(kind)}", ct);
        return state?.DiscordId;
    }

    private static List<VariableDoc> BuildVariables() {
        var list = new List<VariableDoc>(DeployEmbedDefaults.Variables.Count);
        foreach (var (name, desc) in DeployEmbedDefaults.Variables) list.Add(new VariableDoc(name, desc));
        return list;
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static bool BadTemplate(string? s) =>
        !string.IsNullOrEmpty(s) && Template.Parse(s).HasErrors;
}
