using Scriban;
using SyncKit.Contract;

namespace SyncKit.Bot;

// Scriban wrapper for admin-editable deploy notification templates. Never throws - a bad
// template must never break a real deploy notification, so any parse/render failure falls
// back to the caller-supplied plain-text default.
public static class TemplateRenderer {
    public static string Render(string? template, string fallback, DeployResponse res, string appName) {
        if (string.IsNullOrEmpty(template)) return fallback;
        try {
            var parsed = Template.Parse(template);
            if (parsed.HasErrors) return fallback;

            var scriptObject = new Scriban.Runtime.ScriptObject {
                { "ok", res.Ok },
                { "already_up_to_date", res.AlreadyUpToDate },
                { "tail", res.Tail ?? "" },
                { "from_hash", res.FromHash ?? "" },
                { "to_hash", res.ToHash ?? "" },
                { "from_url", res.FromUrl ?? "" },
                { "to_url", res.ToUrl ?? "" },
                { "app_name", appName },
            };
            var context = new TemplateContext();
            context.PushGlobal(scriptObject);
            return parsed.Render(context);
        } catch {
            return fallback;
        }
    }
}
