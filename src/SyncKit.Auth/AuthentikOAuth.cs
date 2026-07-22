using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SyncKit.Auth;

// Resolved claims from a completed Authentik OIDC exchange.
public sealed record AuthentikTokenResult(string Sub, string? DiscordId, string? Username, string? Avatar, string? Sid, string? IdToken);

// PKCE authorization-code flow against a self-hosted Authentik instance, scoped to one app's
// own client_id/secret. One instance per consuming app - each app has its own Authentik
// Application registration.
public sealed class AuthentikOAuth(string authority, string clientId, string clientSecret, string callbackUrl) {
    private static readonly HttpClient Http = new();

    public string Authority { get; } = authority.TrimEnd('/');

    public (string Query, string State, string CodeVerifier) BuildAuthParams() {
        var state = DiscordOAuth.RandomHex(16);
        var verifier = GenerateCodeVerifier();
        var challenge = ComputeCodeChallenge(verifier);

        var query = new Dictionary<string, string> {
            ["client_id"] = clientId,
            ["redirect_uri"] = callbackUrl,
            ["response_type"] = "code",
            ["scope"] = "openid+profile+email+discord_id",
            ["state"] = state,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
        };
        var qs = string.Join("&", query.Select(kv =>
            $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value).Replace("%2B", "+")}"));
        return (qs, state, verifier);
    }

    public async Task<AuthentikTokenResult> HandleCallbackAsync(string code, string codeVerifier, CancellationToken ct = default) {
        var tokenResp = await Http.PostAsync($"{Authority}/application/o/token/", new FormUrlEncodedContent(new Dictionary<string, string> {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = callbackUrl,
            ["code_verifier"] = codeVerifier,
        }), ct);
        tokenResp.EnsureSuccessStatusCode();
        using var tokenDoc = JsonDocument.Parse(await tokenResp.Content.ReadAsStringAsync(ct));
        var accessToken = tokenDoc.RootElement.TryGetProperty("access_token", out var atEl) ? atEl.GetString() : null;
        if (string.IsNullOrEmpty(accessToken))
            throw new InvalidOperationException("Authentik token response missing access_token");

        var idToken = tokenDoc.RootElement.TryGetProperty("id_token", out var itEl) ? itEl.GetString() : null;
        var sid = ReadSessionIdFromIdToken(idToken);

        using var userInfoReq = new HttpRequestMessage(HttpMethod.Get, $"{Authority}/application/o/userinfo/");
        userInfoReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        var userInfoResp = await Http.SendAsync(userInfoReq, ct);
        userInfoResp.EnsureSuccessStatusCode();
        using var userInfoDoc = JsonDocument.Parse(await userInfoResp.Content.ReadAsStringAsync(ct));
        var root = userInfoDoc.RootElement;

        var sub = root.TryGetProperty("sub", out var subEl) ? subEl.GetString() : null;
        if (string.IsNullOrEmpty(sub))
            throw new InvalidOperationException("Authentik userinfo response missing sub");

        return new AuthentikTokenResult(
            Sub: sub,
            DiscordId: root.TryGetProperty("discord_id", out var did) ? did.GetString() : null,
            Username: root.TryGetProperty("preferred_username", out var un) ? un.GetString() : null,
            Avatar: root.TryGetProperty("picture", out var av) ? av.GetString() : null,
            Sid: sid,
            IdToken: idToken);
    }

    public static string? ReadSessionIdFromIdToken(string? idToken) {
        if (string.IsNullOrEmpty(idToken)) return null;
        var parts = idToken.Split('.');
        if (parts.Length < 2) return null;
        try {
            var payload = Convert.FromBase64String(PadBase64Url(parts[1]));
            using var doc = JsonDocument.Parse(payload);
            return doc.RootElement.TryGetProperty("sid", out var sidEl) ? sidEl.GetString() : null;
        } catch (Exception) {
            return null;
        }
    }

    private static string PadBase64Url(string value) {
        var s = value.Replace('-', '+').Replace('_', '/');
        return (s.Length % 4) switch { 2 => s + "==", 3 => s + "=", _ => s };
    }

    private static string GenerateCodeVerifier() {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncode(bytes);
    }

    public static string ComputeCodeChallenge(string verifier) {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
