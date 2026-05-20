namespace TunSociety.Api.DTOs.Community;

public class DirectMessageResponse
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public string ConversationType { get; set; } = "Private";
    public Guid SenderUserId { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public Guid RecipientUserId { get; set; }
    public string RecipientName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public bool IsRead { get; set; }
}
