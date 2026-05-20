namespace TunSociety.Api.Models;

public class EventParticipant
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public CommunityEvent? Event { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public string Status { get; set; } = "Interested";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
}
