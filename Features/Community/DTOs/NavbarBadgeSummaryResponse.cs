namespace TunSociety.Api.DTOs.Community;

public class NavbarBadgeSummaryResponse
{
    public int UnreadNotificationsCount { get; set; }
    public bool HasUnreadMessages { get; set; }
    public bool HasPendingFriendRequests { get; set; }
}
