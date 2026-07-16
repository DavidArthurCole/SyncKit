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

    public static Embed Render(EmbedSpec spec, DeployResponse res, string appName) {
        try {
            var builder = new EmbedBuilder();

            var authorName = R(spec.AuthorName, res, appName);
            if (authorName.Length > 0) {
                var author = new EmbedAuthorBuilder().WithName(Trunc(authorName, MaxAuthorName));
                var authorUrl = R(spec.AuthorUrl, res, appName);
                if (authorUrl.Length > 0) author.WithUrl(authorUrl);
                var authorIcon = R(spec.AuthorIconUrl, res, appName);
                if (authorIcon.Length > 0) author.WithIconUrl(authorIcon);
                builder.WithAuthor(author);
            }

            var title = R(spec.Title, res, appName);
            if (title.Length > 0) {
                builder.WithTitle(Trunc(title, MaxTitle));
                var titleUrl = R(spec.TitleUrl, res, appName);
                if (titleUrl.Length > 0) builder.WithUrl(titleUrl);
            }

            var description = R(spec.Description, res, appName);
            if (description.Length > 0) builder.WithDescription(Trunc(description, MaxDescription));

            var count = 0;
            foreach (var field in spec.Fields) {
                if (count >= MaxFields) break;
                var name = R(field.Name, res, appName);
                var value = R(field.Value, res, appName);
                if (name.Length == 0 || value.Length == 0) continue;
                builder.AddField(Trunc(name, MaxFieldName), Trunc(value, MaxFieldValue), field.Inline);
                count++;
            }

            var imageUrl = R(spec.ImageUrl, res, appName);
            if (imageUrl.Length > 0) builder.WithImageUrl(imageUrl);
            var thumbnailUrl = R(spec.ThumbnailUrl, res, appName);
            if (thumbnailUrl.Length > 0) builder.WithThumbnailUrl(thumbnailUrl);

            var footerText = R(spec.FooterText, res, appName);
            if (footerText.Length > 0) {
                var footer = new EmbedFooterBuilder().WithText(Trunc(footerText, MaxFooter));
                var footerIcon = R(spec.FooterIconUrl, res, appName);
                if (footerIcon.Length > 0) footer.WithIconUrl(footerIcon);
                builder.WithFooter(footer);
            }

            if (spec.Color.HasValue) builder.WithColor(new Color(spec.Color.Value));
            if (spec.Timestamp) builder.WithTimestamp(spec.TimestampFixed ?? DateTimeOffset.UtcNow);

            return builder.Build();
        } catch {
            return new EmbedBuilder().WithDescription("deploy notification").Build();
        }
    }

    private static string R(string? template, DeployResponse res, string appName) =>
        TemplateRenderer.RenderOrEmpty(template, res, appName);

    private static string Trunc(string s, int max) => s.Length <= max ? s : s[..max];
}
