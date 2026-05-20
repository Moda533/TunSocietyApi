namespace TunSociety.Api.DTOs.Community;

public class UpdateConversationReadCursorRequest
{
    public Guid UserId { get; set; }
    public Guid LastVisibleMessageId { get; set; }
}
