namespace TunSociety.Api.DTOs.Community;

public class PostCommentResponse
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? ParentCommentId { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public List<Guid> MentionedUserIds { get; set; } = [];
    public List<PostCommentResponse> Replies { get; set; } = [];
    public int RepliesCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
