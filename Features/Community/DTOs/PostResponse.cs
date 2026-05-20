namespace TunSociety.Api.DTOs.Community;

public class PostResponse
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public string RoleLabel { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string Visibility { get; set; } = "Public";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public PostReactionSummaryResponse Reactions { get; set; } = new();
    public List<PostCommentResponse> Comments { get; set; } = [];
}
