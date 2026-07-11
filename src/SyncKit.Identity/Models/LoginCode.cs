namespace SyncKit.Identity.Models;

public sealed class LoginCode {
    public string Code { get; set; } = "";
    public Guid UserId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RedeemedAt { get; set; }
}
