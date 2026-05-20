namespace TunSociety.Api.DTOs.Admin;

public sealed class AdminEventEvaluationDashboardResponse
{
    public AdminEventEvaluationSummaryResponse Summary { get; init; } = new();

    public List<AdminEventEvaluationEventResponse> Events { get; init; } = [];

    public List<AdminEventEvaluationFeedbackResponse> RecentFeedback { get; init; } = [];
}

public sealed class AdminEventEvaluationSummaryResponse
{
    public int TotalEvaluations { get; init; }

    public double? AverageRating { get; init; }

    public int EventsWithEvaluations { get; init; }

    public int PastEventsWithoutEvaluations { get; init; }

    public int FeedbackCount { get; init; }
}

public sealed class AdminEventEvaluationEventResponse
{
    public Guid EventId { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Location { get; init; } = string.Empty;

    public DateTime StartsAtUtc { get; init; }

    public string CreatedByName { get; init; } = string.Empty;

    public int GoingCount { get; init; }

    public int InterestedCount { get; init; }

    public int CommentsCount { get; init; }

    public int EvaluationCount { get; init; }

    public double? AverageRating { get; init; }

    public int FeedbackCount { get; init; }

    public DateTime? LatestEvaluationAtUtc { get; init; }

    public List<int> RatingBreakdown { get; init; } = [];
}

public sealed class AdminEventEvaluationFeedbackResponse
{
    public Guid Id { get; init; }

    public Guid EventId { get; init; }

    public string EventTitle { get; init; } = string.Empty;

    public Guid UserId { get; init; }

    public string UserName { get; init; } = string.Empty;

    public int Rating { get; init; }

    public string Feedback { get; init; } = string.Empty;

    public DateTime CreatedAtUtc { get; init; }

    public DateTime? UpdatedAtUtc { get; init; }
}
