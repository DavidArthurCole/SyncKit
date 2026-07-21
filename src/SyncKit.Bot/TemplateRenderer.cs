using Scriban;
using Scriban.Runtime;
using SyncKit.Contract;

namespace SyncKit.Bot;

// Scriban wrapper for admin-editable notification templates. Never throws - a bad template must
// never break a real notification, so any parse/render failure falls back to the caller-supplied
// default. Source-agnostic: callers pass a variable dictionary (see DeployVars/DashboardVars).
public static class TemplateRenderer {
    public static string Render(string? template, string fallback, IReadOnlyDictionary<string, object?> vars) {
        if (string.IsNullOrEmpty(template)) return fallback;
        return TryRender(template, vars, out var rendered) ? rendered : fallback;
    }

    // Embed-field variant: null/empty template yields "" (skip), a broken template also yields "".
    public static string RenderOrEmpty(string? template, IReadOnlyDictionary<string, object?> vars) {
        if (string.IsNullOrEmpty(template)) return "";
        return TryRender(template, vars, out var rendered) ? rendered : "";
    }

    public static string Render(string? template, string fallback, DeployResponse res, string appName) =>
        Render(template, fallback, DeployVars.Build(res, appName));

    public static string RenderOrEmpty(string? template, DeployResponse res, string appName) =>
        RenderOrEmpty(template, DeployVars.Build(res, appName));

    private static bool TryRender(string template, IReadOnlyDictionary<string, object?> vars, out string rendered) {
        rendered = "";
        try {
            var parsed = Template.Parse(template);
            if (parsed.HasErrors) return false;

            var obj = new ScriptObject();
            foreach (var (key, value) in vars) obj[key] = value;
            var context = new TemplateContext();
            context.PushGlobal(obj);
            rendered = parsed.Render(context);
            return true;
        } catch {
            return false;
        }
    }
}
