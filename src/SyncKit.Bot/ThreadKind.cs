namespace SyncKit.Bot;

// Fixed set of feature threads ChannelHub can create under a guild's dashboard channel.
// New kinds get added here, not via new env vars - EnabledThreads CSV picks them up for free.
public enum ThreadKind {
    GithubFeed,
    DeployNotifications,
}

public static class ThreadKinds {
    public static string ToName(ThreadKind kind) => kind.ToString();

    public static bool TryParse(string name, out ThreadKind kind) =>
        Enum.TryParse(name.Trim(), ignoreCase: true, out kind) && Enum.IsDefined(kind);

    // Order-preserving, case-insensitive, tolerant of blank/duplicate entries.
    public static IReadOnlyList<ThreadKind> ParseCsv(string? csv) {
        if (string.IsNullOrWhiteSpace(csv)) return Array.Empty<ThreadKind>();
        var seen = new HashSet<ThreadKind>();
        var result = new List<ThreadKind>();
        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries)) {
            if (TryParse(part, out var kind) && seen.Add(kind))
                result.Add(kind);
        }
        return result;
    }
}
