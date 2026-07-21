namespace SyncKit.Metrics;

public sealed class RequestMetricsOptions {
    public string PathPrefix { get; set; } = "/api";
    public string InternalMarkerHeader { get; set; } = "X-Internal";
    public int RecentCapacity { get; set; } = 512;
    public int MaxKeys { get; set; } = 2000;
    public string? HostedDomain { get; set; }
    public bool HostedBehindProxy { get; set; } = true;
}
