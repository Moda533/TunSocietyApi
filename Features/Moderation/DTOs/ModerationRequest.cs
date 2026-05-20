namespace TunSociety.Api.DTOs.Moderation;

public class ModerationRequest
{
    public Guid? MessageId { get; set; }
    public string Content { get; set; } = string.Empty;
    public string ContentType { get; set; } = "GENERIC";
}
