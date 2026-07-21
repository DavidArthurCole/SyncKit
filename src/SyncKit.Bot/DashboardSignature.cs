using System.Text;
using SyncKit.Contract;

namespace SyncKit.Bot;

public static class DashboardSignature {
    public static string Of(DashboardSnapshot snapshot) {
        var sb = new StringBuilder();
        Append(sb, snapshot.AppName);
        Append(sb, snapshot.Version);
        Append(sb, snapshot.BuildHash);
        Append(sb, snapshot.DeployStatus);
        Append(sb, snapshot.UptimeSince.ToUnixTimeSeconds().ToString());
        Append(sb, snapshot.RepoUrl);
        foreach (var (key, value) in snapshot.ExtraFields.OrderBy(kv => kv.Key, StringComparer.Ordinal)) {
            Append(sb, key);
            Append(sb, value);
        }
        return sb.ToString();
    }

    private static void Append(StringBuilder sb, string value) {
        sb.Append(value.Length).Append(':').Append(value);
    }
}
