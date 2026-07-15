namespace SyncKit.Bot;

// Fixed set of feature threads ChannelHub can create under a guild's dashboard channel.
// Adding a kind means: new enum value + one admin config field + one page section.
public enum ThreadKind {
    GithubFeed,
    DeployNotifications,
}

public static class ThreadKinds {
    public static string ToName(ThreadKind kind) => kind.ToString();
}
