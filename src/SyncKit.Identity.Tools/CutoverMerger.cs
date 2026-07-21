using SyncKit.Contract;
using SyncKit.Identity.Models;

namespace SyncKit.Identity.Tools;

public sealed record RemapEntry(string DiscordId, Guid KeptUserId, Guid RetiredUserId, string SourceOfKept);

public sealed record OrphanIdentity(Guid UserId, string Provider, string Subject, string Source);

public sealed record MergeResult(
    IReadOnlyList<User> Users,
    IReadOnlyList<Models.Identity> Identities,
    IReadOnlyList<RemapEntry> Remaps,
    IReadOnlyList<OrphanIdentity> Orphans);

// Pure merge decision, no I/O: EggIncognito's users win discord_id collisions.
// EggLedger's identities for that discord_id get remapped onto the surviving user_id; non-colliding users pass through untouched.
public static class CutoverMerger {
    public static MergeResult Merge(SourceSnapshot egi, SourceSnapshot ledger) {
        var egiByDiscordId = egi.Users.Where(u => u.DiscordId is not null).ToDictionary(u => u.DiscordId!);
        var remaps = new List<RemapEntry>();
        var idRemap = new Dictionary<Guid, Guid>(); // ledger user_id -> surviving user_id

        var users = new List<User>();
        foreach (var u in egi.Users) users.Add(ToUser(u));

        foreach (var u in ledger.Users) {
            if (u.DiscordId is not null && egiByDiscordId.TryGetValue(u.DiscordId, out var kept)) {
                idRemap[u.UserId] = kept.UserId;
                remaps.Add(new RemapEntry(u.DiscordId, kept.UserId, u.UserId, "eggincognito"));
                continue;
            }
            users.Add(ToUser(u));
        }

        // A user_id an identity row references but that has no matching users row is pre-existing
        // source-data corruption (confirmed: EggIncognito's identities table has no FK, so nothing
        // ever enforced this) - skip it and report it rather than fail the whole cutover.
        var knownUserIds = users.Select(u => u.UserId).ToHashSet();
        var orphans = new List<OrphanIdentity>();

        var identities = new List<Models.Identity>();
        foreach (var i in egi.Identities) {
            if (!knownUserIds.Contains(i.UserId)) { orphans.Add(new(i.UserId, i.Provider, i.Subject, "eggincognito")); continue; }
            identities.Add(ToIdentity(i, i.UserId));
        }
        foreach (var i in ledger.Identities) {
            var effectiveUserId = idRemap.GetValueOrDefault(i.UserId, i.UserId);
            if (!knownUserIds.Contains(effectiveUserId)) { orphans.Add(new(i.UserId, i.Provider, i.Subject, "eggledger")); continue; }
            identities.Add(ToIdentity(i, effectiveUserId));
        }

        return new MergeResult(users, Dedupe(identities), remaps, orphans);
    }

    // A remapped ledger user may already have an authentik/discord identity row EggIncognito's
    // side also created independently (both apps' discord logins insert (discord, discordId)) -
    // keep the earliest-linked row per (provider, subject), drop the duplicate.
    private static List<Models.Identity> Dedupe(List<Models.Identity> identities) =>
        [.. identities
            .GroupBy(i => (i.Provider, i.Subject))
            .Select(g => g.OrderBy(i => i.LinkedAt).First())];

    private static User ToUser(SourceUser u) => new() {
        UserId = u.UserId,
        DiscordId = u.DiscordId,
        Username = u.Username,
        Avatar = u.Avatar,
        Role = u.Role ?? UserRoles.ToName(UserRole.Viewer),
        CreatedAt = u.CreatedAt,
        LastLoginAt = u.LastLoginAt ?? u.CreatedAt,
    };

    private static Models.Identity ToIdentity(SourceIdentity i, Guid effectiveUserId) => new() {
        UserId = effectiveUserId,
        Provider = i.Provider,
        Subject = i.Subject,
        LinkedAt = i.LinkedAt,
    };
}
