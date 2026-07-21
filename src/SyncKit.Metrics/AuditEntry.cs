namespace SyncKit.Metrics;

public sealed record AuditEntry(
    long AtEpochSeconds,
    string Method,
    string Path,
    int Status,
    RequestBucket Bucket,
    string Ip,
    string? User);
