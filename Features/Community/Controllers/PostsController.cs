using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TunSociety.Api.Data;
using TunSociety.Api.DTOs.Common;
using TunSociety.Api.DTOs.Community;
using TunSociety.Api.Infrastructure;
using TunSociety.Api.DTOs.Moderation;
using TunSociety.Api.Models;
using TunSociety.Api.Services;

namespace TunSociety.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PostsController : AppControllerBase
{
    private static readonly string[] AllowedVisibilities = ["Public", "OnlyFriends", "Private"];
    private static readonly string[] AllowedReactions = ["like", "insightful", "support"];

    private readonly ApplicationDbContext _dbContext;
    private readonly ModerationService _moderationService;
    private readonly SanctionService _sanctionService;
    private readonly AuditService _auditService;

    public PostsController(
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
    public async Task<ActionResult<IEnumerable<PostResponse>>> GetFeed(
        [FromQuery] Guid userId,
        [FromQuery] int take = 30,
        CancellationToken cancellationToken = default)
    {
        if (CurrentUserId is not Guid currentUserId)
        {
            return Unauthorized();
        }

        if (userId != Guid.Empty && userId != currentUserId)
        {
            return Forbid();
        }

        var userExists = await _dbContext.Users.AnyAsync(user => user.Id == currentUserId, cancellationToken);
        if (!userExists)
        {
            return NotFound("User not found.");
        }

        take = Math.Clamp(take, 1, 100);

        var friendSet = await LoadFriendSetAsync(currentUserId, cancellationToken);
        var friendIds = friendSet.ToList();

        var postsQuery = _dbContext.Posts
            .AsNoTracking()
            .Include(post => post.User)
                .ThenInclude(user => user!.Badge)
            .Include(post => post.Comments)
                .ThenInclude(comment => comment.User)
            .Include(post => post.Reactions)
            .Where(post =>
                post.Visibility == "Public" ||
                post.UserId == currentUserId ||
                (post.Visibility == "OnlyFriends" && friendIds.Contains(post.UserId)));

        var posts = await postsQuery
            .OrderByDescending(post => post.CreatedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

        var visiblePosts = posts
            .Take(take)
            .Select(post => MapPost(post, currentUserId))
            .ToList();

        return Ok(visiblePosts);
    }

    [HttpGet("{postId:guid}")]
    public async Task<ActionResult<PostResponse>> GetById(
        Guid postId,
        [FromQuery] Guid userId,
        CancellationToken cancellationToken = default)
    {
        var accessError = EnsureCurrentUserMatches(userId);
        if (accessError is not null)
        {
            return accessError;
        }

        var currentUserId = CurrentUserId!.Value;
        var post = await LoadPostGraphAsync(postId, cancellationToken);
        if (post == null)
        {
            return NotFound("Post not found.");
        }

        var friendSet = await LoadFriendSetAsync(currentUserId, cancellationToken);
        if (!CanUserViewPost(post, currentUserId, friendSet))
        {
            return Forbid();
        }

        return Ok(MapPost(post, currentUserId));
    }

    [HttpPost]
    public async Task<ActionResult<SubmissionResult<PostResponse>>> Create(CreatePostRequest request, CancellationToken cancellationToken)
    {
        var accessError = EnsureCurrentUserMatches(request.UserId);
        if (accessError is not null)
        {
            return accessError;
        }

        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Content))
        {
            return BadRequest("Title and content are required.");
        }

        var visibility = NormalizeVisibility(request.Visibility);
        if (visibility == null)
        {
            return BadRequest("Visibility must be Public, OnlyFriends, or Private.");
        }

        var currentUserId = CurrentUserId!.Value;
        var user = await _dbContext.Users
            .Include(candidate => candidate.Badge)
            .FirstOrDefaultAsync(candidate => candidate.Id == currentUserId, cancellationToken);
        if (user == null)
        {
            return NotFound("User not found.");
        }

        var frozenError = EnsureActiveUser(user);
        if (frozenError is not null)
        {
            return frozenError;
        }

        var postId = Guid.NewGuid();
        var rawTitle = request.Title.Trim();
        var rawContent = request.Content.Trim();
        var originalSubmission = $"Title: {rawTitle}{Environment.NewLine}Content: {rawContent}";
        var moderation = await _moderationService.EvaluateAsync(postId, originalSubmission, "POST", cancellationToken);
        moderation.ContentType = nameof(Post);
        moderation.UserId = user.Id;
        moderation.ContentSnapshot = originalSubmission;
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

        Post? post = null;
        if (moderation.Action == "Allow")
        {
            post = new Post
            {
                Id = postId,
                UserId = user.Id,
                Title = rawTitle,
                Content = rawContent,
                ImageUrl = string.IsNullOrWhiteSpace(request.ImageUrl) ? null : request.ImageUrl.Trim(),
                Visibility = visibility,
                CreatedAtUtc = DateTime.UtcNow
            };

            _dbContext.Posts.Add(post);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "post.create",
            nameof(Post),
            post?.Id.ToString() ?? postId.ToString(),
            $"visibility={visibility};action={moderation.Action};score={moderation.Score:F3};flags={(moderation.Flags.Count == 0 ? "none" : string.Join(',', moderation.Flags))}",
            user.Id,
            cancellationToken);

        return Ok(new SubmissionResult<PostResponse>
        {
            Data = post == null
                ? null
                : MapPost(post, currentUserId, user.DisplayName, user.Badge?.Name ?? ClubMembershipDefaults.MemberBadgeName),
            Moderation = ModerationFeedbackResponse.From(moderation, outcome)
        });
    }

    [HttpPut("{postId:guid}")]
    public async Task<ActionResult<SubmissionResult<PostResponse>>> Update(
        Guid postId,
        UpdatePostRequest request,
        CancellationToken cancellationToken)
    {
        var accessError = EnsureCurrentUserMatches(request.UserId);
        if (accessError is not null)
        {
            return accessError;
        }

        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Content))
        {
            return BadRequest("Title and content are required.");
        }

        var visibility = NormalizeVisibility(request.Visibility);
        if (visibility == null)
        {
            return BadRequest("Visibility must be Public, OnlyFriends, or Private.");
        }

        var currentUserId = CurrentUserId!.Value;
        var user = await _dbContext.Users.FirstOrDefaultAsync(candidate => candidate.Id == currentUserId, cancellationToken);
        if (user == null)
        {
            return NotFound("User not found.");
        }

        var frozenError = EnsureActiveUser(user);
        if (frozenError is not null)
        {
            return frozenError;
        }

        var post = await LoadPostGraphAsync(postId, cancellationToken);
        if (post == null)
        {
            return NotFound("Post not found.");
        }

        var resourceError = EnsureResourceAccess(post.UserId, allowAdmin: true);
        if (resourceError is not null)
        {
            return resourceError;
        }

        var rawTitle = request.Title.Trim();
        var rawContent = request.Content.Trim();
        var originalSubmission = $"Title: {rawTitle}{Environment.NewLine}Content: {rawContent}";
        var moderation = await _moderationService.EvaluateAsync(post.Id, originalSubmission, "POST", cancellationToken);
        moderation.ContentType = nameof(Post);
        moderation.UserId = user.Id;
        moderation.ContentSnapshot = originalSubmission;
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
            post.Title = rawTitle;
            post.Content = rawContent;
            post.ImageUrl = string.IsNullOrWhiteSpace(request.ImageUrl) ? null : request.ImageUrl.Trim();
            post.Visibility = visibility;
            post.UpdatedAtUtc = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "post.update",
            nameof(Post),
            post.Id.ToString(),
            $"action={moderation.Action};visibility={visibility};score={moderation.Score:F3};flags={(moderation.Flags.Count == 0 ? "none" : string.Join(',', moderation.Flags))}",
            user.Id,
            cancellationToken);

        return Ok(new SubmissionResult<PostResponse>
        {
            Data = moderation.Action == "Allow"
                ? MapPost(post, currentUserId)
                : null,
            Moderation = ModerationFeedbackResponse.From(moderation, outcome)
        });
    }

    [HttpDelete("{postId:guid}")]
    public async Task<ActionResult> Delete(
        Guid postId,
        [FromQuery] Guid userId,
        [FromBody] DeletePostRequest? request,
        CancellationToken cancellationToken)
    {
        var actorUserId = request?.UserId ?? userId;

        if (actorUserId == Guid.Empty && CurrentUserId is Guid authenticatedUserId)
        {
            actorUserId = authenticatedUserId;
        }

        var accessError = EnsureCurrentUserMatches(actorUserId);
        if (accessError is not null)
        {
            return accessError;
        }

        var currentUserId = CurrentUserId!.Value;
        var user = await _dbContext.Users.FirstOrDefaultAsync(candidate => candidate.Id == currentUserId, cancellationToken);
        if (user == null)
        {
            return NotFound("User not found.");
        }

        var post = await _dbContext.Posts.FirstOrDefaultAsync(candidate => candidate.Id == postId, cancellationToken);
        if (post == null)
        {
            return NotFound("Post not found.");
        }

        var resourceError = EnsureResourceAccess(post.UserId, allowAdmin: true);
        if (resourceError is not null)
        {
            return resourceError;
        }

        _dbContext.Posts.Remove(post);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "post.delete",
            nameof(Post),
            post.Id.ToString(),
            $"ownerUserId={post.UserId}",
            user.Id,
            cancellationToken);

        return NoContent();
    }

    [HttpPost("{postId:guid}/comments")]
    public async Task<ActionResult<SubmissionResult<PostResponse>>> AddComment(
        Guid postId,
        CreatePostCommentRequest request,
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
        var user = await _dbContext.Users.FirstOrDefaultAsync(candidate => candidate.Id == currentUserId, cancellationToken);
        if (user == null)
        {
            return NotFound("User not found.");
        }

        var frozenError = EnsureActiveUser(user);
        if (frozenError is not null)
        {
            return frozenError;
        }

        var post = await LoadPostGraphAsync(postId, cancellationToken);
        if (post == null)
        {
            return NotFound("Post not found.");
        }

        var friendSet = await LoadFriendSetAsync(currentUserId, cancellationToken);
        if (!CanUserViewPost(post, currentUserId, friendSet))
        {
            return Forbid();
        }

        PostComment? parentComment = null;
        if (request.ParentCommentId is Guid requestedParentCommentId && requestedParentCommentId != Guid.Empty)
        {
            parentComment = post.Comments.FirstOrDefault(comment => comment.Id == requestedParentCommentId);
            if (parentComment == null)
            {
                return BadRequest("Parent comment not found.");
            }

            if (parentComment.ParentCommentId is Guid topLevelParentId)
            {
                parentComment = post.Comments.FirstOrDefault(comment => comment.Id == topLevelParentId) ?? parentComment;
            }
        }

        var mentionedUserIds = request.MentionedUserIds
            .Where(mentionedUserId => mentionedUserId != Guid.Empty)
            .Distinct()
            .Take(20)
            .ToList();

        if (mentionedUserIds.Count > 0)
        {
            mentionedUserIds = await _dbContext.Users
                .AsNoTracking()
                .Where(candidate => mentionedUserIds.Contains(candidate.Id))
                .Select(candidate => candidate.Id)
                .ToListAsync(cancellationToken);
        }

        var commentId = Guid.NewGuid();
        var rawContent = request.Content.Trim();
        var moderation = await _moderationService.EvaluateAsync(commentId, rawContent, "COMMENT", cancellationToken);
        moderation.ContentType = nameof(PostComment);
        moderation.UserId = user.Id;
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

        PostComment? comment = null;
        if (moderation.Action == "Allow")
        {
            comment = new PostComment
            {
                Id = commentId,
                PostId = post.Id,
                ParentCommentId = parentComment?.Id,
                UserId = currentUserId,
                Content = rawContent,
                MentionedUserIdsJson = JsonSerializer.Serialize(mentionedUserIds),
                CreatedAtUtc = DateTime.UtcNow
            };

            _dbContext.PostComments.Add(comment);

            var notifiedUserIds = new HashSet<Guid>();
            var notificationCreatedAtUtc = DateTime.UtcNow;

            void AddPostActivityNotification(
                Guid targetUserId,
                string type,
                string title,
                string detail,
                Guid? relatedCommentId,
                Guid? relatedReplyId)
            {
                if (targetUserId == currentUserId || !notifiedUserIds.Add(targetUserId))
                {
                    return;
                }

                _dbContext.Notifications.Add(new CommunityNotification
                {
                    Id = Guid.NewGuid(),
                    UserId = targetUserId,
                    Type = type,
                    Title = title,
                    Detail = detail,
                    RelatedPostId = post.Id,
                    RelatedCommentId = relatedCommentId,
                    RelatedReplyId = relatedReplyId,
                    CreatedAtUtc = notificationCreatedAtUtc
                });
            }

            if (parentComment != null)
            {
                AddPostActivityNotification(
                    parentComment.UserId,
                    "CommentReply",
                    "New reply",
                    $"{user.DisplayName} replied to your comment.",
                    parentComment.Id,
                    comment.Id);
            }
            else
            {
                AddPostActivityNotification(
                    post.UserId,
                    "Comment",
                    "New comment",
                    $"{user.DisplayName} commented on your post.",
                    comment.Id,
                    null);
            }

            foreach (var mentionedUserId in mentionedUserIds)
            {
                var isReply = parentComment != null;
                AddPostActivityNotification(
                    mentionedUserId,
                    isReply ? "ReplyMention" : "CommentMention",
                    "You were mentioned",
                    $"{user.DisplayName} mentioned you in {(isReply ? "a reply" : "a comment")}.",
                    parentComment?.Id ?? comment.Id,
                    isReply ? comment.Id : null);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "post.comment",
            nameof(PostComment),
            comment?.Id.ToString() ?? commentId.ToString(),
            $"postId={post.Id};action={moderation.Action};score={moderation.Score:F3};flags={(moderation.Flags.Count == 0 ? "none" : string.Join(',', moderation.Flags))}",
            user.Id,
            cancellationToken);

        if (comment == null)
        {
            return Ok(new SubmissionResult<PostResponse>
            {
                Data = null,
                Moderation = ModerationFeedbackResponse.From(moderation, outcome)
            });
        }

        var refreshed = await LoadPostGraphAsync(postId, cancellationToken);
        if (refreshed == null)
        {
            return NotFound("Post not found after update.");
        }

        return Ok(new SubmissionResult<PostResponse>
        {
            Data = MapPost(refreshed, currentUserId),
            Moderation = ModerationFeedbackResponse.From(moderation, outcome)
        });
    }

    [HttpPost("{postId:guid}/reactions")]
    public async Task<ActionResult<PostResponse>> React(
        Guid postId,
        ReactToPostRequest request,
        CancellationToken cancellationToken)
    {
        var accessError = EnsureCurrentUserMatches(request.UserId);
        if (accessError is not null)
        {
            return accessError;
        }

        var normalizedReaction = request.ReactionType.Trim().ToLowerInvariant();
        if (!AllowedReactions.Contains(normalizedReaction))
        {
            return BadRequest("ReactionType must be like, insightful, or support.");
        }

        var currentUserId = CurrentUserId!.Value;
        var user = await _dbContext.Users.FirstOrDefaultAsync(candidate => candidate.Id == currentUserId, cancellationToken);
        if (user == null)
        {
            return NotFound("User not found.");
        }

        var frozenError = EnsureActiveUser(user);
        if (frozenError is not null)
        {
            return frozenError;
        }

        var post = await LoadPostGraphAsync(postId, cancellationToken);
        if (post == null)
        {
            return NotFound("Post not found.");
        }

        var friendSet = await LoadFriendSetAsync(currentUserId, cancellationToken);
        if (!CanUserViewPost(post, currentUserId, friendSet))
        {
            return Forbid();
        }

        var existingReaction = await _dbContext.PostReactions
            .FirstOrDefaultAsync(
                reaction => reaction.PostId == post.Id && reaction.UserId == currentUserId,
                cancellationToken);

        var notifyPostOwner = false;

        if (existingReaction == null)
        {
            _dbContext.PostReactions.Add(new PostReaction
            {
                Id = Guid.NewGuid(),
                PostId = post.Id,
                UserId = currentUserId,
                ReactionType = normalizedReaction,
                CreatedAtUtc = DateTime.UtcNow
            });
            notifyPostOwner = true;
        }
        else if (existingReaction.ReactionType == normalizedReaction)
        {
            _dbContext.PostReactions.Remove(existingReaction);
        }
        else
        {
            existingReaction.ReactionType = normalizedReaction;
            existingReaction.CreatedAtUtc = DateTime.UtcNow;
            notifyPostOwner = true;
        }

        if (notifyPostOwner && post.UserId != currentUserId)
        {
            _dbContext.Notifications.Add(new CommunityNotification
            {
                Id = Guid.NewGuid(),
                UserId = post.UserId,
                Type = "Reaction",
                Title = "New reaction",
                Detail = $"{user.DisplayName} reacted to your post.",
                RelatedPostId = post.Id,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "post.react",
            nameof(PostReaction),
            post.Id.ToString(),
            $"reaction={normalizedReaction}",
            user.Id,
            cancellationToken);

        var refreshed = await LoadPostGraphAsync(postId, cancellationToken);
        if (refreshed == null)
        {
            return NotFound("Post not found after update.");
        }

        return Ok(MapPost(refreshed, currentUserId));
    }

    private async Task<Post?> LoadPostGraphAsync(Guid postId, CancellationToken cancellationToken)
    {
        return await _dbContext.Posts
            .Include(post => post.User)
                .ThenInclude(user => user!.Badge)
            .Include(post => post.Comments)
                .ThenInclude(comment => comment.User)
            .Include(post => post.Reactions)
            .FirstOrDefaultAsync(post => post.Id == postId, cancellationToken);
    }

    private async Task<HashSet<Guid>> LoadFriendSetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var acceptedFriendIds = await _dbContext.FriendRequests
            .AsNoTracking()
            .Where(request => request.Status == "Accepted" &&
                (request.RequesterUserId == userId || request.RecipientUserId == userId))
            .Select(request => request.RequesterUserId == userId ? request.RecipientUserId : request.RequesterUserId)
            .ToListAsync(cancellationToken);

        return acceptedFriendIds.ToHashSet();
    }

    private static string? NormalizeVisibility(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Public";
        }

        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "public" => "Public",
            "onlyfriends" => "OnlyFriends",
            "friends" => "OnlyFriends",
            "private" => "Private",
            _ => null
        };
    }

    private static bool CanUserViewPost(Post post, Guid userId, HashSet<Guid>? friendSet)
    {
        if (post.Visibility == "Public")
        {
            return true;
        }

        if (post.UserId == userId)
        {
            return true;
        }

        if (post.Visibility == "OnlyFriends")
        {
            return friendSet?.Contains(post.UserId) ?? false;
        }

        return false;
    }

    private static PostResponse MapPost(Post post, Guid userId, string? authorName = null, string? roleLabel = null)
    {
        var reactions = new PostReactionSummaryResponse
        {
            Like = post.Reactions.Count(reaction => reaction.ReactionType == "like"),
            Insightful = post.Reactions.Count(reaction => reaction.ReactionType == "insightful"),
            Support = post.Reactions.Count(reaction => reaction.ReactionType == "support"),
            MyReaction = post.Reactions
                .Where(reaction => reaction.UserId == userId)
                .Select(reaction => reaction.ReactionType)
                .FirstOrDefault()
        };

        var comments = post.Comments
            .OrderBy(comment => comment.CreatedAtUtc)
            .ToList();
        var repliesByParent = comments
            .Where(comment => comment.ParentCommentId.HasValue)
            .GroupBy(comment => comment.ParentCommentId!.Value)
            .ToDictionary(group => group.Key, group => group.OrderBy(comment => comment.CreatedAtUtc).ToList());

        return new PostResponse
        {
            Id = post.Id,
            UserId = post.UserId,
            AuthorName = authorName ?? post.User?.DisplayName ?? post.User?.UserName ?? "Member",
            RoleLabel = roleLabel ?? post.User?.Badge?.Name ?? ClubMembershipDefaults.MemberBadgeName,
            Title = post.Title,
            Content = post.Content,
            ImageUrl = post.ImageUrl,
            Visibility = post.Visibility,
            CreatedAtUtc = post.CreatedAtUtc,
            UpdatedAtUtc = post.UpdatedAtUtc,
            Reactions = reactions,
            Comments = comments
                .Where(comment => comment.ParentCommentId == null)
                .Select(comment => MapComment(comment, repliesByParent, includeReplies: true))
                .ToList()
        };
    }

    private static PostCommentResponse MapComment(
        PostComment comment,
        IReadOnlyDictionary<Guid, List<PostComment>> repliesByParent,
        bool includeReplies)
    {
        var replies = includeReplies && repliesByParent.TryGetValue(comment.Id, out var childComments)
            ? childComments
            : [];

        return new PostCommentResponse
        {
            Id = comment.Id,
            UserId = comment.UserId,
            ParentCommentId = comment.ParentCommentId,
            AuthorName = comment.User?.DisplayName ?? comment.User?.UserName ?? "Member",
            Content = comment.Content,
            MentionedUserIds = ParseMentionedUserIds(comment.MentionedUserIdsJson),
            RepliesCount = replies.Count,
            Replies = replies
                .Select(reply => MapComment(reply, repliesByParent, includeReplies: false))
                .ToList(),
            CreatedAtUtc = comment.CreatedAtUtc
        };
    }

    private static List<Guid> ParseMentionedUserIds(string? mentionedUserIdsJson)
    {
        if (string.IsNullOrWhiteSpace(mentionedUserIdsJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<Guid>>(mentionedUserIdsJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
