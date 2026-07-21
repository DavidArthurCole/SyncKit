namespace SyncKit.Metrics;

public interface IRequestAuditSink {
    Task RecordAsync(AuditEntry entry, CancellationToken ct);
}
