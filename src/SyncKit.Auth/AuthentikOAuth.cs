using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SyncKit.Auth;

// Resolved claims from a completed Authentik OIDC exchange.
public sealed record AuthentikTokenResult(string Sub, string? DiscordId, string? Username, string? Avatar);

// PKCE authorization-code flow against a self-hosted Authentik instance. Hand-rolled to match
// DiscordOAuth.cs's shape exactly (this repo has no ASP.NET OIDC-handler/IConfiguration
// convention anywhere) rather than pulling in Microsoft.AspNetCore.Authentication.OpenIdConnect.
public static class AuthentikOAuth {
    private static string _authority = "";
    private static string _clientId = "";
    private static string _clientSecret = "";
    private static string _redirectUrl = "";
    private static readonly HttpClient Http = new();

    public static void Init(string authority, string clientId, string clientSecret, string redirectUrl) {
        _authority = authority.TrimEnd('/');
        _clientId = clientId;
        _clientSecret = clientSecret;
        _redirectUrl = redirectUrl;
    }

    public static (string Query, string State, string CodeVerifier) BuildAuthParams() {
        var state = DiscordOAuth.RandomHex(16);
        var verifier = GenerateCodeVerifier();
        var challenge = ComputeCodeChallenge(verifier);

        var query = new Dictionary<string, string> {
            ["client_id"] = _clientId,
            ["redirect_uri"] = _redirectUrl,
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

    public static (string Url, string State, string CodeVerifier) AuthUrl() {
        var (qs, state, verifier) = BuildAuthParams();
        // Authentik picks the flow to run from the provider's own Authentication Flow setting,
        // not a hardcoded slug here - keeps this in sync with whatever flow the provider is set to.
        return ($"{_authority}/application/o/authorize/?{qs}", state, verifier);
    }

    public static async Task<AuthentikTokenResult> HandleCallbackAsync(string code, string codeVerifier, CancellationToken ct = default) {
        var tokenResp = await Http.PostAsync($"{_authority}/application/o/token/", new FormUrlEncodedContent(new Dictionary<string, string> {
            ["client_id"] = _clientId,
            ["client_secret"] = _clientSecret,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = _redirectUrl,
            ["code_verifier"] = codeVerifier,
        }), ct);
        tokenResp.EnsureSuccessStatusCode();
        using var tokenDoc = JsonDocument.Parse(await tokenResp.Content.ReadAsStringAsync(ct));
        var accessToken = tokenDoc.RootElement.TryGetProperty("access_token", out var atEl) ? atEl.GetString() : null;
        if (string.IsNullOrEmpty(accessToken))
            throw new InvalidOperationException("Authentik token response missing access_token");

        using var userInfoReq = new HttpRequestMessage(HttpMethod.Get, $"{_authority}/application/o/userinfo/");
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
            Avatar: root.TryGetProperty("picture", out var av) ? av.GetString() : null);
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
