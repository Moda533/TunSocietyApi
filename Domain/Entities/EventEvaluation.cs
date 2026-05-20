namespace TunSociety.Api.Models;

public class EventEvaluation
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public CommunityEvent? Event { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public int Rating { get; set; }
    public string? Feedback { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
}
