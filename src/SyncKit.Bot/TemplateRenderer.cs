using Scriban;
using SyncKit.Contract;

namespace SyncKit.Bot;

// Scriban wrapper for admin-editable deploy notification templates. Never throws - a bad
// template must never break a real deploy notification, so any parse/render failure falls
// back to the caller-supplied plain-text default.
public static class TemplateRenderer {
    public static string Render(string? template, string fallback, DeployResponse res, string appName) {
        if (string.IsNullOrEmpty(template)) return fallback;
        return TryRender(template, res, appName, out var rendered) ? rendered : fallback;
    }

    // Embed-field variant: null/empty template yields "" (skip), a broken template also yields "".
    public static string RenderOrEmpty(string? template, DeployResponse res, string appName) {
        if (string.IsNullOrEmpty(template)) return "";
        return TryRender(template, res, appName, out var rendered) ? rendered : "";
    }

    private static bool TryRender(string template, DeployResponse res, string appName, out string rendered) {
        rendered = "";
        try {
            var parsed = Template.Parse(template);
            if (parsed.HasErrors) return false;

            var context = new TemplateContext();
            context.PushGlobal(BuildScriptObject(res, appName));
            rendered = parsed.Render(context);
            return true;
        } catch {
            return false;
        }
    }

    private static Scriban.Runtime.ScriptObject BuildScriptObject(DeployResponse res, string appName) => new() {
        { "ok", res.Ok },
        { "already_up_to_date", res.AlreadyUpToDate },
        { "tail", res.Tail ?? "" },
        { "from_hash", res.FromHash ?? "" },
        { "to_hash", res.ToHash ?? "" },
        { "from_url", res.FromUrl ?? "" },
        { "to_url", res.ToUrl ?? "" },
        { "app_name", appName },
    };
}
