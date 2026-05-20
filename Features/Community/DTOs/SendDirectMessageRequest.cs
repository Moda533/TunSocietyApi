namespace TunSociety.Api.DTOs.Community;

public class SendDirectMessageRequest
{
    public Guid SenderUserId { get; set; }
    public Guid RecipientUserId { get; set; }
    public string Content { get; set; } = string.Empty;
}
