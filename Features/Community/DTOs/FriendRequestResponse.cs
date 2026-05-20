namespace TunSociety.Api.DTOs.Community;

public class FriendRequestResponse
{
    public Guid Id { get; set; }
    public Guid RequesterUserId { get; set; }
    public string RequesterDisplayName { get; set; } = string.Empty;
    public string RequesterEmail { get; set; } = string.Empty;
    public Guid RecipientUserId { get; set; }
    public string RecipientDisplayName { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public string? Note { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}
