using Npgsql;
using SyncKit.Bot;
using SyncKit.Db;
using Xunit;

namespace SyncKit.Bot.Tests;

// DB-gated: same pattern as SyncKit.Identity.Tests (plain Fact + early return when unset).
public class ChannelStateStoreTests {
    private static string? ConnString => Environment.GetEnvironmentVariable("SYNCKIT_TEST_PG_CONN");

    private static async Task<NpgsqlDataSource> MakeDbAsync() {
        var dataSource = NpgsqlDataSource.Create(ConnString!);
        await using var conn = await dataSource.OpenConnectionAsync();
        await Migrator.MigrateAsync(conn, Path.Combine(AppContext.BaseDirectory, "Migrations"));
        return dataSource;
    }

    [Fact]
    public async Task UpsertAsync_ThenGetAsync_RoundTrips() {
        if (string.IsNullOrEmpty(ConnString)) return;
        await using var db = await MakeDbAsync();
        var store = new ChannelStateStore(db);

        await store.UpsertAsync("guild-1", "eggledger", "dashboard", "msg-1", null, CancellationToken.None);
        var result = await store.GetAsync("guild-1", "eggledger", "dashboard", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("msg-1", result!.DiscordId);
        Assert.Null(result.WebhookToken);
    }

    [Fact]
    public async Task UpsertAsync_Conflict_UpdatesInPlace() {
        if (string.IsNullOrEmpty(ConnString)) return;
        await using var db = await MakeDbAsync();
        var store = new ChannelStateStore(db);

        await store.UpsertAsync("guild-2", "eggledger", "thread:GithubFeed:webhook", "wh-1", "tok-1", CancellationToken.None);
        await store.UpsertAsync("guild-2", "eggledger", "thread:GithubFeed:webhook", "wh-1", "tok-2", CancellationToken.None);
        var result = await store.GetAsync("guild-2", "eggledger", "thread:GithubFeed:webhook", CancellationToken.None);

        Assert.Equal("tok-2", result!.WebhookToken);
    }

    [Fact]
    public async Task DeleteAsync_RemovesRow() {
        if (string.IsNullOrEmpty(ConnString)) return;
        await using var db = await MakeDbAsync();
        var store = new ChannelStateStore(db);

        await store.UpsertAsync("guild-3", "eggledger", "thread:DeployNotifications", "th-1", null, CancellationToken.None);
        await store.DeleteAsync("guild-3", "eggledger", "thread:DeployNotifications", CancellationToken.None);
        var result = await store.GetAsync("guild-3", "eggledger", "thread:DeployNotifications", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ListAsync_ScopedToGuildAndApp() {
        if (string.IsNullOrEmpty(ConnString)) return;
        await using var db = await MakeDbAsync();
        var store = new ChannelStateStore(db);

        await store.UpsertAsync("guild-4", "eggledger", "dashboard", "msg-a", null, CancellationToken.None);
        await store.UpsertAsync("guild-4", "eggledger", "thread:GithubFeed", "th-a", null, CancellationToken.None);
        await store.UpsertAsync("guild-4", "eggincognito", "dashboard", "msg-b", null, CancellationToken.None);

        var results = await store.ListAsync("guild-4", "eggledger", CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.DoesNotContain(results, r => r.AppName == "eggincognito");
    }
}
