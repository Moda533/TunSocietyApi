using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TunSociety.Api.Data;
using TunSociety.Api.DTOs.Common;
using TunSociety.Api.DTOs.Events;
using TunSociety.Api.DTOs.Moderation;
using TunSociety.Api.Infrastructure;
using TunSociety.Api.Models;
using TunSociety.Api.Services;

namespace TunSociety.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class EventsController : AppControllerBase
{
    private static readonly string[] ParticipationStatuses = ["Going", "Interested"];
    private const string GoingInterestedStatus = "GoingInterested";
    private readonly ApplicationDbContext _dbContext;
    private readonly ModerationService _moderationService;
    private readonly SanctionService _sanctionService;
    private readonly AuditService _auditService;
    private readonly EventImageStorageService _eventImageStorageService;

    public EventsController(
        ApplicationDbContext dbContext,
        ModerationService moderationService,
        SanctionService sanctionService,
        AuditService auditService,
        EventImageStorageService eventImageStorageService)
    {
        _dbContext = dbContext;
        _moderationService = moderationService;
        _sanctionService = sanctionService;
        _auditService = auditService;
        _eventImageStorageService = eventImageStorageService;
    }

    [Authorize(Policy = PermissionNames.EventsRead)]
    [HttpGet]
    public async Task<ActionResult<IEnumerable<EventResponse>>> GetEvents(
        [FromQuery] Guid userId,
        [FromQuery] int take = 30,
        CancellationToken cancellationToken = default)
    {
        var accessError = EnsureCurrentUserMatches(userId);
        if (accessError is not null)
        {
            return accessError;
        }

        take = Math.Clamp(take, 1, 100);
        var currentUserId = CurrentUserId!.Value;

        var events = await _dbContext.Events
            .AsNoTracking()
            .Include(item => item.CreatedByUser)
            .Include(item => item.Participants)
                .ThenInclude(item => item.User)
            .Include(item => item.Comments)
                .ThenInclude(item => item.User)
            .Include(item => item.Evaluations)
            .OrderByDescending(item => item.StartsAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

        return Ok(events.Select(item => MapEvent(item, currentUserId)).ToList());
    }

    [Authorize(Policy = PermissionNames.EventsRead)]
    [HttpGet("{eventId:guid}")]
    public async Task<ActionResult<EventResponse>> GetById(
        Guid eventId,
        [FromQuery] Guid userId,
        CancellationToken cancellationToken = default)
    {
        var accessError = EnsureCurrentUserMatches(userId);
        if (accessError is not null)
        {
            return accessError;
        }

        var item = await LoadEventGraphAsync(eventId, cancellationToken);
        if (item == null)
        {
            return NotFound("Event not found.");
        }

        return Ok(MapEvent(item, CurrentUserId!.Value));
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    [Authorize(Policy = PermissionNames.EventsManage)]
    public async Task<ActionResult<SubmissionResult<EventResponse>>> Create(
        [FromForm] CreateEventRequest request,
        CancellationToken cancellationToken)
    {
        var accessError = EnsureCurrentUserMatches(request.UserId);
        if (accessError is not null)
        {
            return accessError;
        }

        var validationError = ValidateEventInput(request.Title, request.Description, request.Location, request.StartsAtUtc);
        if (validationError is not null)
        {
            return validationError;
        }

        var currentUserId = CurrentUserId!.Value;
        var user = await _dbContext.Users.FirstOrDefaultAsync(item => item.Id == currentUserId, cancellationToken);
        if (user == null)
        {
            return NotFound("User not found.");
        }

        var frozenError = EnsureActiveUser(user);
        if (frozenError is not null)
        {
            return frozenError;
        }

        var eventId = Guid.NewGuid();
        var title = request.Title.Trim();
        var description = request.Description.Trim();
        var location = request.Location.Trim();
        var moderationSnapshot = $"Title: {title}{Environment.NewLine}Description: {description}{Environment.NewLine}Location: {location}";
        var moderation = await _moderationService.EvaluateAsync(eventId, moderationSnapshot, "EVENT", cancellationToken);
        moderation.ContentType = nameof(CommunityEvent);
        moderation.UserId = currentUserId;
        moderation.ContentSnapshot = moderationSnapshot;
        _dbContext.ModerationResults.Add(moderation);

        var outcome = await _sanctionService.EvaluateAsync(user, moderation, cancellationToken);
        if (outcome.Warning != null)
        {
            _dbContext.Warnings.Add(outcome.Warning);
        }

        if (outcome.Freeze != null)
        {
            _dbContext.Freezes.Add(outcome.Freeze);
        }

        CommunityEvent? created = null;
        string? uploadedImageUrl = null;
        try
        {
            if (moderation.Action == "Allow")
            {
                uploadedImageUrl = await SaveEventImageAsync(eventId, request.Image, cancellationToken);
                created = new CommunityEvent
                {
                    Id = eventId,
                    CreatedByUserId = currentUserId,
                    Title = title,
                    Description = description,
                    StartsAtUtc = NormalizeUtc(request.StartsAtUtc),
                    Location = location,
                    ImageUrl = uploadedImageUrl,
                    CreatedAtUtc = DateTime.UtcNow
                };

                _dbContext.Events.Add(created);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            _eventImageStorageService.DeleteManagedEventImage(uploadedImageUrl);
            return BadRequest(ex.Message);
        }
        catch
        {
            _eventImageStorageService.DeleteManagedEventImage(uploadedImageUrl);
            throw;
        }

        await _auditService.LogAsync(
            "event.create",
            nameof(CommunityEvent),
            created?.Id.ToString() ?? eventId.ToString(),
            $"action={moderation.Action};score={moderation.Score:F3}",
            currentUserId,
            cancellationToken);

        return Ok(new SubmissionResult<EventResponse>
        {
            Data = created == null ? null : MapEvent(created, currentUserId, user.DisplayName, user.Role),
            Moderation = ModerationFeedbackResponse.From(moderation, outcome)
        });
    }

    [HttpPut("{eventId:guid}")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    [Authorize(Policy = PermissionNames.EventsManage)]
    public async Task<ActionResult<SubmissionResult<EventResponse>>> Update(
        Guid eventId,
        [FromForm] UpdateEventRequest request,
        CancellationToken cancellationToken)
    {
        var accessError = EnsureCurrentUserMatches(request.UserId);
        if (accessError is not null)
        {
            return accessError;
        }

        var validationError = ValidateEventInput(request.Title, request.Description, request.Location, request.StartsAtUtc);
        if (validationError is not null)
        {
            return validationError;
        }

        var currentUserId = CurrentUserId!.Value;
        var user = await _dbContext.Users.FirstOrDefaultAsync(item => item.Id == currentUserId, cancellationToken);
        if (user == null)
        {
            return NotFound("User not found.");
        }

        var eventItem = await LoadEventGraphAsync(eventId, cancellationToken);
        if (eventItem == null)
        {
            return NotFound("Event not found.");
        }

        var title = request.Title.Trim();
        var description = request.Description.Trim();
        var location = request.Location.Trim();
        var moderationSnapshot = $"Title: {title}{Environment.NewLine}Description: {description}{Environment.NewLine}Location: {location}";
        var moderation = await _moderationService.EvaluateAsync(eventItem.Id, moderationSnapshot, "EVENT", cancellationToken);
        moderation.ContentType = nameof(CommunityEvent);
        moderation.UserId = currentUserId;
        moderation.ContentSnapshot = moderationSnapshot;
        _dbContext.ModerationResults.Add(moderation);

        var outcome = await _sanctionService.EvaluateAsync(user, moderation, cancellationToken);
        if (outcome.Warning != null)
        {
            _dbContext.Warnings.Add(outcome.Warning);
        }

        if (outcome.Freeze != null)
        {
            _dbContext.Freezes.Add(outcome.Freeze);
        }

        var previousImageUrl = eventItem.ImageUrl;
        string? uploadedImageUrl = null;
        try
        {
            if (moderation.Action == "Allow")
            {
                uploadedImageUrl = await SaveEventImageAsync(eventItem.Id, request.Image, cancellationToken);

                eventItem.Title = title;
                eventItem.Description = description;
                eventItem.StartsAtUtc = NormalizeUtc(request.StartsAtUtc);
                eventItem.Location = location;
                eventItem.ImageUrl = uploadedImageUrl ?? (request.RemoveImage ? null : previousImageUrl);
                eventItem.UpdatedAtUtc = DateTime.UtcNow;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            _eventImageStorageService.DeleteManagedEventImage(uploadedImageUrl);
            return BadRequest(ex.Message);
        }
        catch
        {
            _eventImageStorageService.DeleteManagedEventImage(uploadedImageUrl);
            throw;
        }

        if (moderation.Action == "Allow" && eventItem.ImageUrl != previousImageUrl)
        {
            _eventImageStorageService.DeleteManagedEventImage(previousImageUrl);
        }

        await _auditService.LogAsync(
            "event.update",
            nameof(CommunityEvent),
            eventItem.Id.ToString(),
            $"action={moderation.Action};score={moderation.Score:F3}",
            currentUserId,
            cancellationToken);

        return Ok(new SubmissionResult<EventResponse>
        {
            Data = moderation.Action == "Allow" ? MapEvent(eventItem, currentUserId) : null,
            Moderation = ModerationFeedbackResponse.From(moderation, outcome)
        });
    }

    [HttpDelete("{eventId:guid}")]
    [Authorize(Policy = PermissionNames.EventsManage)]
    public async Task<ActionResult> Delete(
        Guid eventId,
        [FromQuery] Guid userId,
        CancellationToken cancellationToken)
    {
        var accessError = EnsureCurrentUserMatches(userId);
        if (accessError is not null)
        {
            return accessError;
        }

        var eventItem = await _dbContext.Events.FirstOrDefaultAsync(item => item.Id == eventId, cancellationToken);
        if (eventItem == null)
        {
            return NotFound("Event not found.");
        }

        var imageUrl = eventItem.ImageUrl;
        _dbContext.Events.Remove(eventItem);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _eventImageStorageService.DeleteManagedEventImage(imageUrl);

        await _auditService.LogAsync(
            "event.delete",
            nameof(CommunityEvent),
            eventId.ToString(),
            "deleted=true",
            CurrentUserId,
            cancellationToken);

        return NoContent();
    }

    [Authorize(Policy = PermissionNames.EventsRead)]
    [HttpPost("{eventId:guid}/participation")]
    public async Task<ActionResult<EventResponse>> UpdateParticipation(
        Guid eventId,
        UpdateEventParticipationRequest request,
        CancellationToken cancellationToken)
    {
        var accessError = EnsureCurrentUserMatches(request.UserId);
        if (accessError is not null)
        {
            return accessError;
        }

        var status = NormalizeParticipationStatus(request.Status);
        if (status == null)
        {
            return BadRequest("Status must be Going, Interested, or None.");
        }

        var currentUserId = CurrentUserId!.Value;
        var eventItem = await LoadEventGraphAsync(eventId, cancellationToken);
        if (eventItem == null)
        {
            return NotFound("Event not found.");
        }

        var existing = await _dbContext.EventParticipants
            .FirstOrDefaultAsync(item => item.EventId == eventId && item.UserId == currentUserId, cancellationToken);

        if (status == "None")
        {
            if (existing != null)
            {
                _dbContext.EventParticipants.Remove(existing);
            }
        }
        else
        {
            var isGoing = existing != null && HasGoingStatus(existing.Status);
            var isInterested = existing != null && HasInterestedStatus(existing.Status);

            if (status == "Going")
            {
                isGoing = !isGoing;
            }
            else
            {
                isInterested = !isInterested;
            }

            var nextStatus = BuildParticipationStatus(isGoing, isInterested);
            if (nextStatus == null)
            {
                if (existing != null)
                {
                    _dbContext.EventParticipants.Remove(existing);
                }
            }
            else if (existing == null)
            {
                _dbContext.EventParticipants.Add(new EventParticipant
                {
                    Id = Guid.NewGuid(),
                    EventId = eventId,
                    UserId = currentUserId,
                    Status = nextStatus,
                    CreatedAtUtc = DateTime.UtcNow
                });
            }
            else
            {
                existing.Status = nextStatus;
                existing.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "event.participation",
            nameof(EventParticipant),
            eventId.ToString(),
            $"status={status}",
            currentUserId,
            cancellationToken);

        var refreshed = await LoadEventGraphAsync(eventId, cancellationToken);
        return refreshed == null ? NotFound("Event not found after update.") : Ok(MapEvent(refreshed, currentUserId));
    }

    [Authorize(Policy = PermissionNames.EventsRead)]
    [HttpPost("{eventId:guid}/comments")]
    public async Task<ActionResult<SubmissionResult<EventResponse>>> AddComment(
        Guid eventId,
        CreateEventCommentRequest request,
        CancellationToken cancellationToken)
    {
        var accessError = EnsureCurrentUserMatches(request.UserId);
        if (accessError is not null)
        {
            return accessError;
        }

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return BadRequest("Comment content is required.");
        }

        var currentUserId = CurrentUserId!.Value;
        var user = await _dbContext.Users.FirstOrDefaultAsync(item => item.Id == currentUserId, cancellationToken);
        if (user == null)
        {
            return NotFound("User not found.");
        }

        var frozenError = EnsureActiveUser(user);
        if (frozenError is not null)
        {
            return frozenError;
        }

        var eventExists = await _dbContext.Events.AnyAsync(item => item.Id == eventId, cancellationToken);
        if (!eventExists)
        {
            return NotFound("Event not found.");
        }

        var commentId = Guid.NewGuid();
        var rawContent = request.Content.Trim();
        var moderation = await _moderationService.EvaluateAsync(commentId, rawContent, "COMMENT", cancellationToken);
        moderation.ContentType = nameof(EventComment);
        moderation.UserId = currentUserId;
        moderation.ContentSnapshot = rawContent;
        _dbContext.ModerationResults.Add(moderation);

        var outcome = await _sanctionService.EvaluateAsync(user, moderation, cancellationToken);
        if (outcome.Warning != null)
        {
            _dbContext.Warnings.Add(outcome.Warning);
        }

        if (outcome.Freeze != null)
        {
            _dbContext.Freezes.Add(outcome.Freeze);
        }

        if (moderation.Action == "Allow")
        {
            _dbContext.EventComments.Add(new EventComment
            {
                Id = commentId,
                EventId = eventId,
                UserId = currentUserId,
                Content = rawContent,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "event.comment",
            nameof(EventComment),
            commentId.ToString(),
            $"eventId={eventId};action={moderation.Action};score={moderation.Score:F3}",
            currentUserId,
            cancellationToken);

        var refreshed = await LoadEventGraphAsync(eventId, cancellationToken);
        return Ok(new SubmissionResult<EventResponse>
        {
            Data = moderation.Action == "Allow" && refreshed != null ? MapEvent(refreshed, currentUserId) : null,
            Moderation = ModerationFeedbackResponse.From(moderation, outcome)
        });
    }

    [Authorize(Policy = PermissionNames.EventsRead)]
    [HttpPost("{eventId:guid}/evaluations")]
    public async Task<ActionResult<EventResponse>> Evaluate(
        Guid eventId,
        CreateEventEvaluationRequest request,
        CancellationToken cancellationToken)
    {
        var accessError = EnsureCurrentUserMatches(request.UserId);
        if (accessError is not null)
        {
            return accessError;
        }

        if (request.Rating is < 1 or > 5)
        {
            return BadRequest("Rating must be between 1 and 5.");
        }

        var currentUserId = CurrentUserId!.Value;
        var eventItem = await LoadEventGraphAsync(eventId, cancellationToken);
        if (eventItem == null)
        {
            return NotFound("Event not found.");
        }

        if (eventItem.StartsAtUtc > DateTime.UtcNow)
        {
            return BadRequest("Events can only be evaluated after they start.");
        }

        var existing = await _dbContext.EventEvaluations
            .FirstOrDefaultAsync(item => item.EventId == eventId && item.UserId == currentUserId, cancellationToken);

        if (existing == null)
        {
            _dbContext.EventEvaluations.Add(new EventEvaluation
            {
                Id = Guid.NewGuid(),
                EventId = eventId,
                UserId = currentUserId,
                Rating = request.Rating,
                Feedback = string.IsNullOrWhiteSpace(request.Feedback) ? null : request.Feedback.Trim(),
                CreatedAtUtc = DateTime.UtcNow
            });
        }
        else
        {
            existing.Rating = request.Rating;
            existing.Feedback = string.IsNullOrWhiteSpace(request.Feedback) ? null : request.Feedback.Trim();
            existing.UpdatedAtUtc = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "event.evaluate",
            nameof(EventEvaluation),
            eventId.ToString(),
            $"rating={request.Rating}",
            currentUserId,
            cancellationToken);

        var refreshed = await LoadEventGraphAsync(eventId, cancellationToken);
        return refreshed == null ? NotFound("Event not found after update.") : Ok(MapEvent(refreshed, currentUserId));
    }

    private async Task<CommunityEvent?> LoadEventGraphAsync(Guid eventId, CancellationToken cancellationToken)
    {
        return await _dbContext.Events
            .Include(item => item.CreatedByUser)
            .Include(item => item.Participants)
                .ThenInclude(item => item.User)
            .Include(item => item.Comments)
                .ThenInclude(item => item.User)
            .Include(item => item.Evaluations)
            .FirstOrDefaultAsync(item => item.Id == eventId, cancellationToken);
    }

    private ActionResult? ValidateEventInput(string title, string description, string location, DateTime startsAtUtc)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(description))
        {
            return BadRequest("Title and description are required.");
        }

        if (string.IsNullOrWhiteSpace(location))
        {
            return BadRequest("Location is required.");
        }

        if (startsAtUtc == default)
        {
            return BadRequest("Event date and time are required.");
        }

        return null;
    }

    private async Task<string?> SaveEventImageAsync(Guid eventId, IFormFile? image, CancellationToken cancellationToken)
    {
        if (image == null || image.Length == 0)
        {
            return null;
        }

        return await _eventImageStorageService.SaveEventImageAsync(eventId, image, cancellationToken);
    }

    private static DateTime NormalizeUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    private static string? NormalizeParticipationStatus(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized == "none" || normalized == "notgoing" || normalized == "clear")
        {
            return "None";
        }

        return ParticipationStatuses.FirstOrDefault(item => item.Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static string? BuildParticipationStatus(bool isGoing, bool isInterested)
    {
        if (isGoing && isInterested)
        {
            return GoingInterestedStatus;
        }

        if (isGoing)
        {
            return "Going";
        }

        return isInterested ? "Interested" : null;
    }

    private static bool HasGoingStatus(string? status)
    {
        return string.Equals(status, "Going", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, GoingInterestedStatus, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasInterestedStatus(string? status)
    {
        return string.Equals(status, "Interested", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, GoingInterestedStatus, StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatParticipationStatus(string status)
    {
        return string.Equals(status, GoingInterestedStatus, StringComparison.OrdinalIgnoreCase)
            ? "Going and Interested"
            : status;
    }

    private static EventResponse MapEvent(
        CommunityEvent eventItem,
        Guid currentUserId,
        string? createdByName = null,
        string? createdByRole = null)
    {
        var participants = eventItem.Participants
            .OrderBy(item => HasGoingStatus(item.Status) ? 0 : 1)
            .ThenBy(item => item.User?.DisplayName ?? item.User?.UserName)
            .Select(item => new EventParticipantResponse
            {
                UserId = item.UserId,
                DisplayName = item.User?.DisplayName ?? item.User?.UserName ?? "Member",
                Role = item.User?.Role ?? "User",
                Status = FormatParticipationStatus(item.Status),
                UpdatedAtUtc = item.UpdatedAtUtc ?? item.CreatedAtUtc
            })
            .ToList();

        var comments = eventItem.Comments
            .OrderBy(item => item.CreatedAtUtc)
            .Select(item => new EventCommentResponse
            {
                Id = item.Id,
                UserId = item.UserId,
                AuthorName = item.User?.DisplayName ?? item.User?.UserName ?? "Member",
                Content = item.Content,
                CreatedAtUtc = item.CreatedAtUtc
            })
            .ToList();

        var evaluations = eventItem.Evaluations.ToList();

        return new EventResponse
        {
            Id = eventItem.Id,
            CreatedByUserId = eventItem.CreatedByUserId,
            CreatedByName = createdByName ?? eventItem.CreatedByUser?.DisplayName ?? eventItem.CreatedByUser?.UserName ?? "Moderator",
            CreatedByRole = createdByRole ?? eventItem.CreatedByUser?.Role ?? "Moderator",
            Title = eventItem.Title,
            Description = eventItem.Description,
            StartsAtUtc = eventItem.StartsAtUtc,
            Location = eventItem.Location,
            ImageUrl = eventItem.ImageUrl,
            ChatConversationId = eventItem.ChatConversationId,
            CreatedAtUtc = eventItem.CreatedAtUtc,
            UpdatedAtUtc = eventItem.UpdatedAtUtc,
            MyStatus = eventItem.Participants.FirstOrDefault(item => item.UserId == currentUserId)?.Status,
            GoingCount = eventItem.Participants.Count(item => HasGoingStatus(item.Status)),
            InterestedCount = eventItem.Participants.Count(item => HasInterestedStatus(item.Status)),
            CommentsCount = comments.Count,
            AverageRating = evaluations.Count == 0 ? null : Math.Round(evaluations.Average(item => item.Rating), 2),
            EvaluationCount = evaluations.Count,
            MyRating = evaluations.FirstOrDefault(item => item.UserId == currentUserId)?.Rating,
            Participants = participants,
            Comments = comments
        };
    }
}
