namespace SyncKit.Identity.Models;

public sealed class Identity {
    public Guid UserId { get; set; }
    public string Provider { get; set; } = "";
    public string Subject { get; set; } = "";
    public string? Username { get; set; }
    public string? Avatar { get; set; }
    public DateTimeOffset LinkedAt { get; set; }
}
