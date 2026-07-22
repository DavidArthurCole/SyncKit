using Discord;
using SyncKit.Contract;

namespace SyncKit.Bot;

public sealed record RenderedMessage(
    Embed? Embed, MessageComponent? Components, bool IsComponentsV2, AllowedMentions AllowedMentions);

public static class MessageRenderer {
    private const int MaxTextDisplay = 4000;
    private const int MaxComponents = 10;

    public static RenderedMessage Render(MessageSpec spec, IReadOnlyDictionary<string, object?> vars) {
        var mentions = spec.Mentions is { } m
            ? new MentionSpec(m.Users ?? [], m.Roles ?? [], m.Everyone)
            : new MentionSpec([], [], false);
        var allowed = BuildAllowedMentions(mentions);
        var withPings = WithPings(vars, mentions);

        if (spec.Kind == MessageKind.Components && spec.Components is { } components) {
            var rendered = RenderComponents(components, withPings);
            return new RenderedMessage(null, rendered, true, allowed);
        }

        var embedSpec = spec.Embed ?? DeployEmbedDefaults.Success;
        var embed = EmbedRenderer.Render(embedSpec, withPings);
        return new RenderedMessage(embed, null, false, allowed);
    }

    private static MessageComponent RenderComponents(ComponentSpec spec, IReadOnlyDictionary<string, object?> vars) {
        try {
            var container = new ContainerBuilder();
            if (spec.AccentColor is uint c) container.WithAccentColor(new Color(c));

            var count = 0;
            foreach (var block in spec.Blocks ?? []) {
                if (count >= MaxComponents) break;
                if (string.Equals(block.Kind, "separator", StringComparison.OrdinalIgnoreCase)) {
                    container.WithSeparator();
                    count++;
                    continue;
                }
                var text = TemplateRenderer.RenderOrEmpty(block.Text, vars);
                if (text.Length == 0) continue;
                container.WithTextDisplay(Trunc(text, MaxTextDisplay));
                count++;
            }

            return new ComponentBuilderV2().AddComponent(container).Build();
        } catch {
            return new ComponentBuilderV2().AddComponent(new TextDisplayBuilder("notification")).Build();
        }
    }

    private static AllowedMentions BuildAllowedMentions(MentionSpec m) {
        var allowed = new AllowedMentions(AllowedMentionTypes.None);
        if (m.Everyone) allowed.AllowedTypes = AllowedMentionTypes.Everyone;
        foreach (var u in m.Users)
            if (ulong.TryParse(u, out var id)) (allowed.UserIds ??= []).Add(id);
        foreach (var r in m.Roles)
            if (ulong.TryParse(r, out var id)) (allowed.RoleIds ??= []).Add(id);
        return allowed;
    }

    private static Dictionary<string, object?> WithPings(
        IReadOnlyDictionary<string, object?> vars, MentionSpec m) {
        var parts = new List<string>();
        foreach (var r in m.Roles) if (ulong.TryParse(r, out _)) parts.Add($"<@&{r}>");
        foreach (var u in m.Users) if (ulong.TryParse(u, out _)) parts.Add($"<@{u}>");
        if (m.Everyone) parts.Add("@everyone");

        return new Dictionary<string, object?>(vars) { ["pings"] = string.Join(" ", parts) };
    }

    private static string Trunc(string s, int max) => s.Length <= max ? s : s[..max];
}
