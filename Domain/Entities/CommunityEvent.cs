namespace TunSociety.Api.Models;

public class CommunityEvent
{
    public Guid Id { get; set; }
    public Guid CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime StartsAtUtc { get; set; }
    public string Location { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public Guid? ChatConversationId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }

    public ICollection<EventParticipant> Participants { get; set; } = new List<EventParticipant>();
    public ICollection<EventComment> Comments { get; set; } = new List<EventComment>();
    public ICollection<EventEvaluation> Evaluations { get; set; } = new List<EventEvaluation>();
}
