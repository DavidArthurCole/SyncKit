using Npgsql;
using SyncKit.Bot;
using SyncKit.Db;
using Xunit;

namespace SyncKit.Bot.Tests;

public class BotConfigServiceTests {
    private static string? ConnString => Environment.GetEnvironmentVariable("SYNCKIT_TEST_PG_CONN");

    private static async Task<NpgsqlDataSource> MakeDbAsync() {
        var dataSource = NpgsqlDataSource.Create(ConnString!);
        await using var conn = await dataSource.OpenConnectionAsync();
        await Migrator.MigrateAsync(conn, Path.Combine(AppContext.BaseDirectory, "Migrations"));
        return dataSource;
    }

    [Fact]
    public void ValidateEmbedJson_NullOrBlank_ReturnsNull() {
        Assert.Null(BotConfigService.ValidateEmbedJson(null));
        Assert.Null(BotConfigService.ValidateEmbedJson(""));
        Assert.Null(BotConfigService.ValidateEmbedJson("   "));
    }

    [Fact]
    public void ValidateEmbedJson_ValidSpec_ReturnsNull() {
        Assert.Null(BotConfigService.ValidateEmbedJson("{\"title\":\"ok\",\"description\":\"{{ from_hash }}\"}"));
    }

    [Fact]
    public void ValidateEmbedJson_MalformedJson_ReturnsMessage() {
        Assert.NotNull(BotConfigService.ValidateEmbedJson("{not json"));
    }

    [Fact]
    public void ValidateEmbedJson_BrokenTemplateField_ReturnsMessage() {
        Assert.NotNull(BotConfigService.ValidateEmbedJson("{\"description\":\"{{ 1 + }}\"}"));
    }

    [Fact]
    public void ValidateEmbedJson_BrokenTemplateInFields_ReturnsMessage() {
        Assert.NotNull(BotConfigService.ValidateEmbedJson("{\"fields\":[{\"name\":\"n\",\"value\":\"{{ 1 + }}\",\"inline\":false}]}"));
    }

    [Fact]
    public void DefaultsAndVariables_Exposed() {
        var svc = MakePureService();
        Assert.NotNull(svc.DefaultSuccess);
        Assert.NotNull(svc.DefaultFailure);
        Assert.NotNull(svc.DefaultAlreadyUpToDate);
        Assert.NotEmpty(svc.Variables);
    }

    [Fact]
    public async Task SaveAsync_BadEmbedJson_ReturnsErrorWithoutPersist() {
        if (string.IsNullOrEmpty(ConnString)) return;
        await using var db = await MakeDbAsync();
        var (svc, calls) = MakeDbService(db, "guild-bcs-bad");

        var result = await svc.SaveAsync(new BotConfigInput(null, null, null, "{not json", null, null), CancellationToken.None);

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
        Assert.StartsWith("Success embed invalid:", result.Error);
        Assert.Empty(calls);

        var store = new ChannelConfigStore(db);
        Assert.Null(await store.GetAsync("guild-bcs-bad", "eggledger", CancellationToken.None));
    }

    [Fact]
    public async Task SaveAsync_GithubThreadSet_CallsEnsureAndReturnsUrl() {
        if (string.IsNullOrEmpty(ConnString)) return;
        await using var db = await MakeDbAsync();
        var (svc, calls) = MakeDbService(db, "guild-bcs-ensure");

        var result = await svc.SaveAsync(new BotConfigInput(null, "12345", null, null, null, null), CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal("http://hook/stub", result.GithubWebhookUrl);
        Assert.Contains(("ensure", 12345UL), calls);
    }

    [Fact]
    public async Task SaveAsync_GithubThreadCleared_CallsTeardown() {
        if (string.IsNullOrEmpty(ConnString)) return;
        await using var db = await MakeDbAsync();
        var (svc, calls) = MakeDbService(db, "guild-bcs-teardown");

        var result = await svc.SaveAsync(new BotConfigInput(null, null, null, null, null, null), CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Null(result.GithubWebhookUrl);
        Assert.Contains(("teardown", 0UL), calls);
    }

    [Fact]
    public async Task GetAsync_PrefillsThreadIdsFromStateStore() {
        if (string.IsNullOrEmpty(ConnString)) return;
        await using var db = await MakeDbAsync();
        var stateStore = new ChannelStateStore(db);
        await stateStore.UpsertAsync("guild-bcs-prefill", "eggledger", $"thread:{ThreadKinds.ToName(ThreadKind.GithubFeed)}", "999", null, CancellationToken.None);
        await stateStore.UpsertAsync("guild-bcs-prefill", "eggledger", $"thread:{ThreadKinds.ToName(ThreadKind.DeployNotifications)}", "888", null, CancellationToken.None);

        var (svc, _) = MakeDbService(db, "guild-bcs-prefill");
        var view = await svc.GetAsync(CancellationToken.None);

        Assert.Equal("999", view.GithubFeedThreadId);
        Assert.Equal("888", view.DeployNotificationsThreadId);
        Assert.Null(view.GithubWebhookUrl);
    }

    private static BotConfigService MakePureService() {
        var db = NpgsqlDataSource.Create("Host=localhost");
        return new BotConfigService("g", "eggledger",
            new ChannelConfigStore(db), new ChannelStateStore(db),
            (_, _, _) => Task.FromResult<string?>(null),
            (_, _) => Task.CompletedTask);
    }

    private static (BotConfigService Service, List<(string Op, ulong Id)> Calls) MakeDbService(NpgsqlDataSource db, string guildId) {
        var calls = new List<(string Op, ulong Id)>();
        var svc = new BotConfigService(guildId, "eggledger",
            new ChannelConfigStore(db), new ChannelStateStore(db),
            (_, id, _) => { calls.Add(("ensure", id)); return Task.FromResult<string?>("http://hook/stub"); },
            (_, _) => { calls.Add(("teardown", 0UL)); return Task.CompletedTask; });
        return (svc, calls);
    }
}
