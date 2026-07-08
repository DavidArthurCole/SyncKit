namespace SyncKit.Identity;

// CSV of Discord snowflakes re-promoted to admin on every resolve. The one and only allowlist
// now that both EggIncognito and EggLedger resolve through this same service.
public sealed record AdminAllowlist(IReadOnlySet<string> Ids)
{
    public static AdminAllowlist FromConfig(string? csv)
    {
        var ids = (csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
        return new AdminAllowlist(ids);
    }
}
