using System.Net.Http.Json;
using SyncKit.Contract;

namespace SyncKit.Identity.Client;

public sealed class IdentityApiClient(HttpClient http) {
    public async Task<IdentityResolveResponse> ResolveAsync(
        string provider, string subject, string? discordId, string? username, string? avatar, CancellationToken ct) {
        var req = new IdentityResolveRequest { Provider = provider, Subject = subject, DiscordId = discordId, Username = username, Avatar = avatar };
        var resp = await http.PostAsJsonAsync("/identity/resolve", req, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<IdentityResolveResponse>(cancellationToken: ct))!;
    }

    public async Task<IdentityUserResponse?> GetAsync(Guid userId, CancellationToken ct) {
        var resp = await http.GetAsync($"/identity/{userId}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<IdentityUserResponse>(cancellationToken: ct);
    }

    public async Task<IReadOnlyList<IdentityUserResponse>> ListAdminUsersAsync(CancellationToken ct) {
        var resp = await http.GetAsync("/identity/admin/users", ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<List<IdentityUserResponse>>(cancellationToken: ct))!;
    }

    public async Task RevokeSessionAsync(string sid, CancellationToken ct) {
        var resp = await http.PostAsJsonAsync("/identity/revoke-session", new RevokeSessionRequest { Sid = sid }, ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task<bool> IsRevokedAsync(string sid, CancellationToken ct) {
        var resp = await http.GetAsync($"/identity/sessions/{sid}/revoked", ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<bool>(cancellationToken: ct));
    }

    public async Task<Guid> MergeAsync(Guid keepUserId, Guid mergeUserId, CancellationToken ct) {
        var resp = await http.PostAsJsonAsync("/identity/merge", new MergeUsersRequest { KeepUserId = keepUserId, MergeUserId = mergeUserId }, ct);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<MergeResult>(cancellationToken: ct);
        return body!.UserId;
    }

    public async Task SetRoleAsync(Guid userId, string role, CancellationToken ct) {
        var resp = await http.PostAsJsonAsync($"/identity/{userId}/role", new SetRoleRequest { Role = role }, ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task<RedeemLoginCodeResponse> RedeemAsync(string code, CancellationToken ct) {
        var resp = await http.PostAsJsonAsync("/identity/redeem", new RedeemLoginCodeRequest { Code = code }, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<RedeemLoginCodeResponse>(cancellationToken: ct))!;
    }

    public async Task<LoginSourcesResponse> GetLoginSourcesAsync(string returnUrl, string mode, CancellationToken ct) {
        var url = $"/login/sources?returnUrl={Uri.EscapeDataString(returnUrl)}&mode={Uri.EscapeDataString(mode)}";
        var resp = await http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<LoginSourcesResponse>(cancellationToken: ct))!;
    }

    public async Task<ProfileResponse?> GetProfileAsync(string sessionToken, CancellationToken ct) {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/profile/me");
        req.Headers.Add("X-SyncKit-Session", sessionToken);
        var resp = await http.SendAsync(req, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<ProfileResponse>(cancellationToken: ct);
    }

    public string StartLinkUrl(string provider, string returnUrl) =>
        $"/profile/link/{provider}/start?returnUrl={Uri.EscapeDataString(returnUrl)}";

    public async Task<bool> UnlinkIdentityAsync(string sessionToken, string provider, string subject, CancellationToken ct) {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"/profile/identities/{provider}/{subject}/unlink");
        req.Headers.Add("X-SyncKit-Session", sessionToken);
        var resp = await http.SendAsync(req, ct);
        return resp.IsSuccessStatusCode;
    }

    public async Task<bool> UploadAvatarAsync(string sessionToken, Stream content, string fileName, string contentType, CancellationToken ct) {
        using var form = new MultipartFormDataContent();
        using var fileContent = new StreamContent(content);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "file", fileName);
        using var req = new HttpRequestMessage(HttpMethod.Post, "/profile/avatar") { Content = form };
        req.Headers.Add("X-SyncKit-Session", sessionToken);
        var resp = await http.SendAsync(req, ct);
        return resp.IsSuccessStatusCode;
    }

    public async Task<bool> SelectAvatarAsync(string sessionToken, string provider, string subject, CancellationToken ct) {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/profile/avatar/select") {
            Content = JsonContent.Create(new AvatarSelectRequest { Provider = provider, Subject = subject }),
        };
        req.Headers.Add("X-SyncKit-Session", sessionToken);
        var resp = await http.SendAsync(req, ct);
        return resp.IsSuccessStatusCode;
    }

    private sealed record MergeResult(Guid UserId);
}
