using Npgsql;
using SyncKit.Bot;
using SyncKit.Db;
using Xunit;

namespace SyncKit.Bot.Tests;

public class ChannelConfigStoreTests {
    private static string? ConnString => Environment.GetEnvironmentVariable("SYNCKIT_TEST_PG_CONN");

    private static async Task<NpgsqlDataSource> MakeDbAsync() {
        var dataSource = NpgsqlDataSource.Create(ConnString!);
        await using var conn = await dataSource.OpenConnectionAsync();
        await Migrator.MigrateAsync(conn, Path.Combine(AppContext.BaseDirectory, "Migrations"));
        return dataSource;
    }

    [Fact]
    public async Task GetAsync_NoRow_ReturnsNull() {
        if (string.IsNullOrEmpty(ConnString)) return;
        await using var db = await MakeDbAsync();
        var store = new ChannelConfigStore(db);

        var result = await store.GetAsync("guild-cc-missing", "eggledger", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task UpsertAsync_ThenGetAsync_RoundTrips() {
        if (string.IsNullOrEmpty(ConnString)) return;
        await using var db = await MakeDbAsync();
        var store = new ChannelConfigStore(db);

        await store.UpsertAsync(new ChannelConfig("guild-cc-1", "eggledger", "111", "222", "333",
            "{\"title\":\"ok\"}", "{\"title\":\"fail\"}", "{\"title\":\"utd\"}", "{\"title\":\"dash\"}",
            "{\"kind\":\"components\"}", null, null), CancellationToken.None);
        var result = await store.GetAsync("guild-cc-1", "eggledger", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("111", result!.DashboardChannelId);
        Assert.Equal("222", result.GithubFeedThreadId);
        Assert.Equal("333", result.DeployNotificationsThreadId);
        Assert.Equal("{\"title\":\"ok\"}", result.SuccessEmbedJson);
        Assert.Equal("{\"title\":\"fail\"}", result.FailureEmbedJson);
        Assert.Equal("{\"title\":\"utd\"}", result.UptodateEmbedJson);
        Assert.Equal("{\"title\":\"dash\"}", result.DashboardEmbedJson);
        Assert.Equal("{\"kind\":\"components\"}", result.SuccessMessageJson);
        Assert.Null(result.FailureMessageJson);
    }

    [Fact]
    public async Task UpsertAsync_Conflict_UpdatesInPlace() {
        if (string.IsNullOrEmpty(ConnString)) return;
        await using var db = await MakeDbAsync();
        var store = new ChannelConfigStore(db);

        await store.UpsertAsync(new ChannelConfig("guild-cc-2", "eggledger", "111", null, null, null, null, null, null), CancellationToken.None);
        await store.UpsertAsync(new ChannelConfig("guild-cc-2", "eggledger", "222", null, null, null, null, null, null), CancellationToken.None);
        var result = await store.GetAsync("guild-cc-2", "eggledger", CancellationToken.None);

        Assert.Equal("222", result!.DashboardChannelId);
    }

    [Fact]
    public async Task UpsertAsync_NullFields_StoreAsNull() {
        if (string.IsNullOrEmpty(ConnString)) return;
        await using var db = await MakeDbAsync();
        var store = new ChannelConfigStore(db);

        await store.UpsertAsync(new ChannelConfig("guild-cc-3", "eggledger", null, null, null, null, null, null, null), CancellationToken.None);
        var result = await store.GetAsync("guild-cc-3", "eggledger", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Null(result!.DashboardChannelId);
        Assert.Null(result.SuccessEmbedJson);
        Assert.Null(result.SuccessMessageJson);
    }
}
