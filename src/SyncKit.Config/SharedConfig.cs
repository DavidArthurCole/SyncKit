using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SyncKit.Config;

// The narrow, explicitly-scoped set of values that are genuinely identical across 2+ containers
// in the stack today (copy-pasted per service block). NOT a config service, NOT a replacement
// for per-app secrets (OAuth client ids/secrets, per-app DB credentials stay as env vars).
public sealed record SharedConfig(
    string? SharedRoleId,
    string? DeployAgentUrl,
    string? DeployAgentSecret,
    string? PostgresHost,
    string? PostgresPort,
    string? AuthentikAuthority) {
    private sealed class Raw {
        public string? SharedRoleId { get; set; }
        public string? DeployAgentUrl { get; set; }
        public string? DeployAgentSecret { get; set; }
        public string? PostgresHost { get; set; }
        public string? PostgresPort { get; set; }
        public string? AuthentikAuthority { get; set; }
    }

    // Any field not present in the file (or the file itself missing) falls back to the
    // equivalent env var via envFallback. Purely additive: no caller breaks if the file
    // doesn't exist yet.
    public static SharedConfig Load(string path, Func<string, string?> envFallback) {
        Raw raw = new();
        if (File.Exists(path)) {
            var yaml = File.ReadAllText(path);
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
            raw = deserializer.Deserialize<Raw>(yaml) ?? new Raw();
        }

        return new SharedConfig(
            raw.SharedRoleId ?? envFallback("SHARED_ROLE_ID"),
            raw.DeployAgentUrl ?? envFallback("DEPLOY_AGENT_URL"),
            raw.DeployAgentSecret ?? envFallback("DEPLOY_AGENT_SECRET"),
            raw.PostgresHost ?? envFallback("POSTGRES_HOST"),
            raw.PostgresPort ?? envFallback("POSTGRES_PORT"),
            raw.AuthentikAuthority ?? envFallback("AUTHENTIK_AUTHORITY"));
    }
}
