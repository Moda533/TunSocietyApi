using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TunSociety.Api.Data;
using TunSociety.Api.DTOs.Moderation;
using TunSociety.Api.Infrastructure;
using TunSociety.Api.Services;

namespace TunSociety.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ModerationController : AppControllerBase
{
    private static readonly string[] ReviewActions = ["Flag", "Block"];
    private static readonly string[] AllowActionAliases = ["Allow", "ALLOW", "allow", "Allowed", "ALLOWED", "allowed"];
    private static readonly string[] FlagActionAliases = ["Flag", "FLAG", "flag", "Flagged", "FLAGGED", "flagged"];
    private static readonly string[] BlockActionAliases = ["Block", "BLOCK", "block", "Blocked", "BLOCKED", "blocked"];
    private static readonly string[] ModerationReviewAuditActions = ["moderation.dismiss", "moderation.warn", "moderation.freeze", "moderation.escalate"];
    private static readonly string[] AppealStatuses = ["Open", "Accepted", "Rejected"];
    private static readonly JsonSerializerOptions MetadataJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ApplicationDbContext _dbContext;
    private readonly LocalAiService _localAiService;
    private readonly ModerationService _moderationService;
    private readonly AuditService _auditService;
    private readonly ILogger<ModerationController> _logger;

    public ModerationController(
        ApplicationDbContext dbContext,
        LocalAiService localAiService,
        ModerationService moderationService,
        AuditService auditService,
        ILogger<ModerationController> logger)
    {
        _dbContext = dbContext;
        _localAiService = localAiService;
        _moderationService = moderationService;
        _auditService = auditService;
        _logger = logger;
    }

    [Authorize(Policy = PermissionNames.ModerationReview)]
    [HttpPost("score")]
    public async Task<ActionResult<ModerationResponse>> Score(ModerationRequest request, CancellationToken cancellationToken)
    {
        var messageId = request.MessageId ?? Guid.NewGuid();
        var result = await _moderationService.EvaluateAsync(messageId, request.Content, request.ContentType, cancellationToken);

        return Ok(new ModerationResponse
        {
            MessageId = messageId,
            Score = result.Score,
            Flags = [.. result.Flags],
            Action = result.Action,
            Reason = result.Reason
        });
    }

    [Authorize(Policy = PermissionNames.ModerationReview)]
    [HttpPost]
    public async Task<ActionResult<LocalAiModerationResult>> Moderate(ModerationRequest request, CancellationToken cancellationToken)
    {
        var localResult = await _localAiService.ModerateAsync(
            request.Content,
            request.ContentType,
            cancellationToken);

        return Ok(localResult);
    }

    [Authorize(Policy = PermissionNames.ModerationReview)]
    [HttpGet("flagged-content")]
    public async Task<ActionResult<IEnumerable<FlaggedContentReviewResponse>>> GetFlaggedContent(
        [FromQuery] int take = 50,
        [FromQuery] string? action = null,
        [FromQuery] Guid? userId = null,
        [FromQuery] bool escalatedOnly = false,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 200);

        var normalizedAction = NormalizeReviewAction(action);
        if (action is not null && normalizedAction is null)
        {
            return BadRequest("Action must be Flag or Block.");
        }

        var query = _dbContext.ModerationResults
            .AsNoTracking()
            .Include(result => result.User)
            .Where(result => !AllowActionAliases.Contains(result.Action));

        if (normalizedAction is not null)
        {
            var actionAliases = GetReviewActionAliases(normalizedAction);
            query = query.Where(result => actionAliases.Contains(result.Action));
        }

        if (userId is Guid filteredUserId && filteredUserId != Guid.Empty)
        {
            query = query.Where(result => result.UserId == filteredUserId);
        }

        if (escalatedOnly)
        {
            query = query.Where(result => result.IsEscalated);
        }

        var records = await query
            .OrderByDescending(result => result.IsEscalated)
            .ThenByDescending(result => result.EscalatedAtUtc)
            .ThenByDescending(result => result.CreatedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

        var moderationResultIds = records.Select(result => result.Id.ToString()).ToList();
        var reviewLogs = moderationResultIds.Count == 0
            ? new List<Models.AuditLog>()
            : await _dbContext.AuditLogs
                .AsNoTracking()
                .Where(log =>
                    log.EntityType == nameof(Models.ModerationResult) &&
                    moderationResultIds.Contains(log.EntityId) &&
                    ModerationReviewAuditActions.Contains(log.Action))
                .OrderByDescending(log => log.CreatedAtUtc)
                .ToListAsync(cancellationToken);

        var latestReviewByModerationId = reviewLogs
            .GroupBy(log => log.EntityId)
            .ToDictionary(group => group.Key, group => group.First());

        var countsByAction = records
            .GroupBy(result => NormalizeStoredReviewAction(result.Action) ?? result.Action)
            .ToDictionary(group => group.Key, group => group.Count());

        _logger.LogInformation(
            "Loaded moderation queue. RequestedAction={RequestedAction} Take={Take} UserId={UserId} EscalatedOnly={EscalatedOnly} Returned={Returned} Counts={Counts}",
            normalizedAction ?? "All",
            take,
            userId,
            escalatedOnly,
            records.Count,
            string.Join(", ", countsByAction.Select(item => $"{item.Key}:{item.Value}")));

        var items = records
            .Select(result =>
            {
                var action = NormalizeStoredReviewAction(result.Action) ?? result.Action;
                latestReviewByModerationId.TryGetValue(result.Id.ToString(), out var latestReview);
                var reviewAction = NormalizeAuditReviewAction(latestReview?.Action);

                return new FlaggedContentReviewResponse
                {
                    ModerationResultId = result.Id,
                    ContentId = result.ContentId,
                    MessageId = result.ContentId,
                    ContentType = result.ContentType,
                    UserId = result.UserId,
                    UserDisplayName = result.User != null ? result.User.DisplayName : "Unknown",
                    UserEmail = result.User != null ? result.User.Email : string.Empty,
                    Content = result.ContentSnapshot,
                    Score = result.Score,
                    Action = action,
                    Reason = result.Reason,
                    IsEscalated = result.IsEscalated,
                    EscalatedAtUtc = result.EscalatedAtUtc,
                    EscalationNote = result.EscalationNote,
                    IsReviewed = latestReview is not null,
                    ReviewAction = reviewAction,
                    ReviewActionLabel = BuildReviewActionLabel(reviewAction, action),
                    ReviewNote = ExtractReviewNote(latestReview?.MetadataJson),
                    ReviewedAtUtc = latestReview?.CreatedAtUtc,
                    Flags = [.. result.Flags],
                    CreatedAtUtc = result.CreatedAtUtc
                };
            })
            .ToList();

        return Ok(items);
    }

    [Authorize(Policy = PermissionNames.ModerationReview)]
    [HttpGet("warnings")]
    public async Task<ActionResult<IEnumerable<WarningReviewResponse>>> GetWarnings(
        [FromQuery] int take = 50,
        [FromQuery] Guid? userId = null,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 200);

        var query = _dbContext.Warnings
            .AsNoTracking()
            .Include(warning => warning.User)
            .AsQueryable();

        if (userId is Guid filteredUserId && filteredUserId != Guid.Empty)
        {
            query = query.Where(warning => warning.UserId == filteredUserId);
        }

        var items = await query
            .OrderByDescending(warning => warning.IssuedAtUtc)
            .Take(take)
            .Select(warning => new WarningReviewResponse
            {
                Id = warning.Id,
                UserId = warning.UserId,
                UserDisplayName = warning.User != null ? warning.User.DisplayName : "Unknown",
                UserEmail = warning.User != null ? warning.User.Email : string.Empty,
                Reason = warning.Reason,
                IssuedAtUtc = warning.IssuedAtUtc
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [Authorize(Policy = PermissionNames.ModerationReview)]
    [HttpGet("freezes")]
    public async Task<ActionResult<IEnumerable<FreezeReviewResponse>>> GetFreezes(
        [FromQuery] int take = 50,
        [FromQuery] bool activeOnly = false,
        [FromQuery] Guid? userId = null,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 200);

        var query = _dbContext.Freezes
            .AsNoTracking()
            .Include(freeze => freeze.User)
            .AsQueryable();

        if (activeOnly)
        {
            query = query.Where(freeze => freeze.IsActive);
        }

        if (userId is Guid filteredUserId && filteredUserId != Guid.Empty)
        {
            query = query.Where(freeze => freeze.UserId == filteredUserId);
        }

        var items = await query
            .OrderByDescending(freeze => freeze.StartsAtUtc)
            .Take(take)
            .Select(freeze => new FreezeReviewResponse
            {
                Id = freeze.Id,
                UserId = freeze.UserId,
                UserDisplayName = freeze.User != null ? freeze.User.DisplayName : "Unknown",
                UserEmail = freeze.User != null ? freeze.User.Email : string.Empty,
                Reason = freeze.Reason,
                StartsAtUtc = freeze.StartsAtUtc,
                EndsAtUtc = freeze.EndsAtUtc,
                IsActive = freeze.IsActive
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [Authorize(Policy = PermissionNames.AppealsRead)]
    [HttpGet("appeals")]
    public async Task<ActionResult<IEnumerable<AppealReviewResponse>>> GetAppeals(
        [FromQuery] int take = 50,
        [FromQuery] string? status = null,
        [FromQuery] Guid? userId = null,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 200);

        var normalizedStatus = NormalizeAppealStatus(status);
        if (status is not null && normalizedStatus is null)
        {
            return BadRequest("Status must be Open, Accepted, or Rejected.");
        }

        var query = _dbContext.Appeals
            .AsNoTracking()
            .Include(appeal => appeal.User)
            .AsQueryable();

        if (normalizedStatus is not null)
        {
            query = query.Where(appeal => appeal.Status == normalizedStatus);
        }

        if (userId is Guid filteredUserId && filteredUserId != Guid.Empty)
        {
            query = query.Where(appeal => appeal.UserId == filteredUserId);
        }

        var items = await query
            .OrderByDescending(appeal => appeal.CreatedAtUtc)
            .Take(take)
            .Select(appeal => new AppealReviewResponse
            {
                Id = appeal.Id,
                UserId = appeal.UserId,
                UserDisplayName = appeal.User != null ? appeal.User.DisplayName : "Unknown",
                UserEmail = appeal.User != null ? appeal.User.Email : string.Empty,
                TargetType = appeal.TargetType,
                TargetId = appeal.TargetId,
                Status = appeal.Status,
                Reason = appeal.Reason,
                CreatedAtUtc = appeal.CreatedAtUtc,
                ResolvedAtUtc = appeal.ResolvedAtUtc
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [Authorize(Policy = PermissionNames.AppealsReview)]
    [HttpPut("appeals/{id:guid}/status")]
    public async Task<ActionResult<AppealReviewResponse>> UpdateAppealStatus(
        Guid id,
        UpdateAppealStatusRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedStatus = NormalizeAppealStatus(request.Status);
        if (normalizedStatus is null)
        {
            return BadRequest("Status must be Open, Accepted, or Rejected.");
        }

        var appeal = await _dbContext.Appeals
            .Include(current => current.User)
            .FirstOrDefaultAsync(current => current.Id == id, cancellationToken);

        if (appeal == null)
        {
            return NotFound("Appeal not found.");
        }

        appeal.Status = normalizedStatus;
        appeal.ResolvedAtUtc = normalizedStatus == "Open"
            ? null
            : DateTime.UtcNow;

        if (normalizedStatus == "Accepted")
        {
            await AcceptAppealAsync(appeal, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "appeal.review",
            nameof(Models.Appeal),
            appeal.Id.ToString(),
            $"status={appeal.Status}",
            CurrentUserId,
            cancellationToken);

        return Ok(new AppealReviewResponse
        {
            Id = appeal.Id,
            UserId = appeal.UserId,
            UserDisplayName = appeal.User?.DisplayName ?? "Unknown",
            UserEmail = appeal.User?.Email ?? string.Empty,
            TargetType = appeal.TargetType,
            TargetId = appeal.TargetId,
            Status = appeal.Status,
            Reason = appeal.Reason,
            CreatedAtUtc = appeal.CreatedAtUtc,
            ResolvedAtUtc = appeal.ResolvedAtUtc
        });
    }

    private static string? NormalizeReviewAction(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToLowerInvariant() switch
        {
            "flag" => "Flag",
            "flagged" => "Flag",
            "block" => "Block",
            "blocked" => "Block",
            _ => null
        };

        return normalized is not null && ReviewActions.Contains(normalized)
            ? normalized
            : null;
    }

    private static string[] GetReviewActionAliases(string action)
    {
        return action switch
        {
            "Flag" => FlagActionAliases,
            "Block" => BlockActionAliases,
            _ => []
        };
    }

    private static string? NormalizeStoredReviewAction(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "flag" or "flagged" => "Flag",
            "block" or "blocked" => "Block",
            _ => null
        };
    }

    private static string? NormalizeAuditReviewAction(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "moderation.dismiss" => "Dismiss",
            "moderation.warn" => "Warn",
            "moderation.freeze" => "Freeze",
            "moderation.escalate" => "Escalate",
            _ => null
        };
    }

    private static string? BuildReviewActionLabel(string? reviewAction, string contentAction)
    {
        return reviewAction switch
        {
            "Dismiss" => string.Equals(contentAction, "Block", StringComparison.OrdinalIgnoreCase)
                ? "Removed"
                : "Dismissed",
            "Warn" => "Warned",
            "Freeze" => "Frozen",
            "Escalate" => "Escalated",
            _ => null
        };
    }

    private static string? ExtractReviewNote(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        try
        {
            var metadata = JsonSerializer.Deserialize<Dictionary<string, string?>>(metadataJson, MetadataJsonOptions);
            return metadata != null && metadata.TryGetValue("reason", out var reason) && !string.IsNullOrWhiteSpace(reason)
                ? reason
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? NormalizeAppealStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToLowerInvariant() switch
        {
            "open" => "Open",
            "accepted" => "Accepted",
            "rejected" => "Rejected",
            _ => null
        };

        return normalized is not null && AppealStatuses.Contains(normalized)
            ? normalized
            : null;
    }

    private async Task AcceptAppealAsync(Models.Appeal appeal, CancellationToken cancellationToken)
    {
        if (!appeal.TargetType.Equals(nameof(Models.Freeze), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var freeze = await _dbContext.Freezes
            .FirstOrDefaultAsync(current => current.Id == appeal.TargetId && current.UserId == appeal.UserId, cancellationToken);

        if (freeze == null)
        {
            return;
        }

        freeze.IsActive = false;
        freeze.EndsAtUtc = DateTime.UtcNow;

        var hasActiveFreeze = await _dbContext.Freezes
            .AnyAsync(current => current.UserId == appeal.UserId && current.IsActive && current.Id != freeze.Id, cancellationToken);

        if (hasActiveFreeze)
        {
            return;
        }

        var user = appeal.User ?? await _dbContext.Users.FirstOrDefaultAsync(current => current.Id == appeal.UserId, cancellationToken);
        if (user != null)
        {
            user.IsFrozen = false;
        }
    }
}
