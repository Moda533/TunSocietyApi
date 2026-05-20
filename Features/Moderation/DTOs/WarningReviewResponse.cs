namespace TunSociety.Api.DTOs.Moderation;

public class WarningReviewResponse
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string UserDisplayName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime IssuedAtUtc { get; set; }
}
