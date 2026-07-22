using Npgsql;
using SyncKit.Db;
using SyncKit.Identity;

namespace SyncKit.Identity.Tests;

public class UserQueriesTests {
    private static string? ConnString => Environment.GetEnvironmentVariable("SYNCKIT_TEST_PG_CONN");

    private static async Task<NpgsqlDataSource> MakeDbAsync() {
        var dataSource = NpgsqlDataSource.Create(ConnString!);
        await using var conn = await dataSource.OpenConnectionAsync();
        await Migrator.MigrateAsync(conn, Path.Combine(AppContext.BaseDirectory, "Migrations"));
        return dataSource;
    }

    [Fact]
    public async Task GetAsync_NewUser_AvatarIsCustomDefaultsFalse() {
        if (string.IsNullOrEmpty(ConnString)) return;
        await using var db = await MakeDbAsync();
        var resolver = new IdentityResolver(db, AdminAllowlist.FromConfig(""));
        var users = new UserQueries(db);

        var resolved = await resolver.ResolveAsync("authentik", "uq-sub-1", null, "quinn", null, CancellationToken.None);
        var user = await users.GetAsync(resolved.UserId, CancellationToken.None);

        Assert.NotNull(user);
        Assert.False(user!.AvatarIsCustom);
    }
}
