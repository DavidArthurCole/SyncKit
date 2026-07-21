using Scriban.Runtime;
using SyncKit.Contract;

namespace SyncKit.Bot;

// Builds the Scriban variable set for deploy-notification templates. The token names are frozen
// (they appear in DeployEmbedDefaults.Variables and every saved template).
public static class DeployVars {
    public static IReadOnlyDictionary<string, object?> Build(DeployResponse res, string appName) =>
        new Dictionary<string, object?> {
            ["ok"] = res.Ok,
            ["already_up_to_date"] = res.AlreadyUpToDate,
            ["tail"] = res.Tail ?? "",
            ["from_hash"] = res.FromHash ?? "",
            ["to_hash"] = res.ToHash ?? "",
            ["from_url"] = res.FromUrl ?? "",
            ["to_url"] = res.ToUrl ?? "",
            ["app_name"] = appName,
        };
}

// Builds the Scriban variable set for the dashboard embed. Provider ExtraFields are exposed as a
// nested object so templates read {{ extra.Mode }}. Token names frozen (DashboardEmbedDefaults.Variables).
public static class DashboardVars {
    public static IReadOnlyDictionary<string, object?> Build(DashboardSnapshot snapshot) {
        var extra = new ScriptObject();
        foreach (var (key, value) in snapshot.ExtraFields) extra[key] = value;

        return new Dictionary<string, object?> {
            ["app_name"] = snapshot.AppName,
            ["version"] = snapshot.Version,
            ["build_hash"] = snapshot.BuildHash,
            ["deploy_status"] = snapshot.DeployStatus,
            ["up_since_unix"] = snapshot.UptimeSince.ToUnixTimeSeconds(),
            ["repo_url"] = snapshot.RepoUrl,
            ["extra"] = extra,
        };
    }
}
