// One-time cutover: merges EggIncognito's and EggLedger's independent users/identities tables
// into SyncKit's identity schema. Dry-run (report only) unless --commit is passed.
//
// Env:
//   EGI_SOURCE_DB_CONNECTION      EggIncognito's Postgres connection string (read-only use)
//   LEDGER_SOURCE_DB_CONNECTION   EggLedger's Postgres connection string (read-only use)
//   IDENTITY_DB_CONNECTION        target SyncKit identity database connection string
using Npgsql;
using SyncKit.Db;
using SyncKit.Identity.Tools;

var commit = args.Contains("--commit");

var egiConn = RequireEnv("EGI_SOURCE_DB_CONNECTION");
var ledgerConn = RequireEnv("LEDGER_SOURCE_DB_CONNECTION");
var targetConn = RequireEnv("IDENTITY_DB_CONNECTION");
if (egiConn is null || ledgerConn is null || targetConn is null) return 1;

Console.WriteLine("cutover: reading source databases (read-only)...");
var egi = await SourceReader.ReadEggIncognitoAsync(egiConn, CancellationToken.None);
var ledger = await SourceReader.ReadEggLedgerAsync(ledgerConn, CancellationToken.None);
Console.WriteLine($"cutover: eggincognito users={egi.Users.Count} identities={egi.Identities.Count}");
Console.WriteLine($"cutover: eggledger    users={ledger.Users.Count} identities={ledger.Identities.Count}");

var merge = CutoverMerger.Merge(egi, ledger);

Console.WriteLine($"cutover: {merge.Remaps.Count} discord_id collisions found (same person, both apps):");
foreach (var r in merge.Remaps)
    Console.WriteLine($"  discord_id={r.DiscordId} keep={r.KeptUserId} ({r.SourceOfKept}) retire={r.RetiredUserId}");

Console.WriteLine($"cutover: merged result -> {merge.Users.Count} users, {merge.Identities.Count} identities");

if (merge.Orphans.Count > 0) {
    Console.WriteLine($"cutover: {merge.Orphans.Count} orphaned identity row(s) skipped (user_id has no matching users row, pre-existing source data issue):");
    foreach (var o in merge.Orphans)
        Console.WriteLine($"  source={o.Source} user_id={o.UserId} provider={o.Provider} subject={o.Subject}");
}

if (!commit) {
    Console.WriteLine("cutover: dry run only, no writes made. Re-run with --commit to apply.");
    return 0;
}

Console.WriteLine("cutover: applying migrations to target..."); {
    await using var targetDb = NpgsqlDataSource.Create(targetConn);
    await using var conn = await targetDb.OpenConnectionAsync();
    await Migrator.MigrateAsync(conn, Path.Combine(AppContext.BaseDirectory, "Migrations"));
}

Console.WriteLine("cutover: writing merged users/identities to target...");
await CutoverWriter.WriteAsync(targetConn, merge, CancellationToken.None);
Console.WriteLine("cutover: done. Source databases were not modified.");
return 0;

static string? RequireEnv(string name) {
    var value = Environment.GetEnvironmentVariable(name);
    if (string.IsNullOrEmpty(value)) {
        Console.Error.WriteLine($"cutover: {name} is required");
        return null;
    }
    return value;
}
