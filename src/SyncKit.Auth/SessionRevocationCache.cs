using System.Collections.Concurrent;

namespace SyncKit.Auth;

public sealed class SessionRevocationCache(TimeProvider clock, TimeSpan liveTtl) {
    private static readonly TimeSpan RevokedTtl = TimeSpan.FromMinutes(10);
    private const int SoftCap = 4096;

    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    public async Task<bool> IsRevokedAsync(string sid, Func<CancellationToken, Task<bool>> fetch, CancellationToken ct) {
        var now = clock.GetUtcNow();
        if (_entries.TryGetValue(sid, out var cached) && cached.Expires > now)
            return cached.Revoked;

        var revoked = await fetch(ct);
        _entries[sid] = new Entry(revoked, now + (revoked ? RevokedTtl : liveTtl));

        if (_entries.Count > SoftCap) Prune(now);
        return revoked;
    }

    private void Prune(DateTimeOffset now) {
        foreach (var pair in _entries)
            if (pair.Value.Expires <= now)
                _entries.TryRemove(pair.Key, out _);
    }

    private readonly record struct Entry(bool Revoked, DateTimeOffset Expires);
}
