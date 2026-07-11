using Npgsql;
using SyncKit.Db;
using SyncKit.Identity;
using SyncKit.Identity.Models;

namespace SyncKit.Identity.Tests;

// DB-gated: needs a real Postgres connection to exercise transactional insert/conflict behavior
// in-process, matching the established pattern in EggIncognito/EggLedger's own identity tests
// (plain Fact + early return when the env var is unset, no live Postgres in CI).
public class IdentityResolverTests {
    private static string? ConnString => Environment.GetEnvironmentVariable("SYNCKIT_TEST_PG_CONN");

    private static async Task<NpgsqlDataSource> MakeDbAsync() {
        var dataSource = NpgsqlDataSource.Create(ConnString!);
        await using var conn = await dataSource.OpenConnectionAsync();
        await Migrator.MigrateAsync(conn, Path.Combine(AppContext.BaseDirectory, "Migrations"));
        return dataSource;
    }

    private static IdentityResolver MakeResolver(NpgsqlDataSource db, string adminCsv = "") =>
        new(db, AdminAllowlist.FromConfig(adminCsv));

    [Fact]
    public async Task ResolveAsync_NewSub_NoDiscordId_CreatesNewUser() {
        if (string.IsNullOrEmpty(ConnString)) return;
        await using var db = await MakeDbAsync();
        var resolver = MakeResolver(db);

        var result = await resolver.ResolveAsync("authentik", "sub-new-1", null, "alice", null, CancellationToken.None);

        Assert.True(result.IsNew);
        Assert.Equal("viewer", result.Role);
    }

    [Fact]
    public async Task ResolveAsync_ExistingSub_ReturnsSameUserId() {
        if (string.IsNullOrEmpty(ConnString)) return;
        await using var db = await MakeDbAsync();
        var resolver = MakeResolver(db);

        var first = await resolver.ResolveAsync("authentik", "sub-existing-1", null, "bob", null, CancellationToken.None);
        var second = await resolver.ResolveAsync("authentik", "sub-existing-1", null, "bob", null, CancellationToken.None);

        Assert.Equal(first.UserId, second.UserId);
        Assert.False(second.IsNew);
    }

    [Fact]
    public async Task ResolveAsync_MatchingDiscordId_AutoLinksExistingUser() {
        if (string.IsNullOrEmpty(ConnString)) return;
        await using var db = await MakeDbAsync();
        var resolver = MakeResolver(db);

        var discordResult = await resolver.ResolveAsync("discord", "discord-42", null, "carol", null, CancellationToken.None);
        var linkedResult = await resolver.ResolveAsync("authentik", "sub-link-1", "discord-42", "carol", null, CancellationToken.None);

        Assert.Equal(discordResult.UserId, linkedResult.UserId);
    }

    [Fact]
    public async Task ResolveAsync_AdminAllowlist_PromotesOnLogin() {
        if (string.IsNullOrEmpty(ConnString)) return;
        await using var db = await MakeDbAsync();
        var resolver = MakeResolver(db, "999-admin");

        var result = await resolver.ResolveAsync("discord", "999-admin", null, "dave", null, CancellationToken.None);

        Assert.Equal("admin", result.Role);
    }

    [Fact]
    public async Task ResolveAsync_DiscordPath_IsTransactional_ConcurrentFirstLoginsAgree() {
        if (string.IsNullOrEmpty(ConnString)) return;
        await using var db = await MakeDbAsync();
        var resolver = MakeResolver(db);

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => resolver.ResolveAsync("discord", "discord-race-1", null, "racer", null, CancellationToken.None));
        var results = await Task.WhenAll(tasks);

        Assert.Single(results.Select(r => r.UserId).Distinct());
    }

    [Fact]
    public async Task MergeAsync_ReassignsIdentitiesAndDeletesLoser() {
        if (string.IsNullOrEmpty(ConnString)) return;
        await using var db = await MakeDbAsync();
        var resolver = MakeResolver(db);

        var keep = await resolver.ResolveAsync("discord", "keep-1", null, "keeper", null, CancellationToken.None);
        var merge = await resolver.ResolveAsync("authentik", "merge-sub-1", null, "merged", null, CancellationToken.None);

        var winner = await resolver.MergeAsync(keep.UserId, merge.UserId, CancellationToken.None);

        Assert.Equal(keep.UserId, winner);
        var users = new UserQueries(db);
        Assert.Null(await users.GetAsync(merge.UserId, CancellationToken.None));
        var reResolved = await resolver.ResolveAsync("authentik", "merge-sub-1", null, "merged", null, CancellationToken.None);
        Assert.Equal(keep.UserId, reResolved.UserId);
    }
}
