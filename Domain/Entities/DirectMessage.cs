namespace TunSociety.Api.Models;

public class DirectMessage
{
    public Guid Id { get; set; }
    public Guid SenderUserId { get; set; }
    public User? SenderUser { get; set; }
    public Guid RecipientUserId { get; set; }
    public User? RecipientUser { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsRead { get; set; }
    public DateTime? ReadAtUtc { get; set; }
}
