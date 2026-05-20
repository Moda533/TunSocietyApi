namespace TunSociety.Api.Models;

public class CommunityNotification
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public string Type { get; set; } = "System";
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAtUtc { get; set; }
    public Guid? RelatedPostId { get; set; }
    public Guid? RelatedCommentId { get; set; }
    public Guid? RelatedReplyId { get; set; }
    public Guid? RelatedGroupConversationId { get; set; }
    public string? ImageUrl { get; set; }
}
