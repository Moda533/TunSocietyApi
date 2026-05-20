using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TunSociety.Api.Data;
using TunSociety.Api.DTOs.Community;

namespace TunSociety.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NavbarController : AppControllerBase
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

    public NavbarController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet("badges")]
    public async Task<ActionResult<NavbarBadgeSummaryResponse>> GetBadges(
        [FromQuery] Guid userId,
        CancellationToken cancellationToken = default)
    {
        var accessError = EnsureCurrentUserMatches(userId);
        if (accessError is not null)
        {
            return accessError;
        }

        var currentUserId = CurrentUserId!.Value;

        var unreadNotificationsCount = await _dbContext.Notifications
            .AsNoTracking()
            .CountAsync(notification =>
                notification.UserId == currentUserId &&
                !notification.IsRead &&
                VisibleNotificationTypes.Contains(notification.Type),
                cancellationToken);

        var hasUnreadDirectMessages = await _dbContext.DirectMessages
            .AsNoTracking()
            .AnyAsync(message => message.RecipientUserId == currentUserId && !message.IsRead, cancellationToken);

        var hasUnreadGroupMessages = await _dbContext.GroupConversationMembers
            .AsNoTracking()
            .Where(member =>
                member.UserId == currentUserId &&
                member.Status == "Active" &&
                !member.IsArchived &&
                !member.IsMuted &&
                member.GroupConversation != null &&
                member.GroupConversation.DeletedAtUtc == null)
            .AnyAsync(member => _dbContext.GroupMessages.Any(message =>
                message.GroupConversationId == member.GroupConversationId &&
                message.SenderUserId != currentUserId &&
                (member.ClearedAtUtc == null || message.CreatedAtUtc > member.ClearedAtUtc.Value) &&
                (member.LastReadAtUtc == null || message.CreatedAtUtc > member.LastReadAtUtc.Value)),
                cancellationToken);

        var hasPendingFriendRequests = await _dbContext.FriendRequests
            .AsNoTracking()
            .AnyAsync(request => request.RecipientUserId == currentUserId && request.Status == "Pending", cancellationToken);

        return Ok(new NavbarBadgeSummaryResponse
        {
            UnreadNotificationsCount = unreadNotificationsCount,
            HasUnreadMessages = hasUnreadDirectMessages || hasUnreadGroupMessages,
            HasPendingFriendRequests = hasPendingFriendRequests
        });
    }
}
