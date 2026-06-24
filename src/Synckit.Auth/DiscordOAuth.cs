using System.Security.Cryptography;
using System.Text.Json;

namespace SyncKit.Auth;

// Captured Discord user fields from /users/@me.
public sealed record DiscordUser(string Id, string Username, string AvatarUrl);

// Ports Go auth/discord.go. Discord OAuth2 authorization-code flow, identify scope.
public static class DiscordOAuth
{
    private const string AuthorizeUrl = "https://discord.com/api/oauth2/authorize";
    private const string TokenUrl = "https://discord.com/api/oauth2/token";
    private const string MeUrl = "https://discord.com/api/users/@me";

    private static string _clientId = "";
    private static string _clientSecret = "";
    private static string _redirectUrl = "";
    private static readonly HttpClient Http = new();

    public static void Init(string clientId, string clientSecret, string redirectUrl)
    {
        _clientId = clientId;
        _clientSecret = clientSecret;
        _redirectUrl = redirectUrl;
    }

    public static (string Url, string State) AuthUrl()
    {
        var state = RandomHex(16);
        var query = new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["redirect_uri"] = _redirectUrl,
            ["response_type"] = "code",
            ["scope"] = "identify",
            ["state"] = state,
        };
        var qs = string.Join("&", query.Select(kv =>
            $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        return ($"{AuthorizeUrl}?{qs}", state);
    }

    // Exchanges code for a token, fetches /users/@me, calls storePending with state, a new
    // 64-char session token, and the Discord user.
    public static async Task HandleCallbackAsync(
        string code, string state,
        Func<string, string, DiscordUser, Task> storePending,
        CancellationToken ct = default)
    {
        var tokenResp = await Http.PostAsync(TokenUrl, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["client_secret"] = _clientSecret,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = _redirectUrl,
        }), ct);
        tokenResp.EnsureSuccessStatusCode();
        using var tokenDoc = JsonDocument.Parse(await tokenResp.Content.ReadAsStringAsync(ct));
        var accessToken = tokenDoc.RootElement.TryGetProperty("access_token", out var atEl)
            ? atEl.GetString()
            : null;
        if (string.IsNullOrEmpty(accessToken))
            throw new InvalidOperationException("Discord token response missing access_token");

        using var meReq = new HttpRequestMessage(HttpMethod.Get, MeUrl);
        meReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        var meResp = await Http.SendAsync(meReq, ct);
        meResp.EnsureSuccessStatusCode();
        using var meDoc = JsonDocument.Parse(await meResp.Content.ReadAsStringAsync(ct));
        var root = meDoc.RootElement;
        var id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        if (string.IsNullOrEmpty(id))
            throw new InvalidOperationException("Discord user response missing id");
        var username = root.TryGetProperty("username", out var u) ? u.GetString() ?? "" : "";
        var avatar = root.TryGetProperty("avatar", out var a) ? a.GetString() : null;
        var avatarUrl = string.IsNullOrEmpty(avatar)
            ? ""
            : $"https://cdn.discordapp.com/avatars/{id}/{avatar}.png";

        var sessionToken = RandomHex(32);
        await storePending(state, sessionToken, new DiscordUser(id, username, avatarUrl));
    }

    public static string GenerateEncryptionKey() => RandomHex(32);

    // Go randomHex(n): n random bytes -> 2n lowercase hex chars.
    public static string RandomHex(int n)
    {
        var b = RandomNumberGenerator.GetBytes(n);
        return Convert.ToHexStringLower(b);
    }
}
