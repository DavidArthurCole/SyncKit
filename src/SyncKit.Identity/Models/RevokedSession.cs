namespace SyncKit.Identity.Models;

public sealed class RevokedSession {
    public string Sid { get; set; } = "";
    public DateTimeOffset RevokedAt { get; set; }
}
