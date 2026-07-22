namespace SyncKit.Bot;

public enum ThreadKind {
    GithubFeed,
    DeployNotifications,
}

public static class ThreadKinds {
    public static string ToName(ThreadKind kind) => kind.ToString();
}
