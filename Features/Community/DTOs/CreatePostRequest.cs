namespace TunSociety.Api.DTOs.Community;

public class CreatePostRequest
{
    public Guid UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string Visibility { get; set; } = "Public";
}
