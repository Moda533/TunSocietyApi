namespace TunSociety.Api.DTOs.Admin;

public class AdminOverviewResponse
{
    public int TotalUsers { get; set; }
    public int ActiveUsers { get; set; }
    public int PostsToday { get; set; }
    public int ReportsPending { get; set; }
    public int WarningsIssued { get; set; }
    public int FrozenAccounts { get; set; }
    public int NewUsersThisWeek { get; set; }
    public int UnassignedMembers { get; set; }
    public List<AdminTrendPointResponse> UserActivityTrend { get; set; } = [];
    public List<AdminTrendPointResponse> ModerationTrend { get; set; } = [];
}
