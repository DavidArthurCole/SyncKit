namespace SyncKit.Identity;

public sealed record AdminAllowlist(IReadOnlySet<string> Ids) {
    public static AdminAllowlist FromConfig(string? csv) {
        var ids = (csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
        return new AdminAllowlist(ids);
    }
}
