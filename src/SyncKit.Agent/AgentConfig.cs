using YamlDotNet.RepresentationModel;

namespace SyncKit.Agent;

public sealed class AgentConfig {
    public string Name { get; init; } = "";
    public string Repo { get; init; } = "";
    public string RepoUrl { get; init; } = "";
    public IReadOnlyList<IStep> Steps { get; init; } = [];

    public static AgentConfig Parse(string yaml) {
        var stream = new YamlStream();
        using var reader = new StringReader(yaml);
        stream.Load(reader);
        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
            throw new FormatException("config: empty or not a mapping");

        var steps = new List<IStep>();
        if (TryGet(root, "steps") is YamlSequenceNode seq) {
            foreach (var node in seq.Children)
                steps.Add(DecodeStep(node));
        }

        return new AgentConfig {
            Name = Scalar(TryGet(root, "name")) ?? "",
            Repo = Scalar(TryGet(root, "repo")) ?? "",
            RepoUrl = Scalar(TryGet(root, "repo_url")) ?? "",
            Steps = steps,
        };
    }

    private static IStep DecodeStep(YamlNode node) {
        if (node is YamlScalarNode s) {
            return s.Value switch {
                "git-pull" => new GitPull(),
                "portainer-update-stack" => new PortainerUpdateService(),
                _ => throw new FormatException($"unknown bare step \"{s.Value}\""),
            };
        }
        if (node is YamlMappingNode m && m.Children.Count == 1) {
            var (keyNode, paramsNode) = (m.Children.First().Key, m.Children.First().Value);
            var key = ((YamlScalarNode)keyNode).Value;
            var p = paramsNode as YamlMappingNode ?? [];
            return key switch {
                "docker-pull" => new DockerPull { Ref = Field(p, "ref"), Container = Field(p, "container") },
                "docker-build" => new DockerBuild { Tag = Field(p, "tag") },
                "container-recreate" => new ContainerRecreate { Name = Field(p, "name") },
                "webhook" => new Webhook { Url = Field(p, "url"), UrlEnv = Field(p, "url_env") },
                "shell" => new Shell { Run = Field(p, "run"), Dir = Field(p, "dir"), Always = Field(p, "always") == "true" },
                "portainer-update-stack" => new PortainerUpdateService {
                    UrlEnv = FieldOr(p, "url_env", "PORTAINER_API_URL"),
                    KeyEnv = FieldOr(p, "key_env", "PORTAINER_API_KEY"),
                    StackIdEnv = FieldOr(p, "stack_id_env", "PORTAINER_STACK_ID"),
                    EndpointIdEnv = FieldOr(p, "endpoint_id_env", "PORTAINER_ENDPOINT_ID"),
                    PullImage = Field(p, "pull_image") == "true",
                },
                _ => throw new FormatException($"unknown step \"{key}\""),
            };
        }
        throw new FormatException("malformed step node");
    }

    private static YamlNode? TryGet(YamlMappingNode map, string key) =>
        map.Children.TryGetValue(new YamlScalarNode(key), out var v) ? v : null;

    private static string? Scalar(YamlNode? n) => (n as YamlScalarNode)?.Value;
    private static string Field(YamlMappingNode p, string key) => Scalar(TryGet(p, key)) ?? "";
    private static string FieldOr(YamlMappingNode p, string key, string fallback) {
        var v = Scalar(TryGet(p, key));
        return string.IsNullOrEmpty(v) ? fallback : v;
    }

    internal static TimeSpan ParseDuration(string s) {
        var total = TimeSpan.Zero;
        var num = "";
        foreach (var ch in s) {
            if (char.IsDigit(ch) || ch == '.') { num += ch; continue; }
            if (num == "") throw new FormatException($"bad duration \"{s}\"");
            var value = double.Parse(num, System.Globalization.CultureInfo.InvariantCulture);
            total += ch switch {
                'h' => TimeSpan.FromHours(value),
                'm' => TimeSpan.FromMinutes(value),
                's' => TimeSpan.FromSeconds(value),
                _ => throw new FormatException($"bad duration unit '{ch}' in \"{s}\""),
            };
            num = "";
        }
        if (num != "") throw new FormatException($"duration \"{s}\" missing unit");
        return total;
    }
}
