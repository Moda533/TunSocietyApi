namespace TunSociety.Api.DTOs.Community;

public class CreateNotificationRequest
{
    public Guid UserId { get; set; }
    public string Type { get; set; } = "System";
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public Guid? RelatedPostId { get; set; }
    public Guid? RelatedCommentId { get; set; }
    public Guid? RelatedReplyId { get; set; }
    public Guid? RelatedGroupConversationId { get; set; }
    public string? ImageUrl { get; set; }
}
