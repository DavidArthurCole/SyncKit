using System.Net.Http.Headers;
using System.Text.Json;
using SyncKit.Contract;

namespace SyncKit.Bot;

public sealed class DeployAgentClient {
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(120) };

    public static async Task<DeployResponse> CallAsync(string agentUrl, string secret, CancellationToken ct = default) {
        try {
            using var req = new HttpRequestMessage(HttpMethod.Post, agentUrl);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
            using var resp = await Http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
                return new DeployResponse { Tail = $"deploy agent returned {(int)resp.StatusCode} {resp.ReasonPhrase}" };
            return Parse(await resp.Content.ReadAsStringAsync(ct));
        } catch (Exception ex) {
            return new DeployResponse { Tail = ex.Message };
        }
    }

    public static DeployResponse Parse(string json) {
        try {
            return JsonSerializer.Deserialize<DeployResponse>(json)
                   ?? new DeployResponse { Tail = "could not decode deploy agent response" };
        } catch (JsonException) {
            return new DeployResponse { Tail = "could not decode deploy agent response" };
        }
    }
}
