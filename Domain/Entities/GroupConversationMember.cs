namespace TunSociety.Api.Models;

public class GroupConversationMember
{
    public Guid GroupConversationId { get; set; }
    public GroupConversation? GroupConversation { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public string Role { get; set; } = "Member";
    public string Status { get; set; } = "Pending";
    public bool IsArchived { get; set; }
    public bool IsMuted { get; set; }
    public bool IsPinned { get; set; }
    public Guid? LastReadMessageId { get; set; }
    public DateTime? LastReadAtUtc { get; set; }
    public Guid? InvitedByUserId { get; set; }
    public User? InvitedByUser { get; set; }
    public DateTime? InvitedAtUtc { get; set; }
    public DateTime JoinedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LeftAtUtc { get; set; }
    public DateTime? ClearedAtUtc { get; set; }
}
