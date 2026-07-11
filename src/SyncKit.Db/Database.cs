using Npgsql;

namespace SyncKit.Db;

// Ports Go db.Init: open and ping a Postgres connection.
public static class Database {
    public static async Task<NpgsqlConnection> InitAsync(string connStr, CancellationToken ct = default) {
        if (string.IsNullOrEmpty(connStr))
            throw new ArgumentException("Database.Init: empty connection string", nameof(connStr));
        var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync(ct);
        await using var ping = new NpgsqlCommand("SELECT 1", conn);
        await ping.ExecuteScalarAsync(ct);
        return conn;
    }
}
