using System.Text;
using Discord;

namespace SyncKit.Bot;

public sealed record OptionShape(
    string Name, string Description, int Type, bool Required, bool Autocomplete,
    IReadOnlyList<OptionShape> Options);

public sealed record CommandShape(string Name, string Description, IReadOnlyList<OptionShape> Options);

public static class CommandSignature {
    public static string Compute(IEnumerable<CommandShape> commands) {
        var sb = new StringBuilder();
        foreach (var c in commands.OrderBy(c => c.Name, StringComparer.Ordinal)) {
            sb.Append(c.Name).Append('|').Append(c.Description).Append('\n');
            AppendOptions(sb, c.Options, depth: 1);
        }
        return sb.ToString();
    }

    private static void AppendOptions(StringBuilder sb, IReadOnlyList<OptionShape> options, int depth) {
        foreach (var o in options) {
            sb.Append('>', depth).Append(o.Name).Append('|').Append(o.Description).Append('|')
              .Append(o.Type).Append('|').Append(o.Required ? 'r' : '-')
              .Append(o.Autocomplete ? 'a' : '-').Append('\n');
            AppendOptions(sb, o.Options, depth + 1);
        }
    }

    public static CommandShape FromProperties(ApplicationCommandProperties props) {
        var slash = props as SlashCommandProperties;
        return new CommandShape(
            props.Name.GetValueOrDefault() ?? "",
            slash?.Description.GetValueOrDefault() ?? "",
            ToShapes(slash?.Options.GetValueOrDefault()?.Select(FromOption)));
    }

    private static OptionShape FromOption(ApplicationCommandOptionProperties o) => new(
        o.Name ?? "", o.Description ?? "", (int)o.Type, Flag(o.IsRequired), Flag(o.IsAutocomplete),
        ToShapes(o.Options?.Select(FromOption)));

    private static OptionShape FromOption(IApplicationCommandOption o) => new(
        o.Name ?? "", o.Description ?? "", (int)o.Type, Flag(o.IsRequired), Flag(o.IsAutocomplete),
        ToShapes(o.Options?.Select(FromOption)));

    public static CommandShape FromCommand(IApplicationCommand cmd) => new(
        cmd.Name ?? "", cmd.Description ?? "",
        ToShapes(cmd.Options?.Select(FromOption)));

    private static bool Flag(bool? value) => value ?? false;

    private static IReadOnlyList<OptionShape> ToShapes(IEnumerable<OptionShape>? shapes) {
        if (shapes is null) return Array.Empty<OptionShape>();
        return shapes.ToList();
    }
}
