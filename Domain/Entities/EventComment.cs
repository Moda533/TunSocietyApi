namespace TunSociety.Api.Models;

public class EventComment
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public CommunityEvent? Event { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
