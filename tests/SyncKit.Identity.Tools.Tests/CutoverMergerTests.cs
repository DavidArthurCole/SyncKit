using SyncKit.Identity.Tools;
using Xunit;

namespace SyncKit.Identity.Tools.Tests;

public class CutoverMergerTests {
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-06-10T00:00:00Z");
    private static readonly DateTimeOffset T1 = DateTimeOffset.Parse("2026-06-11T00:00:00Z");

    [Fact]
    public void Merge_NoOverlap_PassesBothSidesThrough() {
        var egi = new SourceSnapshot(
            [new SourceUser(Guid.NewGuid(), "111", "alice", null, "admin", T0, T0)],
            []);
        var ledger = new SourceSnapshot(
            [new SourceUser(Guid.NewGuid(), "222", "bob", null, null, T0, null)],
            []);

        var result = CutoverMerger.Merge(egi, ledger);

        Assert.Equal(2, result.Users.Count);
        Assert.Empty(result.Remaps);
        Assert.Contains(result.Users, u => u.Username == "alice" && u.Role == "admin");
        Assert.Contains(result.Users, u => u.Username == "bob" && u.Role == "viewer");
    }

    [Fact]
    public void Merge_OverlappingDiscordId_KeepsEggIncognitoUserId() {
        var egiUserId = Guid.NewGuid();
        var ledgerUserId = Guid.NewGuid();
        var egi = new SourceSnapshot(
            [new SourceUser(egiUserId, "999", "dave", null, "viewer", T0, T0)], []);
        var ledger = new SourceSnapshot(
            [new SourceUser(ledgerUserId, "999", "dave", null, null, T1, null)], []);

        var result = CutoverMerger.Merge(egi, ledger);

        Assert.Single(result.Users);
        Assert.Equal(egiUserId, result.Users[0].UserId);
        Assert.Single(result.Remaps);
        Assert.Equal(egiUserId, result.Remaps[0].KeptUserId);
        Assert.Equal(ledgerUserId, result.Remaps[0].RetiredUserId);
        Assert.Equal("999", result.Remaps[0].DiscordId);
    }

    [Fact]
    public void Merge_OverlappingDiscordId_RemapsLedgerIdentitiesOntoKeptUserId() {
        var egiUserId = Guid.NewGuid();
        var ledgerUserId = Guid.NewGuid();
        var egi = new SourceSnapshot(
            [new SourceUser(egiUserId, "999", "dave", null, "viewer", T0, T0)],
            [new SourceIdentity(egiUserId, "authentik", "sub-egi", T0)]);
        var ledger = new SourceSnapshot(
            [new SourceUser(ledgerUserId, "999", "dave", null, null, T1, null)],
            [new SourceIdentity(ledgerUserId, "authentik", "sub-ledger", T1)]);

        var result = CutoverMerger.Merge(egi, ledger);

        Assert.Equal(2, result.Identities.Count);
        Assert.All(result.Identities, i => Assert.Equal(egiUserId, i.UserId));
        Assert.Contains(result.Identities, i => i.Subject == "sub-egi");
        Assert.Contains(result.Identities, i => i.Subject == "sub-ledger");
    }

    [Fact]
    public void Merge_DuplicateIdentityAcrossSides_KeepsEarliestLinked() {
        var egiUserId = Guid.NewGuid();
        var ledgerUserId = Guid.NewGuid();
        var egi = new SourceSnapshot(
            [new SourceUser(egiUserId, "999", "dave", null, "viewer", T0, T0)],
            [new SourceIdentity(egiUserId, "discord", "999", T1)]);
        var ledger = new SourceSnapshot(
            [new SourceUser(ledgerUserId, "999", "dave", null, null, T0, null)],
            [new SourceIdentity(ledgerUserId, "discord", "999", T0)]);

        var result = CutoverMerger.Merge(egi, ledger);

        var discordIdentities = result.Identities.Where(i => i.Provider == "discord" && i.Subject == "999").ToList();
        Assert.Single(discordIdentities);
        Assert.Equal(T0, discordIdentities[0].LinkedAt);
    }

    [Fact]
    public void Merge_LedgerUserWithNullRole_DefaultsToViewer() {
        var egi = new SourceSnapshot([], []);
        var ledger = new SourceSnapshot(
            [new SourceUser(Guid.NewGuid(), "555", "erin", null, null, T0, null)], []);

        var result = CutoverMerger.Merge(egi, ledger);

        Assert.Equal("viewer", result.Users[0].Role);
    }

    [Fact]
    public void Merge_LedgerUserWithNullLastLogin_FallsBackToCreatedAt() {
        var egi = new SourceSnapshot([], []);
        var ledger = new SourceSnapshot(
            [new SourceUser(Guid.NewGuid(), "555", "erin", null, null, T0, null)], []);

        var result = CutoverMerger.Merge(egi, ledger);

        Assert.Equal(T0, result.Users[0].LastLoginAt);
    }

    [Fact]
    public void Merge_IdentityReferencingUnknownUserId_SkippedAsOrphanNotThrown() {
        var orphanUserId = Guid.NewGuid();
        var egi = new SourceSnapshot(
            [],
            [new SourceIdentity(orphanUserId, "authentik", "orphan-sub", T0)]);
        var ledger = new SourceSnapshot([], []);

        var result = CutoverMerger.Merge(egi, ledger);

        Assert.Empty(result.Identities);
        Assert.Single(result.Orphans);
        Assert.Equal(orphanUserId, result.Orphans[0].UserId);
        Assert.Equal("eggincognito", result.Orphans[0].Source);
    }

    [Fact]
    public void Merge_UsersWithoutDiscordId_NeverCollide() {
        var egi = new SourceSnapshot(
            [new SourceUser(Guid.NewGuid(), null, "authentik-only-1", null, "viewer", T0, T0)], []);
        var ledger = new SourceSnapshot(
            [new SourceUser(Guid.NewGuid(), null, "authentik-only-2", null, null, T0, null)], []);

        var result = CutoverMerger.Merge(egi, ledger);

        Assert.Equal(2, result.Users.Count);
        Assert.Empty(result.Remaps);
    }
}
