namespace TunSociety.Api.DTOs.Admin;

public sealed class AdminStatisticsOverviewResponse
{
    public string Range { get; init; } = "30d";

    public AdminStatisticsSummaryResponse Summary { get; init; } = new();

    public List<AdminStatisticsTrendPointResponse> Trends { get; init; } = [];
}

public sealed class AdminStatisticsSummaryResponse
{
    public int TotalUsers { get; init; }

    public int ActiveUsers { get; init; }

    public int FlaggedContent { get; init; }

    public int BlockedUsers { get; init; }

    public int PendingAppeals { get; init; }

    public int ResolvedAppeals { get; init; }

    public int UnassignedMembers { get; init; }

    public double? AverageEventRating { get; init; }

    public int EventAttendanceCount { get; init; }

    public int EventEngagement { get; init; }

    public int EventEvaluationCount { get; init; }
}

public sealed class AdminStatisticsTrendPointResponse
{
    public string Date { get; init; } = string.Empty;

    public int Users { get; init; }

    public int Flagged { get; init; }

    public int Blocked { get; init; }

    public int AppealsSubmitted { get; init; }

    public int AppealsResolved { get; init; }

    public int EventEvaluations { get; init; }

    public double? EventAverageRating { get; init; }
}
