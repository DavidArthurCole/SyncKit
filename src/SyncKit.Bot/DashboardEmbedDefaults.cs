using SyncKit.Contract;

namespace SyncKit.Bot;

public static class DashboardEmbedDefaults {
    public static readonly EmbedSpec Default = new(
        Color: 0x5865F2,
        AuthorName: null, AuthorUrl: null, AuthorIconUrl: null,
        Title: "{{ app_name }}", TitleUrl: null,
        Description: null,
        Fields: new List<EmbedFieldSpec> {
            new("Version", "{{ version }}", true),
            new("Status", "{{ deploy_status }}", true),
            new("Up since", "<t:{{ up_since_unix }}:R>", true),
            new("Build", "{{ if build_hash != \"\" }}`{{ build_hash_short }}`{{ end }}", true),
            new("Repo", "{{ repo_url }}", true),
        },
        ImageUrl: null, ThumbnailUrl: null,
        FooterText: "Updated", FooterIconUrl: null,
        Timestamp: true);

    public static readonly IReadOnlyList<(string Name, string Desc)> Variables = new[] {
        ("app_name", "app name"),
        ("version", "running version"),
        ("build_hash", "full build/commit hash"),
        ("build_hash_short", "7-char build/commit hash"),
        ("deploy_status", "deploy/health status"),
        ("up_since_unix", "process start, unix seconds; <t:{{ up_since_unix }}:R>"),
        ("repo_url", "repository URL"),
        ("extra.<key>", "app-supplied ExtraFields entry, e.g. {{ extra.Mode }}"),
    };
}
