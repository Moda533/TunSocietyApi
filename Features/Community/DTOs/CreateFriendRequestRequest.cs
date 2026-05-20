namespace TunSociety.Api.DTOs.Community;

public class CreateFriendRequestRequest
{
    public Guid RequesterUserId { get; set; }
    public Guid RecipientUserId { get; set; }
    public string? Note { get; set; }
}
