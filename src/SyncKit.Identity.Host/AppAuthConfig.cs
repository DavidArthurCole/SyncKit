using SyncKit.Auth;

namespace SyncKit.Identity.Host;

public sealed record AppAuthConfig(string Origin, AuthentikOAuth OAuth);

public static class AppAuthConfigLoader {
    private static readonly string[] RequiredKeys = ["Origin", "ClientId", "ClientSecret", "CallbackUrl"];

    public static Dictionary<string, AppAuthConfig> LoadFromDirectory(string dirPath, string authentikAuthority) {
        var result = new Dictionary<string, AppAuthConfig>();
        foreach (var filePath in Directory.EnumerateFiles(dirPath)) {
            var values = ParseFile(filePath);
            var missing = RequiredKeys.Where(k => !values.ContainsKey(k)).ToList();
            if (missing.Count > 0)
                throw new InvalidOperationException($"{filePath} missing required key(s): {string.Join(", ", missing)}");

            var oauth = new AuthentikOAuth(authentikAuthority, values["ClientId"], values["ClientSecret"], values["CallbackUrl"]);
            result[values["Origin"]] = new AppAuthConfig(values["Origin"], oauth);
        }
        return result;
    }

    private static Dictionary<string, string> ParseFile(string filePath) {
        var values = new Dictionary<string, string>();
        foreach (var line in File.ReadAllLines(filePath)) {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var idx = line.IndexOf('=');
            if (idx < 0) throw new InvalidOperationException($"{filePath}: malformed line \"{line}\"");
            values[line[..idx].Trim()] = line[(idx + 1)..].Trim();
        }
        return values;
    }
}
