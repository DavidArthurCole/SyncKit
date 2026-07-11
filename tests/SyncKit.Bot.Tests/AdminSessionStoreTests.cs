using Npgsql;
using SyncKit.Bot;
using SyncKit.Db;
using Xunit;

namespace SyncKit.Bot.Tests;

// DB-gated: same pattern as ChannelStateStoreTests.
public class AdminSessionStoreTests {
    private static string? ConnString => Environment.GetEnvironmentVariable("SYNCKIT_TEST_PG_CONN");

    private static async Task<NpgsqlDataSource> MakeDbAsync() {
        var dataSource = NpgsqlDataSource.Create(ConnString!);
        await using var conn = await dataSource.OpenConnectionAsync();
        await Migrator.MigrateAsync(conn, Path.Combine(AppContext.BaseDirectory, "Migrations"));
        return dataSource;
    }

    [Fact]
    public async Task CreateAsync_ThenLookupAsync_RoundTrips() {
        if (string.IsNullOrEmpty(ConnString)) return;
        await using var db = await MakeDbAsync();
        var store = new AdminSessionStore(db);

        await store.CreateAsync("tok-as-1", "discord-1", 9999999999, CancellationToken.None);
        var (found, discordId, expiresAt) = await store.LookupAsync("tok-as-1", CancellationToken.None);

        Assert.True(found);
        Assert.Equal("discord-1", discordId);
        Assert.Equal(9999999999, expiresAt);
    }

    [Fact]
    public async Task LookupAsync_UnknownToken_NotFound() {
        if (string.IsNullOrEmpty(ConnString)) return;
        await using var db = await MakeDbAsync();
        var store = new AdminSessionStore(db);

        var (found, _, _) = await store.LookupAsync("nonexistent-token", CancellationToken.None);

        Assert.False(found);
    }

    [Fact]
    public async Task TouchAsync_UpdatesExpiry() {
        if (string.IsNullOrEmpty(ConnString)) return;
        await using var db = await MakeDbAsync();
        var store = new AdminSessionStore(db);

        await store.CreateAsync("tok-as-2", "discord-2", 1000, CancellationToken.None);
        await store.TouchAsync("tok-as-2", 2000, CancellationToken.None);
        var (_, _, expiresAt) = await store.LookupAsync("tok-as-2", CancellationToken.None);

        Assert.Equal(2000, expiresAt);
    }

    [Fact]
    public async Task DeleteAsync_RemovesSession() {
        if (string.IsNullOrEmpty(ConnString)) return;
        await using var db = await MakeDbAsync();
        var store = new AdminSessionStore(db);

        await store.CreateAsync("tok-as-3", "discord-3", 1000, CancellationToken.None);
        await store.DeleteAsync("tok-as-3", CancellationToken.None);
        var (found, _, _) = await store.LookupAsync("tok-as-3", CancellationToken.None);

        Assert.False(found);
    }
}
