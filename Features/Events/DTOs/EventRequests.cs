using Microsoft.AspNetCore.Http;

namespace TunSociety.Api.DTOs.Events;

public class CreateEventRequest
{
    public Guid UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime StartsAtUtc { get; set; }
    public string Location { get; set; } = string.Empty;
    public IFormFile? Image { get; set; }
    public bool RemoveImage { get; set; }
}

public class UpdateEventRequest
{
    public Guid UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime StartsAtUtc { get; set; }
    public string Location { get; set; } = string.Empty;
    public IFormFile? Image { get; set; }
    public bool RemoveImage { get; set; }
}

public class UpdateEventParticipationRequest
{
    public Guid UserId { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class CreateEventCommentRequest
{
    public Guid UserId { get; set; }
    public string Content { get; set; } = string.Empty;
}

public class CreateEventEvaluationRequest
{
    public Guid UserId { get; set; }
    public int Rating { get; set; }
    public string? Feedback { get; set; }
}
