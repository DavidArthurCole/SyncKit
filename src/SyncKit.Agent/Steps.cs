using System.Net.Http.Headers;
using System.Text.Json;

namespace SyncKit.Agent;

// Pipeline steps, ported from the Go agent (steps.go) one-for-one, plus PortainerUpdateService. Each
// returns null on success or an error string on failure. Commands run via RunContext.Run (never throws).

// GitPull pulls the repo, records from/to short hashes, and flags a no-op pull.
public sealed class GitPull : IStep {
    public string? Exec(RunContext c) {
        c.FromHash = ShortHash(c);
        var (output, ok) = c.Run("git", ["-C", c.Repo, "pull"]);
        c.Out.Append(output);
        if (!ok) return output.Trim().Length > 0 ? output.Trim() : "git pull failed";
        if (output.Contains("Already up to date.")) {
            c.ShortCircuit = true;
            c.ToHash = c.FromHash;
            return null;
        }
        c.ToHash = ShortHash(c);
        return null;
    }

    private static string ShortHash(RunContext c) {
        var (output, _) = c.Run("git", ["-C", c.Repo, "rev-parse", "--short", "HEAD"]);
        return output.Trim();
    }
}

// DockerBuild builds the repo into a tagged image.
public sealed class DockerBuild : IStep {
    public string Tag { get; set; } = "";

    public string? Exec(RunContext c) {
        var (output, ok) = c.Run("docker", ["build", "-t", Tag, c.Repo]);
        c.Out.Append(output);
        return ok ? null : "docker build failed";
    }
}

// DockerPull pulls an image ref and records from/to identities.
// With Container set, a no-op pull only short-circuits when that container also already runs the pulled image, so a lagging container still gets reconciled.
public sealed class DockerPull : IStep {
    public string Ref { get; set; } = "";
    public string Container { get; set; } = "";

    // Must run even after a git-pull short-circuit: the image tag can advance on an unchanged commit.
    public bool RunOnShortCircuit => true;

    public string? Exec(RunContext c) {
        // From = what the CONTAINER is running now (the version being replaced), so the embed shows a
        // real old->new even when the image was pre-pulled on an earlier tick. Falls back to the ref's
        // own identity when no container is configured.
        (c.FromHash, c.FromUrl) = Container != "" ? ContainerIdent(c, Container) : ImageIdent(c, Ref);
        var (output, ok) = c.Run("docker", ["pull", Ref]);
        c.Out.Append(output);
        if (!ok) return output.Trim().Length > 0 ? output.Trim() : "docker pull failed";

        // To = the pulled image's identity (what the container WILL run after the deploy step recreates).
        (c.ToHash, c.ToUrl) = ImageIdent(c, Ref);

        // Short-circuit only when the pull was a no-op AND the container already runs that image, so a
        // container lagging a pre-pulled image is still reconciled by the downstream deploy step.
        if (output.Contains("Image is up to date") && ContainerMatchesImage(c))
            c.ShortCircuit = true;
        return null;
    }

    private bool ContainerMatchesImage(RunContext c) {
        if (Container == "") return true;
        var (imgOut, imgOk) = c.Run("docker", ["image", "inspect", "--format", "{{.Id}}", Ref]);
        if (!imgOk) return true;
        var (ctrOut, ctrOk) = c.Run("docker", ["inspect", "--format", "{{.Image}}", Container]);
        if (!ctrOk) return true;
        return imgOut.Trim() == ctrOut.Trim();
    }

    // Identity of the image a running container is on (its image ID -> revision label).
    private static (string?, string?) ContainerIdent(RunContext c, string container) {
        var (idOut, ok) = c.Run("docker", ["inspect", "--format", "{{.Image}}", container]);
        if (!ok) return (null, null);
        return IdentFromImageId(c, idOut.Trim());
    }

    private static (string?, string?) ImageIdent(RunContext c, string imageRef) {
        var (output, ok) = c.Run("docker",
            ["image", "inspect", "--format", "{{.Id}} {{index .Config.Labels \"org.opencontainers.image.revision\"}}", imageRef]);
        if (!ok) return (null, null);
        return ParseIdent(c, output);
    }

    // Resolve an image ID (sha256:...) to its (revision-or-digest, commit-url).
    private static (string?, string?) IdentFromImageId(RunContext c, string imageId) {
        if (imageId == "") return (null, null);
        var (output, ok) = c.Run("docker",
            ["image", "inspect", "--format", "{{.Id}} {{index .Config.Labels \"org.opencontainers.image.revision\"}}", imageId]);
        if (!ok) return (ShortDigest(imageId), null);
        return ParseIdent(c, output);
    }

    private static (string?, string?) ParseIdent(RunContext c, string inspectOutput) {
        var trimmed = inspectOutput.Trim();
        var space = trimmed.IndexOf(' ');
        var id = space < 0 ? trimmed : trimmed[..space];
        var rev = space < 0 ? "" : trimmed[(space + 1)..].Trim();
        if (rev is not ("" or "<no value>")) {
            var url = c.RepoUrl != "" ? c.RepoUrl.TrimEnd('/') + "/commit/" + rev : "";
            return (ShortSha(rev), url == "" ? null : url);
        }
        return (ShortDigest(id), null);
    }

    private static string ShortSha(string sha) => sha.Length > 7 ? sha[..7] : sha;

    private static string ShortDigest(string id) {
        id = id.StartsWith("sha256:", StringComparison.Ordinal) ? id["sha256:".Length..] : id;
        return id.Length > 12 ? id[..12] : id;
    }
}

// ContainerRecreate is a deprecated no-op kept so existing yaml still parses. The old stop+rm left a
// downtime window; the deploy recreates in place (Portainer step / webhook) instead.
public sealed class ContainerRecreate : IStep {
    public string Name { get; set; } = "";

    public string? Exec(RunContext c) {
        c.Out.Append($"container-recreate {Name}: skipped (deploy recreates in place)\n");
        return null;
    }
}

// Webhook POSTs to a URL (literal or resolved from an env var). Non-2xx fails. Kept for parity; the
// Portainer webhook does NOT re-pull an unchanged :latest, so prefer PortainerUpdateService.
public sealed class Webhook : IStep {
    public string Url { get; set; } = "";
    public string UrlEnv { get; set; } = "";

    public string? Exec(RunContext c) {
        var url = Url;
        if (url == "" && UrlEnv != "") url = Environment.GetEnvironmentVariable(UrlEnv) ?? "";
        if (url == "") return "webhook: no URL";
        try {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var resp = http.PostAsync(url, null).GetAwaiter().GetResult();
            c.Out.Append($"webhook {url} -> {(int)resp.StatusCode}\n");
            if (!resp.IsSuccessStatusCode) return $"webhook returned {(int)resp.StatusCode}";
            return null;
        } catch (Exception e) { return $"webhook: {e.Message}"; }
    }
}

// Shell runs an arbitrary command. The escape hatch.
public sealed class Shell : IStep {
    public string Run { get; set; } = "";
    public string Dir { get; set; } = "";
    public bool Always { get; set; }

    public bool RunOnShortCircuit => Always;

    public string? Exec(RunContext c) {
        var dir = Dir != "" ? Dir : c.Repo;
        var (output, ok) = c.Run("sh", ["-c", $"cd {dir} && {Run}"]);
        c.Out.Append(output);
        return ok ? null : (output.Trim().Length > 0 ? output.Trim() : "shell command failed");
    }
}

// AppCallback POSTs to an app-owned endpoint for deploy logic SyncKit has no business knowing about.
// Bearer-gated, same shape as DEPLOY_AGENT_SECRET/POST /deploy but inverted.
public sealed class AppCallback : IStep {
    public string UrlEnv { get; set; } = "";
    public string SecretEnv { get; set; } = "";

    public string? Exec(RunContext c) {
        var url = Environment.GetEnvironmentVariable(UrlEnv) ?? "";
        if (url == "") return $"app-callback: {UrlEnv} unset";
        var secret = SecretEnv != "" ? Environment.GetEnvironmentVariable(SecretEnv) ?? "" : "";
        try {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
            if (secret != "")
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
            var resp = http.PostAsync(url, null).GetAwaiter().GetResult();
            var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            c.Out.Append(body).Append('\n');
            return resp.IsSuccessStatusCode ? null : $"app-callback {url} -> {(int)resp.StatusCode}";
        } catch (Exception e) { return $"app-callback: {e.Message}"; }
    }
}

// The stack webhook alone no-ops on an unchanged :latest tag, so this PUTs the stack directly to force a real re-pull + recreate.
public sealed class PortainerUpdateService : IStep {
    public string UrlEnv { get; set; } = "PORTAINER_API_URL";
    public string KeyEnv { get; set; } = "PORTAINER_API_KEY";
    public string StackIdEnv { get; set; } = "PORTAINER_STACK_ID";
    public string EndpointIdEnv { get; set; } = "PORTAINER_ENDPOINT_ID";
    // false avoids a 500 on stacks mixing registry + locally-built images, since Portainer tries to pull the local-only ones too.
    public bool PullImage { get; set; }

    public string? Exec(RunContext c) {
        var baseUrl = (Environment.GetEnvironmentVariable(UrlEnv) ?? "").TrimEnd('/');
        var key = Environment.GetEnvironmentVariable(KeyEnv) ?? "";
        var stackId = Environment.GetEnvironmentVariable(StackIdEnv) ?? "";
        var endpointId = Environment.GetEnvironmentVariable(EndpointIdEnv) ?? "";
        if (baseUrl == "" || key == "" || stackId == "" || endpointId == "")
            return $"portainer-update-stack: missing env ({UrlEnv}/{KeyEnv}/{StackIdEnv}/{EndpointIdEnv})";

        try {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
            http.DefaultRequestHeaders.Add("X-API-Key", key);

            // 1. GET the stack to read its current compose + env (the PUT replaces the stack, so we must
            //    echo them back unchanged or Portainer wipes them).
            var getResp = http.GetAsync($"{baseUrl}/api/stacks/{stackId}").GetAwaiter().GetResult();
            var getBody = getResp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!getResp.IsSuccessStatusCode)
                return $"portainer-update-stack: GET stack {(int)getResp.StatusCode}: {Trunc(getBody)}";

            using var stack = JsonDocument.Parse(getBody);
            var fileResp = http.GetAsync($"{baseUrl}/api/stacks/{stackId}/file").GetAwaiter().GetResult();
            var fileBody = fileResp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!fileResp.IsSuccessStatusCode)
                return $"portainer-update-stack: GET stack file {(int)fileResp.StatusCode}: {Trunc(fileBody)}";
            using var fileDoc = JsonDocument.Parse(fileBody);
            var composeContent = fileDoc.RootElement.GetProperty("StackFileContent").GetString() ?? "";

            // Echo the existing env array back verbatim.
            var envArray = stack.RootElement.TryGetProperty("Env", out var env) && env.ValueKind == JsonValueKind.Array
                ? env
                : default;

            // 2. PUT with pullImage=true to force the re-pull + recreate.
            var payload = new Dictionary<string, object?> {
                ["stackFileContent"] = composeContent,
                ["pullImage"] = PullImage,
                ["prune"] = false,
            };
            if (envArray.ValueKind == JsonValueKind.Array)
                payload["env"] = JsonSerializer.Deserialize<object[]>(envArray.GetRawText());

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var putUrl = $"{baseUrl}/api/stacks/{stackId}?endpointId={endpointId}";
            var putResp = http.PutAsync(putUrl, content).GetAwaiter().GetResult();
            var putBody = putResp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            c.Out.Append($"portainer-update-stack {stackId} -> {(int)putResp.StatusCode}\n");
            if (!putResp.IsSuccessStatusCode)
                return $"portainer-update-stack: PUT {(int)putResp.StatusCode}: {Trunc(putBody)}";
            return null;
        } catch (Exception e) { return $"portainer-update-stack: {e.Message}"; }
    }

    private static string Trunc(string s) => s.Length <= 300 ? s : s[..300];
}
