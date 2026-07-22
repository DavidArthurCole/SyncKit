using Scriban.Runtime;
using SyncKit.Contract;

namespace SyncKit.Bot;

public static class DeployVars {
    public static IReadOnlyDictionary<string, object?> Build(DeployResponse res, string appName) =>
        new Dictionary<string, object?> {
            ["ok"] = res.Ok,
            ["already_up_to_date"] = res.AlreadyUpToDate,
            ["tail"] = res.Tail ?? "",
            ["from_hash"] = res.FromHash ?? "",
            ["from_hash_short"] = ShortHash(res.FromHash),
            ["to_hash"] = res.ToHash ?? "",
            ["to_hash_short"] = ShortHash(res.ToHash),
            ["from_url"] = res.FromUrl ?? "",
            ["to_url"] = res.ToUrl ?? "",
            ["app_name"] = appName,
        };

    internal static string ShortHash(string? hash) =>
        hash is null ? "" : hash.Length <= 7 ? hash : hash[..7];
}

public static class DashboardVars {
    public static IReadOnlyDictionary<string, object?> Build(DashboardSnapshot snapshot) {
        var extra = new ScriptObject();
        foreach (var (key, value) in snapshot.ExtraFields) extra[key] = value;

        return new Dictionary<string, object?> {
            ["app_name"] = snapshot.AppName,
            ["version"] = snapshot.Version,
            ["build_hash"] = snapshot.BuildHash,
            ["build_hash_short"] = DeployVars.ShortHash(snapshot.BuildHash),
            ["deploy_status"] = snapshot.DeployStatus,
            ["up_since_unix"] = snapshot.UptimeSince.ToUnixTimeSeconds(),
            ["repo_url"] = snapshot.RepoUrl,
            ["extra"] = extra,
        };
    }
}
