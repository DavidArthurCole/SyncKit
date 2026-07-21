namespace SyncKit.Contract;

public sealed record RateMinute(long MinuteEpochSeconds, int Total, int Limited);

public sealed record PathCount(string Path, long Count);

public sealed record IpCount(string Ip, long Count, long LastSeenEpochSeconds);

public sealed record RecentRequest(long AtEpochSeconds, string Method, string Path, int Status, string Bucket, string Ip);

public sealed record BucketTotals(long Internal, long Cross, long External, bool KeysCapped);

public sealed record TrafficSnapshot(
    IReadOnlyList<RateMinute> Minutes,
    IReadOnlyList<PathCount> Paths,
    IReadOnlyList<IpCount> Ips,
    IReadOnlyList<RecentRequest> Recent,
    BucketTotals Buckets);
