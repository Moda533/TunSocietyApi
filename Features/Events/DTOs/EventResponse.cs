namespace TunSociety.Api.DTOs.Events;

public class EventResponse
{
    public Guid Id { get; set; }
    public Guid CreatedByUserId { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public string CreatedByRole { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime StartsAtUtc { get; set; }
    public string Location { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public Guid? ChatConversationId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public string? MyStatus { get; set; }
    public int GoingCount { get; set; }
    public int InterestedCount { get; set; }
    public int CommentsCount { get; set; }
    public double? AverageRating { get; set; }
    public int EvaluationCount { get; set; }
    public int? MyRating { get; set; }
    public List<EventParticipantResponse> Participants { get; set; } = [];
    public List<EventCommentResponse> Comments { get; set; } = [];
}

public class EventParticipantResponse
{
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
}

public class EventCommentResponse
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
