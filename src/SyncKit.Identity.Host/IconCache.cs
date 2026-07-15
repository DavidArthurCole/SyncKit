using System.Text.Json;

namespace SyncKit.Identity.Host;

// Proxies Authentik source icons so consuming apps get a token-free URL.
// Authentik gates media behind a short-lived signed token, so we fetch through the flow
// executor (which mints a fresh token each call), download the bytes, and cache them for
// the process lifetime. Icons are near-static; restart to refresh.
public sealed class IconCache(IHttpClientFactory httpClientFactory, string authority) {
    private readonly string authority = authority.TrimEnd('/');
    private readonly Dictionary<string, CachedIcon> cache = new(StringComparer.OrdinalIgnoreCase);

    public sealed record CachedIcon(byte[] Bytes, string ContentType);

    // A concurrent cache-miss may fetch the same icon twice; both results are identical so last write wins.
    public async Task<CachedIcon?> GetAsync(string provider, CancellationToken ct) {
        lock (cache) {
            if (cache.TryGetValue(provider, out var hit)) return hit;
        }
        var fetched = await FetchAsync(provider, ct);
        if (fetched is not null) {
            lock (cache) { cache[provider] = fetched; }
        }
        return fetched;
    }

    private async Task<CachedIcon?> FetchAsync(string provider, CancellationToken ct) {
        var iconUrl = await ResolveIconUrlAsync(provider, ct);
        if (iconUrl is null) return null;

        var http = httpClientFactory.CreateClient();
        using var resp = await http.GetAsync(iconUrl, ct);
        if (!resp.IsSuccessStatusCode) return null;
        var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
        var contentType = resp.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
        return new CachedIcon(bytes, contentType);
    }

    private async Task<string?> ResolveIconUrlAsync(string provider, CancellationToken ct) {
        var http = httpClientFactory.CreateClient();
        using var resp = await http.GetAsync($"{authority}/api/v3/flows/executor/federated-authentication-flow/", ct);
        if (!resp.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("sources", out var sources) || sources.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var source in sources.EnumerateArray()) {
            var name = source.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (!string.Equals(name, provider, StringComparison.OrdinalIgnoreCase)) continue;
            var iconUrl = source.TryGetProperty("icon_url", out var i) && i.ValueKind == JsonValueKind.String ? i.GetString() : null;
            if (string.IsNullOrEmpty(iconUrl)) return null;
            return iconUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? iconUrl : $"{authority}/{iconUrl.TrimStart('/')}";
        }
        return null;
    }
}
