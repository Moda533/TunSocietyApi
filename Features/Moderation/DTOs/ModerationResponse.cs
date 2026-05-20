namespace TunSociety.Api.DTOs.Moderation;

public class ModerationResponse
{
    public Guid MessageId { get; set; }
    public double Score { get; set; }
    public List<string> Flags { get; set; } = [];
    public string Action { get; set; } = string.Empty;
    public string? Reason { get; set; }
}
