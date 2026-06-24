using Npgsql;

namespace SyncKit.Db;

// Ports Go db.MigrationFiles / prefixNum / Migrate. Tracking table is synckit_migrations
// (version INTEGER PRIMARY KEY); applies *.up.sql files whose integer prefix exceeds the max
// recorded version, in numeric (not lexical) order.
public static class Migrator
{
    public static int PrefixNum(string path)
    {
        var baseName = Path.GetFileName(path);
        var idx = baseName.IndexOf('_');
        if (idx <= 0) return 0;
        return int.TryParse(baseName[..idx], out var n) ? n : 0;
    }

    public static IReadOnlyList<string> MigrationFiles(string dir)
    {
        return Directory.EnumerateFiles(dir)
            .Where(f => f.EndsWith(".up.sql", StringComparison.Ordinal))
            .OrderBy(PrefixNum)
            .ToList();
    }

    public static async Task MigrateAsync(NpgsqlConnection conn, string dir, CancellationToken ct = default)
    {
        await using (var create = new NpgsqlCommand(
            "CREATE TABLE IF NOT EXISTS synckit_migrations (version INTEGER PRIMARY KEY)", conn))
            await create.ExecuteNonQueryAsync(ct);

        int current;
        await using (var q = new NpgsqlCommand(
            "SELECT COALESCE(MAX(version), 0) FROM synckit_migrations", conn))
            current = Convert.ToInt32(await q.ExecuteScalarAsync(ct));

        foreach (var f in MigrationFiles(dir))
        {
            var v = PrefixNum(f);
            if (v <= current) continue;
            var sql = await File.ReadAllTextAsync(f, ct);
            await using (var exec = new NpgsqlCommand(sql, conn))
                await exec.ExecuteNonQueryAsync(ct);
            await using (var rec = new NpgsqlCommand(
                "INSERT INTO synckit_migrations (version) VALUES ($1)", conn))
            {
                rec.Parameters.Add(new NpgsqlParameter { Value = v });
                await rec.ExecuteNonQueryAsync(ct);
            }
        }
    }
}
