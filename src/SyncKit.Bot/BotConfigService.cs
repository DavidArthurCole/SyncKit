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
    IReadOnlyList<VariableDoc> Variables,
    string? DashboardEmbedJson,
    EmbedSpec DefaultDashboard,
    IReadOnlyList<VariableDoc> DashboardVariables,
    string? SuccessMessageJson,
    string? FailureMessageJson,
    string? UptodateMessageJson,
    MessageSpec DefaultSuccessMessage,
    MessageSpec DefaultFailureMessage,
    MessageSpec DefaultUptodateMessage);

public sealed record BotConfigInput(
    string? DashboardChannelId,
    string? GithubFeedThreadId,
    string? DeployNotificationsThreadId,
    string? SuccessEmbedJson,
    string? FailureEmbedJson,
    string? UptodateEmbedJson,
    string? DashboardEmbedJson = null,
    string? SuccessMessageJson = null,
    string? FailureMessageJson = null,
    string? UptodateMessageJson = null);

public sealed record SaveResult(bool Ok, string? Error, string? GithubWebhookUrl);

public sealed record VariableDoc(string Name, string Desc);

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
            Variables,
            cc?.DashboardEmbedJson,
            DashboardEmbedDefaults.Default,
            DashboardVariables,
            cc?.SuccessMessageJson,
            cc?.FailureMessageJson,
            cc?.UptodateMessageJson,
            DefaultSuccessMessage,
            DefaultFailureMessage,
            DefaultUptodateMessage);
    }

    public async Task<SaveResult> SaveAsync(BotConfigInput input, CancellationToken ct) {
        if (ValidateEmbedJson(input.SuccessEmbedJson) is string se)
            return new SaveResult(false, $"Success embed invalid: {se}", null);
        if (ValidateEmbedJson(input.FailureEmbedJson) is string fe)
            return new SaveResult(false, $"Failure embed invalid: {fe}", null);
        if (ValidateEmbedJson(input.UptodateEmbedJson) is string ue)
            return new SaveResult(false, $"Already-up-to-date embed invalid: {ue}", null);
        if (ValidateEmbedJson(input.DashboardEmbedJson) is string de)
            return new SaveResult(false, $"Dashboard embed invalid: {de}", null);
        if (ValidateMessageJson(input.SuccessMessageJson) is string sm)
            return new SaveResult(false, $"Success message invalid: {sm}", null);
        if (ValidateMessageJson(input.FailureMessageJson) is string fm)
            return new SaveResult(false, $"Failure message invalid: {fm}", null);
        if (ValidateMessageJson(input.UptodateMessageJson) is string um)
            return new SaveResult(false, $"Already-up-to-date message invalid: {um}", null);

        var githubFeedThreadId = Blank(input.GithubFeedThreadId);

        var config = new ChannelConfig(
            guildId, appName,
            Blank(input.DashboardChannelId), githubFeedThreadId, Blank(input.DeployNotificationsThreadId),
            Blank(input.SuccessEmbedJson), Blank(input.FailureEmbedJson), Blank(input.UptodateEmbedJson),
            Blank(input.DashboardEmbedJson),
            Blank(input.SuccessMessageJson), Blank(input.FailureMessageJson), Blank(input.UptodateMessageJson));

        await configStore.UpsertAsync(config, ct);

        string? githubWebhookUrl = null;
        if (githubFeedThreadId is not null && ulong.TryParse(githubFeedThreadId, out var threadId)) {
            githubWebhookUrl = await ensureWebhook(ThreadKind.GithubFeed, threadId, ct);
        } else {
            await teardownWebhook(ThreadKind.GithubFeed, ct);
        }

        return new SaveResult(true, null, githubWebhookUrl);
    }

    public IReadOnlyList<VariableDoc> Variables { get; } = BuildVariables(DeployEmbedDefaults.Variables);
    public IReadOnlyList<VariableDoc> DashboardVariables { get; } = BuildVariables(DashboardEmbedDefaults.Variables);

    public EmbedSpec DefaultSuccess { get; } = DeployEmbedDefaults.Success;
    public EmbedSpec DefaultFailure { get; } = DeployEmbedDefaults.Failure;
    public EmbedSpec DefaultAlreadyUpToDate { get; } = DeployEmbedDefaults.AlreadyUpToDate;
    public EmbedSpec DefaultDashboard { get; } = DashboardEmbedDefaults.Default;

    public MessageSpec DefaultSuccessMessage { get; } = MessageSpec.FromEmbed(DeployEmbedDefaults.Success);
    public MessageSpec DefaultFailureMessage { get; } = MessageSpec.FromEmbed(DeployEmbedDefaults.Failure);
    public MessageSpec DefaultUptodateMessage { get; } = MessageSpec.FromEmbed(DeployEmbedDefaults.AlreadyUpToDate);

    public static string? ValidateEmbedJson(string? json) {
        if (string.IsNullOrWhiteSpace(json)) return null;

        EmbedSpec? spec;
        try {
            spec = JsonSerializer.Deserialize<EmbedSpec>(json);
        } catch (JsonException ex) {
            return ex.Message;
        }
        if (spec is null) return "empty spec";
        return ValidateEmbedSpec(spec);
    }

    public static string? ValidateMessageJson(string? json) {
        if (string.IsNullOrWhiteSpace(json)) return null;

        MessageSpec? spec;
        try {
            spec = JsonSerializer.Deserialize<MessageSpec>(json);
        } catch (JsonException ex) {
            return ex.Message;
        }
        if (spec is null) return "empty spec";
        if (spec.Embed is not null && ValidateEmbedSpec(spec.Embed) is string ee) return ee;
        if (spec.Components is not null) {
            foreach (var block in spec.Components.Blocks ?? []) {
                if (BadTemplate(block.Text)) return "component template parse error";
            }
        }
        return null;
    }

    private static string? ValidateEmbedSpec(EmbedSpec spec) {
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

    private static List<VariableDoc> BuildVariables(IReadOnlyList<(string Name, string Desc)> source) {
        var list = new List<VariableDoc>(source.Count);
        foreach (var (name, desc) in source) list.Add(new VariableDoc(name, desc));
        return list;
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static bool BadTemplate(string? s) =>
        !string.IsNullOrEmpty(s) && Template.Parse(s).HasErrors;
}
