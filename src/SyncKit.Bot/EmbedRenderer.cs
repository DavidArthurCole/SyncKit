using Discord;
using SyncKit.Contract;

namespace SyncKit.Bot;

public static class EmbedRenderer {
    private const int MaxFields = 25;
    private const int MaxTitle = 256;
    private const int MaxDescription = 4096;
    private const int MaxFieldName = 256;
    private const int MaxFieldValue = 1024;
    private const int MaxFooter = 2048;
    private const int MaxAuthorName = 256;

    public static Embed Render(EmbedSpec spec, DeployResponse res, string appName) =>
        Render(spec, DeployVars.Build(res, appName));

    public static Embed Render(EmbedSpec spec, IReadOnlyDictionary<string, object?> vars) {
        try {
            var builder = new EmbedBuilder();

            var authorName = R(spec.AuthorName, vars);
            if (authorName.Length > 0) {
                var author = new EmbedAuthorBuilder().WithName(Trunc(authorName, MaxAuthorName));
                var authorUrl = R(spec.AuthorUrl, vars);
                if (authorUrl.Length > 0) author.WithUrl(authorUrl);
                var authorIcon = R(spec.AuthorIconUrl, vars);
                if (authorIcon.Length > 0) author.WithIconUrl(authorIcon);
                builder.WithAuthor(author);
            }

            var title = R(spec.Title, vars);
            if (title.Length > 0) {
                builder.WithTitle(Trunc(title, MaxTitle));
                var titleUrl = R(spec.TitleUrl, vars);
                if (titleUrl.Length > 0) builder.WithUrl(titleUrl);
            }

            var description = R(spec.Description, vars);
            if (description.Length > 0) builder.WithDescription(Trunc(description, MaxDescription));

            var count = 0;
            foreach (var field in spec.Fields ?? []) {
                if (count >= MaxFields) break;
                var name = R(field.Name, vars);
                var value = R(field.Value, vars);
                if (name.Length == 0 || value.Length == 0) continue;
                builder.AddField(Trunc(name, MaxFieldName), Trunc(value, MaxFieldValue), field.Inline);
                count++;
            }

            var imageUrl = R(spec.ImageUrl, vars);
            if (imageUrl.Length > 0) builder.WithImageUrl(imageUrl);
            var thumbnailUrl = R(spec.ThumbnailUrl, vars);
            if (thumbnailUrl.Length > 0) builder.WithThumbnailUrl(thumbnailUrl);

            var footerText = R(spec.FooterText, vars);
            if (footerText.Length > 0) {
                var footer = new EmbedFooterBuilder().WithText(Trunc(footerText, MaxFooter));
                var footerIcon = R(spec.FooterIconUrl, vars);
                if (footerIcon.Length > 0) footer.WithIconUrl(footerIcon);
                builder.WithFooter(footer);
            }

            if (spec.Color.HasValue) builder.WithColor(new Color(spec.Color.Value));
            if (spec.Timestamp) builder.WithTimestamp(spec.TimestampFixed ?? DateTimeOffset.UtcNow);

            return builder.Build();
        } catch {
            return new EmbedBuilder().WithDescription("notification").Build();
        }
    }

    private static string R(string? template, IReadOnlyDictionary<string, object?> vars) =>
        TemplateRenderer.RenderOrEmpty(template, vars);

    private static string Trunc(string s, int max) => s.Length <= max ? s : s[..max];
}
