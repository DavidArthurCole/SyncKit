using SyncKit.Contract;

namespace SyncKit.Bot;

public static class DeployEmbedDefaults {
    public static readonly EmbedSpec Success = new(
        Color: 0x57F287,
        AuthorName: null, AuthorUrl: null, AuthorIconUrl: null,
        Title: "Updated", TitleUrl: null,
        Description: null,
        Fields: new List<EmbedFieldSpec> {
            new("From", "[`{{ from_hash_short }}`]({{ from_url }})", true),
            new("To", "[`{{ to_hash_short }}`]({{ to_url }})", true),
        },
        ImageUrl: null, ThumbnailUrl: null,
        FooterText: null, FooterIconUrl: null,
        Timestamp: false);

    public static readonly EmbedSpec Failure = new(
        Color: 0xED4245,
        AuthorName: null, AuthorUrl: null, AuthorIconUrl: null,
        Title: "Update failed.", TitleUrl: null,
        Description: """
            ```
            {{ tail }}
            ```
            """,
        Fields: new List<EmbedFieldSpec>(),
        ImageUrl: null, ThumbnailUrl: null,
        FooterText: null, FooterIconUrl: null,
        Timestamp: false);

    public static readonly EmbedSpec AlreadyUpToDate = new(
        Color: 0x5865F2,
        AuthorName: null, AuthorUrl: null, AuthorIconUrl: null,
        Title: "Already up to date.", TitleUrl: null,
        Description: null,
        Fields: new List<EmbedFieldSpec> {
            new("Current", "[`{{ to_hash_short }}`]({{ to_url }})", true),
        },
        ImageUrl: null, ThumbnailUrl: null,
        FooterText: null, FooterIconUrl: null,
        Timestamp: false);

    public static readonly IReadOnlyList<(string Name, string Desc)> Variables = new[] {
        ("ok", "deploy succeeded"),
        ("already_up_to_date", "no update needed"),
        ("tail", "deploy log tail"),
        ("from_hash", "full commit hash before"),
        ("from_hash_short", "7-char commit hash before"),
        ("to_hash", "full commit hash after"),
        ("to_hash_short", "7-char commit hash after"),
        ("from_url", "commit URL before"),
        ("to_url", "commit URL after"),
        ("app_name", "app name"),
    };
}
