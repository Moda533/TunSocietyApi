namespace TunSociety.Api.DTOs.Message;

public class CreateMessageRequest
{
    public Guid UserId { get; set; }
    public string Content { get; set; } = string.Empty;
}
