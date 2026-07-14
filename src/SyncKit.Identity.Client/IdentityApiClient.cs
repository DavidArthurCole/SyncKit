using System.Net.Http.Json;
using SyncKit.Contract;

namespace SyncKit.Identity.Client;

// Server-to-server client for SyncKit.Identity.Host. Construct with an HttpClient whose
// BaseAddress is the host's URL and whose default Authorization header is set to
// "Bearer {IDENTITY_API_SECRET}".
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

    public async Task<LoginSourcesResponse> GetLoginSourcesAsync(string returnOrigin, string mode, CancellationToken ct) {
        var url = $"/login/sources?returnOrigin={Uri.EscapeDataString(returnOrigin)}&mode={Uri.EscapeDataString(mode)}";
        var resp = await http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<LoginSourcesResponse>(cancellationToken: ct))!;
    }

    private sealed record MergeResult(Guid UserId);
}
