namespace TunSociety.Api.Models;

public class PostReaction
{
    public Guid Id { get; set; }
    public Guid PostId { get; set; }
    public Post? Post { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public string ReactionType { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
