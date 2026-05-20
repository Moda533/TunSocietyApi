namespace TunSociety.Api.DTOs.Community;

public class UpdateFriendRequestStatusRequest
{
    public Guid ActorUserId { get; set; }
    public string Status { get; set; } = "Pending";
}
