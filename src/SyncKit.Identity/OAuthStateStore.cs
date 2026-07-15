using Npgsql;

namespace SyncKit.Identity;

public sealed record OAuthState(string CodeVerifier, string ReturnUrl, string Mode);

// Server-side state for the /login/go -> /login/callback round trip. Single-use: consumed
// (deleted) on lookup so a state value can't be replayed against /login/callback twice.
public sealed class OAuthStateStore(NpgsqlDataSource dataSource, TimeSpan? ttl = null) {
    private readonly TimeSpan _ttl = ttl ?? TimeSpan.FromMinutes(5);

    public async Task SaveAsync(string state, string codeVerifier, string returnUrl, string mode, CancellationToken ct) {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO oauth_states (state, code_verifier, return_url, mode, expires_at) VALUES ($1, $2, $3, $4, $5)", conn);
        cmd.Parameters.AddWithValue(state);
        cmd.Parameters.AddWithValue(codeVerifier);
        cmd.Parameters.AddWithValue(returnUrl);
        cmd.Parameters.AddWithValue(mode);
        cmd.Parameters.AddWithValue(DateTimeOffset.UtcNow.Add(_ttl));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<OAuthState?> ConsumeAsync(string state, CancellationToken ct) {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM oauth_states WHERE state = $1 AND expires_at > now() RETURNING code_verifier, return_url, mode", conn);
        cmd.Parameters.AddWithValue(state);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new OAuthState(reader.GetString(0), reader.GetString(1), reader.GetString(2));
    }
}
