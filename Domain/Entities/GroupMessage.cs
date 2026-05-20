namespace TunSociety.Api.Models;

public class GroupMessage
{
    public Guid Id { get; set; }
    public Guid GroupConversationId { get; set; }
    public GroupConversation? GroupConversation { get; set; }
    public Guid SenderUserId { get; set; }
    public User? SenderUser { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
