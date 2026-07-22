using System.Security.Cryptography;
using Npgsql;

namespace SyncKit.Identity;

public sealed record RedeemedLogin(Guid UserId, bool IsNew);

public sealed class LoginCodeStore(NpgsqlDataSource dataSource, TimeSpan? ttl = null) {
    private readonly TimeSpan _ttl = ttl ?? TimeSpan.FromSeconds(60);

    public async Task<string> IssueAsync(Guid userId, bool isNew, CancellationToken ct) {
        var code = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO login_codes (code, user_id, is_new, expires_at) VALUES ($1, $2, $3, $4)", conn);
        cmd.Parameters.AddWithValue(code);
        cmd.Parameters.AddWithValue(userId);
        cmd.Parameters.AddWithValue(isNew);
        cmd.Parameters.AddWithValue(DateTimeOffset.UtcNow.Add(_ttl));
        await cmd.ExecuteNonQueryAsync(ct);
        return code;
    }

    public async Task<RedeemedLogin?> RedeemAsync(string code, CancellationToken ct) {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE login_codes SET redeemed_at = now()
            WHERE code = $1 AND redeemed_at IS NULL AND expires_at > now()
            RETURNING user_id, is_new
            """, conn);
        cmd.Parameters.AddWithValue(code);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new RedeemedLogin(reader.GetGuid(0), reader.GetBoolean(1));
    }
}
