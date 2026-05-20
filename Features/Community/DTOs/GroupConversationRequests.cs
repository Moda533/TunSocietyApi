using Microsoft.AspNetCore.Http;

namespace TunSociety.Api.DTOs.Community;

public class CreateGroupConversationRequest
{
    public Guid CreatorUserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public List<Guid> MemberUserIds { get; set; } = [];
}

public class SendGroupMessageRequest
{
    public Guid SenderUserId { get; set; }
    public string Content { get; set; } = string.Empty;
}

public class ArchiveConversationRequest
{
    public Guid UserId { get; set; }
    public bool IsArchived { get; set; } = true;
}

public class UpdateGroupProfileRequest
{
    public Guid ActorUserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string? Introduction { get; set; }
    public string? Notice { get; set; }
}

public class UploadGroupAvatarRequest
{
    public Guid ActorUserId { get; set; }
    public IFormFile? Avatar { get; set; }
}

public class AddGroupMembersRequest
{
    public Guid ActorUserId { get; set; }
    public List<Guid> MemberUserIds { get; set; } = [];
}

public class UpdateGroupMemberRoleRequest
{
    public Guid ActorUserId { get; set; }
    public string Role { get; set; } = "Member";
}

public class RemoveGroupMemberRequest
{
    public Guid ActorUserId { get; set; }
}

public class UpdateGroupMembershipRequest
{
    public Guid UserId { get; set; }
}

public class UpdateGroupPreferencesRequest
{
    public Guid UserId { get; set; }
    public bool? IsMuted { get; set; }
    public bool? IsPinned { get; set; }
}

public class UpdateGroupCreateRoomPermissionRequest
{
    public Guid ActorUserId { get; set; }
    public string CreateRoomPermission { get; set; } = "AdminsAndModerators";
}

public class ClearGroupChatRequest
{
    public Guid UserId { get; set; }
}

public class DeleteGroupConversationRequest
{
    public Guid ActorUserId { get; set; }
}

public class GroupConversationMemberResponse
{
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public string? AvatarUrl { get; set; }
    public bool IsCurrentUser { get; set; }
    public DateTime JoinedAtUtc { get; set; }
}
