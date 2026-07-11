using Npgsql;
using SyncKit.Db;
using SyncKit.Identity;
using Xunit;

namespace SyncKit.Identity.Tests;

public class LoginCodeStoreTests {
    private static string? ConnString => Environment.GetEnvironmentVariable("SYNCKIT_TEST_PG_CONN");

    private static async Task<NpgsqlDataSource> MakeDbAsync() {
        var dataSource = NpgsqlDataSource.Create(ConnString!);
        await using var conn = await dataSource.OpenConnectionAsync();
        await Migrator.MigrateAsync(conn, Path.Combine(AppContext.BaseDirectory, "Migrations"));
        return dataSource;
    }

    private static async Task<Guid> SeedUserAsync(NpgsqlDataSource db) {
        var resolver = new IdentityResolver(db, AdminAllowlist.FromConfig(""));
        var result = await resolver.ResolveAsync("authentik", Guid.NewGuid().ToString(), null, "codeuser", null, CancellationToken.None);
        return result.UserId;
    }

    [Fact]
    public async Task IssueAsync_ThenRedeemAsync_ReturnsUserId() {
        if (string.IsNullOrEmpty(ConnString)) return;
        await using var db = await MakeDbAsync();
        var userId = await SeedUserAsync(db);
        var store = new LoginCodeStore(db);

        var code = await store.IssueAsync(userId, isNew: false, CancellationToken.None);
        var redeemed = await store.RedeemAsync(code, CancellationToken.None);

        Assert.Equal(userId, redeemed?.UserId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IssueAsync_ThenRedeemAsync_PreservesIsNew(bool isNew) {
        if (string.IsNullOrEmpty(ConnString)) return;
        await using var db = await MakeDbAsync();
        var userId = await SeedUserAsync(db);
        var store = new LoginCodeStore(db);

        var code = await store.IssueAsync(userId, isNew, CancellationToken.None);
        var redeemed = await store.RedeemAsync(code, CancellationToken.None);

        Assert.Equal(isNew, redeemed?.IsNew);
    }

    [Fact]
    public async Task RedeemAsync_SecondAttempt_ReturnsNull() {
        if (string.IsNullOrEmpty(ConnString)) return;
        await using var db = await MakeDbAsync();
        var userId = await SeedUserAsync(db);
        var store = new LoginCodeStore(db);

        var code = await store.IssueAsync(userId, isNew: false, CancellationToken.None);
        await store.RedeemAsync(code, CancellationToken.None);
        var second = await store.RedeemAsync(code, CancellationToken.None);

        Assert.Null(second);
    }

    [Fact]
    public async Task RedeemAsync_UnknownCode_ReturnsNull() {
        if (string.IsNullOrEmpty(ConnString)) return;
        await using var db = await MakeDbAsync();
        var store = new LoginCodeStore(db);

        var result = await store.RedeemAsync("does-not-exist", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task RedeemAsync_ExpiredCode_ReturnsNull() {
        if (string.IsNullOrEmpty(ConnString)) return;
        await using var db = await MakeDbAsync();
        var userId = await SeedUserAsync(db);
        var store = new LoginCodeStore(db, ttl: TimeSpan.FromMilliseconds(1));

        var code = await store.IssueAsync(userId, isNew: false, CancellationToken.None);
        await Task.Delay(50);
        var result = await store.RedeemAsync(code, CancellationToken.None);

        Assert.Null(result);
    }
}
