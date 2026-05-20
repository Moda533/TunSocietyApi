namespace TunSociety.Api.Models;

public class GroupConversation
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string? Introduction { get; set; }
    public string? Notice { get; set; }
    public string CreateRoomPermission { get; set; } = "AdminsAndModerators";
    public string InviteCode { get; set; } = string.Empty;
    public Guid CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
    public Guid? SourceEventId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
    public DateTime? DeletedAtUtc { get; set; }

    public ICollection<GroupConversationMember> Members { get; set; } = new List<GroupConversationMember>();
    public ICollection<GroupMessage> Messages { get; set; } = new List<GroupMessage>();
}
