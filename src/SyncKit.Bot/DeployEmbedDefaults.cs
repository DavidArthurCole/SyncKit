using SyncKit.Contract;

namespace SyncKit.Bot;

public static class DeployEmbedDefaults {
    public static readonly EmbedSpec Success = new(
        Color: 0x57F287,
        AuthorName: null, AuthorUrl: null, AuthorIconUrl: null,
        Title: "Updated", TitleUrl: null,
        Description: null,
        Fields: new List<EmbedFieldSpec> {
            new("From", "[`{{ from_hash }}`]({{ from_url }})", true),
            new("To", "[`{{ to_hash }}`]({{ to_url }})", true),
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
            new("Current", "[`{{ to_hash }}`]({{ to_url }})", true),
        },
        ImageUrl: null, ThumbnailUrl: null,
        FooterText: null, FooterIconUrl: null,
        Timestamp: false);

    public static readonly IReadOnlyList<(string Name, string Desc)> Variables = new[] {
        ("ok", "true when the deploy succeeded"),
        ("already_up_to_date", "true when no update was needed"),
        ("tail", "tail of the deploy log output"),
        ("from_hash", "commit hash before the deploy"),
        ("to_hash", "commit hash after the deploy"),
        ("from_url", "commit URL before the deploy"),
        ("to_url", "commit URL after the deploy"),
        ("app_name", "name of the deployed app"),
    };
}
