namespace TunSociety.Api.DTOs.Community;

public class ReactToPostRequest
{
    public Guid UserId { get; set; }
    public string ReactionType { get; set; } = string.Empty;
}
