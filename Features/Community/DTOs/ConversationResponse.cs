namespace TunSociety.Api.DTOs.Community;

public class ConversationResponse
{
    public Guid ConversationId { get; set; }
    public string ConversationType { get; set; } = "Private";
    public Guid PartnerUserId { get; set; }
    public string PartnerName { get; set; } = string.Empty;
    public string PartnerRole { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string? GroupIntroduction { get; set; }
    public string? GroupNotice { get; set; }
    public string CreateRoomPermission { get; set; } = "AdminsAndModerators";
    public string? InviteCode { get; set; }
    public string? CurrentUserRole { get; set; }
    public string? CurrentUserMembershipStatus { get; set; }
    public bool CurrentUserCanEditGroup { get; set; }
    public bool CurrentUserCanManageMembers { get; set; }
    public bool CurrentUserCanDeleteGroup { get; set; }
    public bool IsMuted { get; set; }
    public bool IsPinned { get; set; }
    public Guid? PartnerLastVisibleMessageId { get; set; }
    public DateTime LastMessageAtUtc { get; set; }
    public bool IsPartnerOnline { get; set; }
    public bool IsArchived { get; set; }
    public int MemberCount { get; set; }
    public int UnreadCount { get; set; }
    public List<GroupConversationMemberResponse> Members { get; set; } = [];
    public List<DirectMessageResponse> Messages { get; set; } = [];
}
