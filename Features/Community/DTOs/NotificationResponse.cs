namespace TunSociety.Api.DTOs.Community;

public class NotificationResponse
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Type { get; set; } = "System";
    public string? ActorDisplayName { get; set; }
    public string? ActorAvatarUrl { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }
    public Guid? RelatedPostId { get; set; }
    public Guid? RelatedCommentId { get; set; }
    public Guid? RelatedReplyId { get; set; }
    public Guid? RelatedGroupConversationId { get; set; }
    public string? ImageUrl { get; set; }
}
