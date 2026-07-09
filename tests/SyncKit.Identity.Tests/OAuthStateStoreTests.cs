using Npgsql;
using SyncKit.Db;
using SyncKit.Identity;
using Xunit;

namespace SyncKit.Identity.Tests;

public class OAuthStateStoreTests
{
    private static string? ConnString => Environment.GetEnvironmentVariable("SYNCKIT_TEST_PG_CONN");

    private static async Task<NpgsqlDataSource> MakeDbAsync()
    {
        var dataSource = NpgsqlDataSource.Create(ConnString!);
        await using var conn = await dataSource.OpenConnectionAsync();
        await Migrator.MigrateAsync(conn, Path.Combine(AppContext.BaseDirectory, "Migrations"));
        return dataSource;
    }

    [Fact]
    public async Task SaveAsync_ThenConsumeAsync_ReturnsState()
    {
        if (string.IsNullOrEmpty(ConnString)) return;
        await using var db = await MakeDbAsync();
        var store = new OAuthStateStore(db);

        await store.SaveAsync("state-1", "verifier-1", "https://eggledger.example.com", CancellationToken.None);
        var result = await store.ConsumeAsync("state-1", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("verifier-1", result!.CodeVerifier);
        Assert.Equal("https://eggledger.example.com", result.ReturnOrigin);
    }

    [Fact]
    public async Task ConsumeAsync_SecondAttempt_ReturnsNull()
    {
        if (string.IsNullOrEmpty(ConnString)) return;
        await using var db = await MakeDbAsync();
        var store = new OAuthStateStore(db);

        await store.SaveAsync("state-2", "verifier-2", "https://eggincognito.example.com", CancellationToken.None);
        await store.ConsumeAsync("state-2", CancellationToken.None);
        var second = await store.ConsumeAsync("state-2", CancellationToken.None);

        Assert.Null(second);
    }

    [Fact]
    public async Task ConsumeAsync_UnknownState_ReturnsNull()
    {
        if (string.IsNullOrEmpty(ConnString)) return;
        await using var db = await MakeDbAsync();
        var store = new OAuthStateStore(db);

        var result = await store.ConsumeAsync("never-saved", CancellationToken.None);

        Assert.Null(result);
    }
}
