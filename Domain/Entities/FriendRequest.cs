namespace TunSociety.Api.Models;

public class FriendRequest
{
    public Guid Id { get; set; }
    public Guid RequesterUserId { get; set; }
    public User? RequesterUser { get; set; }
    public Guid RecipientUserId { get; set; }
    public User? RecipientUser { get; set; }
    public string Status { get; set; } = "Pending";
    public string? Note { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
}
