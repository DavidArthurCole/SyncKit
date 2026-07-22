using Npgsql;
using SyncKit.Db;
using SyncKit.Identity;

namespace SyncKit.Identity.Tests;

public class ProfileServiceTests {
    private static string? ConnString => Environment.GetEnvironmentVariable("SYNCKIT_TEST_PG_CONN");

    private static async Task<NpgsqlDataSource> MakeDbAsync() {
        var dataSource = NpgsqlDataSource.Create(ConnString!);
        await using var conn = await dataSource.OpenConnectionAsync();
        await Migrator.MigrateAsync(conn, Path.Combine(AppContext.BaseDirectory, "Migrations"));
        return dataSource;
    }

    [Fact]
    public async Task ListIdentitiesAsync_ReturnsAllLinkedIdentities() {
        if (string.IsNullOrEmpty(ConnString)) return;
        await using var db = await MakeDbAsync();
        var resolver = new IdentityResolver(db, AdminAllowlist.FromConfig(""));
        var svc = new ProfileService(db);

        var owner = await resolver.ResolveAsync("discord", "ps-owner-1", null, "psowner", null, CancellationToken.None);
        await resolver.TryLinkAsync(owner.UserId, "authentik", "ps-sub-1", null, "psowner", null, CancellationToken.None);

        var identities = await svc.ListIdentitiesAsync(owner.UserId, CancellationToken.None);

        Assert.Equal(2, identities.Count);
    }

    [Fact]
    public async Task UnlinkAsync_LastIdentity_ReturnsLastIdentityAndDoesNotDelete() {
        if (string.IsNullOrEmpty(ConnString)) return;
        await using var db = await MakeDbAsync();
        var resolver = new IdentityResolver(db, AdminAllowlist.FromConfig(""));
        var svc = new ProfileService(db);

        var owner = await resolver.ResolveAsync("discord", "ps-owner-2", null, "psowner2", null, CancellationToken.None);

        var result = await svc.UnlinkAsync(owner.UserId, "discord", "ps-owner-2", CancellationToken.None);

        Assert.Equal(UnlinkResult.LastIdentity, result);
        var identities = await svc.ListIdentitiesAsync(owner.UserId, CancellationToken.None);
        Assert.Single(identities);
    }

    [Fact]
    public async Task UnlinkAsync_NotLastIdentity_Removes() {
        if (string.IsNullOrEmpty(ConnString)) return;
        await using var db = await MakeDbAsync();
        var resolver = new IdentityResolver(db, AdminAllowlist.FromConfig(""));
        var svc = new ProfileService(db);

        var owner = await resolver.ResolveAsync("discord", "ps-owner-3", null, "psowner3", null, CancellationToken.None);
        await resolver.TryLinkAsync(owner.UserId, "authentik", "ps-sub-3", null, "psowner3", null, CancellationToken.None);

        var result = await svc.UnlinkAsync(owner.UserId, "authentik", "ps-sub-3", CancellationToken.None);

        Assert.Equal(UnlinkResult.Unlinked, result);
        var identities = await svc.ListIdentitiesAsync(owner.UserId, CancellationToken.None);
        Assert.Single(identities);
    }

    [Fact]
    public async Task SelectIdentityAvatarAsync_CopiesSnapshotAndClearsCustomFlag() {
        if (string.IsNullOrEmpty(ConnString)) return;
        await using var db = await MakeDbAsync();
        var resolver = new IdentityResolver(db, AdminAllowlist.FromConfig(""));
        var svc = new ProfileService(db);

        var owner = await resolver.ResolveAsync("authentik", "ps-sub-4", null, "psowner4", "https://cdn/avatar4.png", CancellationToken.None);
        await svc.SetCustomAvatarAsync(owner.UserId, "/avatars/custom.png", CancellationToken.None);

        var selected = await svc.SelectIdentityAvatarAsync(owner.UserId, "authentik", "ps-sub-4", CancellationToken.None);

        Assert.True(selected);
        var users = new UserQueries(db);
        var user = await users.GetAsync(owner.UserId, CancellationToken.None);
        Assert.Equal("https://cdn/avatar4.png", user!.Avatar);
        Assert.False(user.AvatarIsCustom);
    }
}
