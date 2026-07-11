namespace SyncKit.Identity.Models;

public sealed class User {
    public Guid UserId { get; set; }
    public string? DiscordId { get; set; }
    public string Username { get; set; } = "";
    public string? Avatar { get; set; }
    public string Role { get; set; } = "viewer";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastLoginAt { get; set; }
}
