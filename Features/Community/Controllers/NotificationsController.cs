using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TunSociety.Api.Data;
using TunSociety.Api.DTOs.Community;
using TunSociety.Api.Infrastructure;
using TunSociety.Api.Models;
using TunSociety.Api.Services;

namespace TunSociety.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NotificationsController : AppControllerBase
{
    private static readonly string[] VisibleNotificationTypes =
    [
        "Reaction",
        "Comment",
        "CommentReply",
        "CommentMention",
        "ReplyMention",
        "Request"
    ];

    private readonly ApplicationDbContext _dbContext;
    private readonly AuditService _auditService;

    public NotificationsController(ApplicationDbContext dbContext, AuditService auditService)
    {
        _dbContext = dbContext;
        _auditService = auditService;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<NotificationResponse>>> GetByUser(
        [FromQuery] Guid userId,
        [FromQuery] bool includeRead = true,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        var accessError = EnsureCurrentUserMatches(userId);
        if (accessError is not null)
        {
            return accessError;
        }

        var currentUserId = CurrentUserId!.Value;

        take = Math.Clamp(take, 1, 200);

        var query = _dbContext.Notifications
            .AsNoTracking()
            .Where(notification =>
                notification.UserId == currentUserId &&
                VisibleNotificationTypes.Contains(notification.Type));

        if (!includeRead)
        {
            query = query.Where(notification => !notification.IsRead);
        }

        var items = await query
            .OrderByDescending(notification => notification.CreatedAtUtc)
            .Take(take)
            .Select(notification => new NotificationResponse
            {
                Id = notification.Id,
                UserId = notification.UserId,
                Type = notification.Type,
                Title = notification.Title,
                Detail = notification.Detail,
                IsRead = notification.IsRead,
                CreatedAtUtc = notification.CreatedAtUtc,
                ReadAtUtc = notification.ReadAtUtc,
                RelatedPostId = notification.RelatedPostId,
                RelatedCommentId = notification.RelatedCommentId,
                RelatedReplyId = notification.RelatedReplyId,
                RelatedGroupConversationId = notification.RelatedGroupConversationId,
                ImageUrl = notification.ImageUrl
            })
            .ToListAsync(cancellationToken);

        await EnrichActorsAsync(items, cancellationToken);

        return Ok(items);
    }

    [Authorize(Policy = PermissionNames.ModerationReview)]
    [HttpPost]
    public async Task<ActionResult<NotificationResponse>> Create(
        CreateNotificationRequest request,
        CancellationToken cancellationToken)
    {
        if (CurrentUserId is not Guid currentUserId)
        {
            return Unauthorized();
        }

        if (request.UserId == Guid.Empty)
        {
            return BadRequest("userId is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Detail))
        {
            return BadRequest("Title and detail are required.");
        }

        var userExists = await _dbContext.Users.AnyAsync(user => user.Id == request.UserId, cancellationToken);
        if (!userExists)
        {
            return NotFound("User not found.");
        }

        var entity = new CommunityNotification
        {
            Id = Guid.NewGuid(),
            UserId = request.UserId,
            Type = string.IsNullOrWhiteSpace(request.Type) ? "System" : request.Type.Trim(),
            Title = request.Title.Trim(),
            Detail = request.Detail.Trim(),
            RelatedPostId = request.RelatedPostId,
            RelatedCommentId = request.RelatedCommentId,
            RelatedReplyId = request.RelatedReplyId,
            RelatedGroupConversationId = request.RelatedGroupConversationId,
            ImageUrl = string.IsNullOrWhiteSpace(request.ImageUrl) ? null : request.ImageUrl.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.Notifications.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "notification.create",
            nameof(CommunityNotification),
            entity.Id.ToString(),
            $"type={entity.Type}",
            currentUserId,
            cancellationToken);

        var response = new NotificationResponse
        {
            Id = entity.Id,
            UserId = entity.UserId,
            Type = entity.Type,
            Title = entity.Title,
            Detail = entity.Detail,
            IsRead = entity.IsRead,
            CreatedAtUtc = entity.CreatedAtUtc,
            ReadAtUtc = entity.ReadAtUtc,
            RelatedPostId = entity.RelatedPostId,
            RelatedCommentId = entity.RelatedCommentId,
            RelatedReplyId = entity.RelatedReplyId,
            RelatedGroupConversationId = entity.RelatedGroupConversationId,
            ImageUrl = entity.ImageUrl
        };
        await EnrichActorsAsync([response], cancellationToken);

        return Ok(response);
    }

    [HttpPost("{id:guid}/read")]
    public async Task<ActionResult<NotificationResponse>> MarkRead(
        Guid id,
        MarkNotificationReadRequest request,
        CancellationToken cancellationToken)
    {
        var accessError = EnsureCurrentUserMatches(request.UserId);
        if (accessError is not null)
        {
            return accessError;
        }

        var currentUserId = CurrentUserId!.Value;
        var entity = await _dbContext.Notifications.FirstOrDefaultAsync(notification => notification.Id == id, cancellationToken);
        if (entity == null)
        {
            return NotFound("Notification not found.");
        }

        if (entity.UserId != currentUserId)
        {
            return Forbid();
        }

        entity.IsRead = true;
        entity.ReadAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "notification.read",
            nameof(CommunityNotification),
            entity.Id.ToString(),
            null,
            currentUserId,
            cancellationToken);

        var response = new NotificationResponse
        {
            Id = entity.Id,
            UserId = entity.UserId,
            Type = entity.Type,
            Title = entity.Title,
            Detail = entity.Detail,
            IsRead = entity.IsRead,
            CreatedAtUtc = entity.CreatedAtUtc,
            ReadAtUtc = entity.ReadAtUtc,
            RelatedPostId = entity.RelatedPostId,
            RelatedCommentId = entity.RelatedCommentId,
            RelatedReplyId = entity.RelatedReplyId,
            RelatedGroupConversationId = entity.RelatedGroupConversationId,
            ImageUrl = entity.ImageUrl
        };
        await EnrichActorsAsync([response], cancellationToken);

        return Ok(response);
    }

    [HttpPost("read-all")]
    public async Task<ActionResult> MarkAllRead(
        MarkAllNotificationsReadRequest request,
        CancellationToken cancellationToken)
    {
        var accessError = EnsureCurrentUserMatches(request.UserId);
        if (accessError is not null)
        {
            return accessError;
        }

        var currentUserId = CurrentUserId!.Value;
        var targets = await _dbContext.Notifications
            .Where(notification =>
                notification.UserId == currentUserId &&
                !notification.IsRead &&
                VisibleNotificationTypes.Contains(notification.Type))
            .ToListAsync(cancellationToken);

        foreach (var item in targets)
        {
            item.IsRead = true;
            item.ReadAtUtc = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "notification.readall",
            nameof(CommunityNotification),
            currentUserId.ToString(),
            $"count={targets.Count}",
            currentUserId,
            cancellationToken);

        return Ok(new { updated = targets.Count });
    }

    private async Task EnrichActorsAsync(List<NotificationResponse> items, CancellationToken cancellationToken)
    {
        var actorIdsByNotificationId = new Dictionary<Guid, Guid>();

        var commentNotifications = items
            .Where(item =>
                item.Type.Contains("Comment", StringComparison.OrdinalIgnoreCase) &&
                (item.RelatedReplyId.HasValue || item.RelatedCommentId.HasValue))
            .ToList();

        if (commentNotifications.Count > 0)
        {
            var commentIds = commentNotifications
                .Select(item => item.RelatedReplyId ?? item.RelatedCommentId)
                .OfType<Guid>()
                .Distinct()
                .ToList();

            var commentActors = await _dbContext.PostComments
                .AsNoTracking()
                .Where(comment => commentIds.Contains(comment.Id))
                .Select(comment => new
                {
                    comment.Id,
                    comment.UserId
                })
                .ToListAsync(cancellationToken);

            var commentActorMap = commentActors.ToDictionary(item => item.Id, item => item.UserId);

            foreach (var notification in commentNotifications)
            {
                var relatedCommentId = notification.RelatedReplyId ?? notification.RelatedCommentId;
                if (relatedCommentId.HasValue && commentActorMap.TryGetValue(relatedCommentId.Value, out var actorUserId))
                {
                    actorIdsByNotificationId[notification.Id] = actorUserId;
                }
            }
        }

        var reactionNotifications = items
            .Where(item =>
                item.Type.Contains("Reaction", StringComparison.OrdinalIgnoreCase) &&
                item.RelatedPostId.HasValue)
            .ToList();

        if (reactionNotifications.Count > 0)
        {
            var reactionPostIds = reactionNotifications
                .Select(item => item.RelatedPostId!.Value)
                .Distinct()
                .ToList();

            var reactionActors = await _dbContext.PostReactions
                .AsNoTracking()
                .Where(reaction => reactionPostIds.Contains(reaction.PostId))
                .Select(reaction => new
                {
                    reaction.PostId,
                    reaction.UserId,
                    reaction.CreatedAtUtc
                })
                .ToListAsync(cancellationToken);

            foreach (var notification in reactionNotifications)
            {
                var actor = reactionActors
                    .Where(reaction => reaction.PostId == notification.RelatedPostId!.Value)
                    .OrderBy(reaction => Math.Abs((reaction.CreatedAtUtc - notification.CreatedAtUtc).Ticks))
                    .FirstOrDefault();

                if (actor != null)
                {
                    actorIdsByNotificationId[notification.Id] = actor.UserId;
                }
            }
        }

        if (actorIdsByNotificationId.Count > 0)
        {
            var actorUserIds = actorIdsByNotificationId.Values
                .Distinct()
                .ToList();

            var actorUsers = await _dbContext.Users
                .AsNoTracking()
                .Where(user => actorUserIds.Contains(user.Id))
                .Select(user => new
                {
                    user.Id,
                    user.DisplayName,
                    user.AvatarUrl,
                    user.Gender
                })
                .ToListAsync(cancellationToken);

            var actorUserMap = actorUsers.ToDictionary(user => user.Id);

            foreach (var notification in items)
            {
                if (!actorIdsByNotificationId.TryGetValue(notification.Id, out var actorUserId) ||
                    !actorUserMap.TryGetValue(actorUserId, out var actorUser))
                {
                    continue;
                }

                notification.ActorDisplayName = actorUser.DisplayName;
                notification.ActorAvatarUrl = NormalizeActorAvatarUrl(UserAvatarHelper.Resolve(actorUser.AvatarUrl, actorUser.Gender));
            }
        }

        var unresolvedNotifications = items
            .Where(item => string.IsNullOrWhiteSpace(item.ActorDisplayName))
            .ToList();

        var actorNames = unresolvedNotifications
            .Select(item => new
            {
                Item = item,
                ActorName = ExtractActorDisplayName(item.Type, item.Detail)
            })
            .Where(entry => !string.IsNullOrWhiteSpace(entry.ActorName))
            .ToList();

        foreach (var entry in actorNames)
        {
            entry.Item.ActorDisplayName = entry.ActorName;
        }

        var distinctActorNames = actorNames
            .Select(entry => entry.ActorName!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (distinctActorNames.Count == 0)
        {
            return;
        }

        var users = await _dbContext.Users
            .AsNoTracking()
            .Where(user => distinctActorNames.Contains(user.DisplayName))
            .Select(user => new
            {
                user.DisplayName,
                user.AvatarUrl,
                user.Gender
            })
            .ToListAsync(cancellationToken);

        var usersByName = users
            .GroupBy(user => user.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var entry in actorNames)
        {
            if (!usersByName.TryGetValue(entry.ActorName!, out var user))
            {
                continue;
            }

            entry.Item.ActorAvatarUrl = NormalizeActorAvatarUrl(UserAvatarHelper.Resolve(user.AvatarUrl, user.Gender));
        }
    }

    private static string? NormalizeActorAvatarUrl(string? avatarUrl)
    {
        var normalizedAvatarUrl = avatarUrl?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedAvatarUrl) ||
            string.Equals(normalizedAvatarUrl, UserAvatarHelper.MaleAvatarPath, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedAvatarUrl, UserAvatarHelper.FemaleAvatarPath, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return normalizedAvatarUrl;
    }

    private static string? ExtractActorDisplayName(string type, string detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return null;
        }

        var suffix = GetActorDetailSuffix(type, detail);
        if (suffix == null || !detail.EndsWith(suffix, StringComparison.Ordinal))
        {
            return null;
        }

        var actorName = detail[..^suffix.Length].Trim();
        return string.IsNullOrWhiteSpace(actorName) ? null : actorName;
    }

    private static string? GetActorDetailSuffix(string type, string detail)
    {
        if (type.Contains("Reaction", StringComparison.OrdinalIgnoreCase))
        {
            return " reacted to your post.";
        }

        if (type.Contains("CommentMention", StringComparison.OrdinalIgnoreCase))
        {
            return " mentioned you in a comment.";
        }

        if (type.Contains("ReplyMention", StringComparison.OrdinalIgnoreCase))
        {
            return " mentioned you in a reply.";
        }

        if (type.Contains("CommentReply", StringComparison.OrdinalIgnoreCase))
        {
            return " replied to your comment.";
        }

        if (type.Contains("Comment", StringComparison.OrdinalIgnoreCase))
        {
            return " commented on your post.";
        }

        if (type.Contains("Request", StringComparison.OrdinalIgnoreCase))
        {
            if (detail.EndsWith(" sent you a friend request.", StringComparison.Ordinal))
            {
                return " sent you a friend request.";
            }

            if (detail.EndsWith(" accepted your request.", StringComparison.Ordinal))
            {
                return " accepted your request.";
            }

            if (detail.EndsWith(" declined your request.", StringComparison.Ordinal))
            {
                return " declined your request.";
            }
        }

        return null;
    }
}
