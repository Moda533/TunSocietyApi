using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TunSociety.Api.Data;
using TunSociety.Api.DTOs.Common;
using TunSociety.Api.DTOs.Community;
using TunSociety.Api.DTOs.Moderation;
using TunSociety.Api.Models;
using TunSociety.Api.Services;

namespace TunSociety.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FriendRequestsController : AppControllerBase
{
    private static readonly string[] AllowedStatuses = ["Pending", "Accepted", "Declined"];

    private readonly ApplicationDbContext _dbContext;
    private readonly ModerationService _moderationService;
    private readonly SanctionService _sanctionService;
    private readonly AuditService _auditService;

    public FriendRequestsController(
        ApplicationDbContext dbContext,
        ModerationService moderationService,
        SanctionService sanctionService,
        AuditService auditService)
    {
        _dbContext = dbContext;
        _moderationService = moderationService;
        _sanctionService = sanctionService;
        _auditService = auditService;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<FriendRequestResponse>>> GetByUser(
        [FromQuery] Guid userId,
        [FromQuery] string? status,
        CancellationToken cancellationToken = default)
    {
        var accessError = EnsureCurrentUserMatches(userId);
        if (accessError is not null)
        {
            return accessError;
        }

        var currentUserId = CurrentUserId!.Value;

        var normalizedStatus = string.IsNullOrWhiteSpace(status)
            ? null
            : NormalizeStatus(status);

        if (status != null && normalizedStatus == null)
        {
            return BadRequest("Invalid status filter.");
        }

        var query = _dbContext.FriendRequests
            .AsNoTracking()
            .Include(request => request.RequesterUser)
            .Include(request => request.RecipientUser)
            .Where(request => request.RequesterUserId == currentUserId || request.RecipientUserId == currentUserId);

        if (normalizedStatus != null)
        {
            query = query.Where(request => request.Status == normalizedStatus);
        }

        var items = await query
            .OrderByDescending(request => request.CreatedAtUtc)
            .Take(200)
            .ToListAsync(cancellationToken);

        return Ok(items.Select(request => new FriendRequestResponse
        {
            Id = request.Id,
            RequesterUserId = request.RequesterUserId,
            RequesterDisplayName = request.RequesterUser!.DisplayName,
            RequesterEmail = request.RequesterUser.Email,
            RecipientUserId = request.RecipientUserId,
            RecipientDisplayName = request.RecipientUser!.DisplayName,
            Status = request.Status,
            Note = request.Note,
            CreatedAtUtc = request.CreatedAtUtc,
            UpdatedAtUtc = request.UpdatedAtUtc
        }).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<SubmissionResult<FriendRequestResponse>>> Create(
        CreateFriendRequestRequest request,
        CancellationToken cancellationToken)
    {
        var accessError = EnsureCurrentUserMatches(request.RequesterUserId);
        if (accessError is not null)
        {
            return accessError;
        }

        if (request.RecipientUserId == Guid.Empty)
        {
            return BadRequest("RecipientUserId is required.");
        }

        var currentUserId = CurrentUserId!.Value;
        if (currentUserId == request.RecipientUserId)
        {
            return BadRequest("You cannot send a request to yourself.");
        }

        var requester = await _dbContext.Users.FirstOrDefaultAsync(user => user.Id == currentUserId, cancellationToken);
        var recipient = await _dbContext.Users.FirstOrDefaultAsync(user => user.Id == request.RecipientUserId, cancellationToken);
        if (requester == null || recipient == null)
        {
            return NotFound("Requester or recipient user not found.");
        }

        var frozenError = EnsureActiveUser(requester);
        if (frozenError is not null)
        {
            return frozenError;
        }

        var exists = await _dbContext.FriendRequests.AnyAsync(
            current =>
                (current.RequesterUserId == currentUserId && current.RecipientUserId == request.RecipientUserId) ||
                (current.RequesterUserId == request.RecipientUserId && current.RecipientUserId == currentUserId),
            cancellationToken);

        if (exists)
        {
            return Conflict("Friend request already exists between these users.");
        }

        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        ModerationResult? noteModeration = null;
        SanctionOutcome? noteOutcome = null;

        if (!string.IsNullOrWhiteSpace(note))
        {
            noteModeration = await _moderationService.EvaluateAsync(Guid.NewGuid(), note, "MESSAGE", cancellationToken);
            if (noteModeration.Action != "Allow")
            {
                noteModeration.ContentType = nameof(FriendRequest);
                noteModeration.UserId = requester.Id;
                noteModeration.ContentSnapshot = note;
                _dbContext.ModerationResults.Add(noteModeration);

                noteOutcome = await _sanctionService.EvaluateAsync(requester, noteModeration, cancellationToken);
                if (noteOutcome.Warning != null)
                {
                    _dbContext.Warnings.Add(noteOutcome.Warning);
                }

                if (noteOutcome.Freeze != null)
                {
                    _dbContext.Freezes.Add(noteOutcome.Freeze);
                }

                note = null;
            }
        }

        var entity = new FriendRequest
        {
            Id = Guid.NewGuid(),
            RequesterUserId = currentUserId,
            RecipientUserId = request.RecipientUserId,
            Status = "Pending",
            Note = note,
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.FriendRequests.Add(entity);
        _dbContext.Notifications.Add(new CommunityNotification
        {
            Id = Guid.NewGuid(),
            UserId = request.RecipientUserId,
            Type = "Request",
            Title = "New friend request",
            Detail = $"{requester.DisplayName} sent you a friend request.",
            CreatedAtUtc = DateTime.UtcNow
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "friendrequest.create",
            nameof(FriendRequest),
            entity.Id.ToString(),
            noteModeration == null
                ? null
                : $"action={noteModeration.Action};score={noteModeration.Score:F3};flags={(noteModeration.Flags.Count == 0 ? "none" : string.Join(',', noteModeration.Flags))}",
            requester.Id,
            cancellationToken);

        return Ok(new SubmissionResult<FriendRequestResponse>
        {
            Data = new FriendRequestResponse
            {
                Id = entity.Id,
                RequesterUserId = entity.RequesterUserId,
                RequesterDisplayName = requester.DisplayName,
                RequesterEmail = requester.Email,
                RecipientUserId = entity.RecipientUserId,
                RecipientDisplayName = recipient.DisplayName,
                Status = entity.Status,
                Note = entity.Note,
                CreatedAtUtc = entity.CreatedAtUtc,
                UpdatedAtUtc = entity.UpdatedAtUtc
            },
            Moderation = noteModeration == null
                ? new ModerationFeedbackResponse
                {
                    Action = "Allow"
                }
                : ModerationFeedbackResponse.From(noteModeration, noteOutcome)
        });
    }

    [HttpPost("{id:guid}/status")]
    public async Task<ActionResult<FriendRequestResponse>> UpdateStatus(
        Guid id,
        UpdateFriendRequestStatusRequest request,
        CancellationToken cancellationToken)
    {
        var accessError = EnsureCurrentUserMatches(request.ActorUserId);
        if (accessError is not null)
        {
            return accessError;
        }

        var status = NormalizeStatus(request.Status);
        if (status == null || status == "Pending")
        {
            return BadRequest("Status must be Accepted or Declined.");
        }

        var entity = await _dbContext.FriendRequests
            .Include(current => current.RequesterUser)
            .Include(current => current.RecipientUser)
            .FirstOrDefaultAsync(current => current.Id == id, cancellationToken);

        if (entity == null)
        {
            return NotFound("Friend request not found.");
        }

        var currentUserId = CurrentUserId!.Value;
        if (entity.RecipientUserId != currentUserId)
        {
            return Forbid();
        }

        entity.Status = status;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        _dbContext.Notifications.Add(new CommunityNotification
        {
            Id = Guid.NewGuid(),
            UserId = entity.RequesterUserId,
            Type = "Request",
            Title = $"Friend request {status.ToLowerInvariant()}",
            Detail = $"{entity.RecipientUser?.DisplayName ?? "A user"} {status.ToLowerInvariant()} your request.",
            CreatedAtUtc = DateTime.UtcNow
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "friendrequest.update",
            nameof(FriendRequest),
            entity.Id.ToString(),
            $"status={status}",
            currentUserId,
            cancellationToken);

        return Ok(new FriendRequestResponse
        {
            Id = entity.Id,
            RequesterUserId = entity.RequesterUserId,
            RequesterDisplayName = entity.RequesterUser?.DisplayName ?? "Requester",
            RequesterEmail = entity.RequesterUser?.Email ?? string.Empty,
            RecipientUserId = entity.RecipientUserId,
            RecipientDisplayName = entity.RecipientUser?.DisplayName ?? "Recipient",
            Status = entity.Status,
            Note = entity.Note,
            CreatedAtUtc = entity.CreatedAtUtc,
            UpdatedAtUtc = entity.UpdatedAtUtc
        });
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Cancel(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (CurrentUserId is not Guid currentUserId)
        {
            return Unauthorized();
        }

        var entity = await _dbContext.FriendRequests
            .FirstOrDefaultAsync(current => current.Id == id, cancellationToken);

        if (entity == null)
        {
            return NotFound("Friend request not found.");
        }

        if (entity.RequesterUserId != currentUserId)
        {
            return Forbid();
        }

        if (!string.Equals(entity.Status, "Pending", StringComparison.OrdinalIgnoreCase))
        {
            return Conflict("Only pending friend requests can be cancelled.");
        }

        _dbContext.FriendRequests.Remove(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "friendrequest.cancel",
            nameof(FriendRequest),
            entity.Id.ToString(),
            null,
            currentUserId,
            cancellationToken);

        return NoContent();
    }

    private static string? NormalizeStatus(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToLowerInvariant();
        var mapped = normalized switch
        {
            "pending" => "Pending",
            "accepted" => "Accepted",
            "declined" => "Declined",
            _ => null
        };

        return mapped is not null && AllowedStatuses.Contains(mapped) ? mapped : null;
    }
}
