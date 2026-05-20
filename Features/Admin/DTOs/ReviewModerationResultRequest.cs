namespace TunSociety.Api.DTOs.Admin;

public class ReviewModerationResultRequest
{
    public string Action { get; set; } = string.Empty;
    public string? Reason { get; set; }
}
