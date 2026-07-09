using Npgsql;
using SyncKit.Db;
using SyncKit.Identity;
using Xunit;

namespace SyncKit.Identity.Tests;

public class ExpiredRowSweeperTests
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
    public async Task SweepAsync_DeletesExpiredOAuthStates_KeepsLive()
    {
        if (string.IsNullOrEmpty(ConnString)) return;
        await using var db = await MakeDbAsync();
        var states = new OAuthStateStore(db, ttl: TimeSpan.FromMilliseconds(1));
        var liveStates = new OAuthStateStore(db, ttl: TimeSpan.FromMinutes(5));

        await states.SaveAsync("expired-state", "v", "https://example.com", CancellationToken.None);
        await liveStates.SaveAsync("live-state", "v", "https://example.com", CancellationToken.None);
        await Task.Delay(50);

        var sweeper = new ExpiredRowSweeper(db, TimeSpan.FromMinutes(10));
        await sweeper.SweepAsync(CancellationToken.None);

        Assert.Null(await liveStates.ConsumeAsync("expired-state", CancellationToken.None));
        Assert.NotNull(await liveStates.ConsumeAsync("live-state", CancellationToken.None));
    }

    [Fact]
    public async Task SweepAsync_DeletesExpiredLoginCodes_KeepsLive()
    {
        if (string.IsNullOrEmpty(ConnString)) return;
        await using var db = await MakeDbAsync();
        var resolver = new IdentityResolver(db, AdminAllowlist.FromConfig(""));
        var userId = (await resolver.ResolveAsync("authentik", Guid.NewGuid().ToString(), null, "sweepuser", null, CancellationToken.None)).UserId;

        var codes = new LoginCodeStore(db, ttl: TimeSpan.FromMilliseconds(1));
        var liveCodes = new LoginCodeStore(db, ttl: TimeSpan.FromMinutes(5));

        var expiredCode = await codes.IssueAsync(userId, false, CancellationToken.None);
        var liveCode = await liveCodes.IssueAsync(userId, false, CancellationToken.None);
        await Task.Delay(50);

        var sweeper = new ExpiredRowSweeper(db, TimeSpan.FromMinutes(10));
        await sweeper.SweepAsync(CancellationToken.None);

        Assert.Null(await liveCodes.RedeemAsync(expiredCode, CancellationToken.None));
        Assert.NotNull(await liveCodes.RedeemAsync(liveCode, CancellationToken.None));
    }
}
