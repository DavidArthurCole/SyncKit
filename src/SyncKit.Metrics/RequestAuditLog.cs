using System.Collections.Concurrent;
using SyncKit.Contract;

namespace SyncKit.Metrics;

public sealed class RequestAuditLog(TimeProvider clock, RequestMetricsOptions options) {
    private readonly AuditEntry?[] _recent = new AuditEntry[Math.Max(1, options.RecentCapacity)];
    private readonly ConcurrentDictionary<string, long> _paths = new();
    private readonly ConcurrentDictionary<string, IpAgg> _ips = new();
    private readonly long[] _buckets = new long[3];
    private readonly Lock _gate = new();
    private long _seq;
    private bool _keysCapped;

    public AuditEntry Record(string method, string path, int status, RequestBucket bucket, string ip, string? user) {
        var at = clock.GetUtcNow().ToUnixTimeSeconds();
        var entry = new AuditEntry(at, method, path, status, bucket, ip, user);

        lock (_gate) {
            _recent[_seq % _recent.Length] = entry;
            _seq++;
        }

        Interlocked.Increment(ref _buckets[(int)bucket]);
        Bump(_paths, path);
        _ips.AddOrUpdate(
            ip,
            _ => Capped(_ips) ? default : new IpAgg { Count = 1, LastSeen = at },
            (_, agg) => new IpAgg { Count = agg.Count + 1, LastSeen = at });
        return entry;
    }

    public IReadOnlyList<RecentRequest> Recent(int take) {
        var wanted = Math.Clamp(take, 0, _recent.Length);
        var result = new List<RecentRequest>(wanted);
        lock (_gate) {
            var count = (int)Math.Min(_seq, _recent.Length);
            for (var i = 0; i < count && result.Count < wanted; i++) {
                var idx = (_seq - 1 - i) % _recent.Length;
                var e = _recent[idx];
                if (e is null) continue;
                result.Add(new RecentRequest(e.AtEpochSeconds, e.Method, e.Path, e.Status, RequestBucketClassifier.ToName(e.Bucket), e.Ip));
            }
        }
        return result;
    }

    public IReadOnlyList<PathCount> Paths() =>
        _paths.Select(kv => new PathCount(kv.Key, kv.Value)).OrderByDescending(p => p.Count).ToList();

    public IReadOnlyList<IpCount> Ips() =>
        _ips.Where(kv => kv.Value.Count > 0)
            .Select(kv => new IpCount(kv.Key, kv.Value.Count, kv.Value.LastSeen))
            .OrderByDescending(p => p.Count).ToList();

    public BucketTotals Buckets() => new(
        Interlocked.Read(ref _buckets[0]),
        Interlocked.Read(ref _buckets[1]),
        Interlocked.Read(ref _buckets[2]),
        Volatile.Read(ref _keysCapped));

    private void Bump(ConcurrentDictionary<string, long> map, string key) {
        if (Capped(map) && !map.ContainsKey(key)) return;
        map.AddOrUpdate(key, 1, (_, v) => v + 1);
    }

    private bool Capped<TValue>(ConcurrentDictionary<string, TValue> map) {
        if (map.Count < options.MaxKeys) return false;
        Volatile.Write(ref _keysCapped, true);
        return true;
    }

    private struct IpAgg {
        public long Count;
        public long LastSeen;
    }
}
