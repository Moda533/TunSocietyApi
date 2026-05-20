namespace TunSociety.Api.DTOs.Community;

public class PostReactionSummaryResponse
{
    public int Like { get; set; }
    public int Insightful { get; set; }
    public int Support { get; set; }
    public string? MyReaction { get; set; }
}
