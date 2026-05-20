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
public class DirectMessagesController : AppControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ModerationService _moderationService;
    private readonly SanctionService _sanctionService;
    private readonly AuditService _auditService;
    private readonly AvatarStorageService _avatarStorageService;
    private const string GroupRoleOwner = "Owner";
    private const string GroupRoleAdmin = "Admin";
    private const string GroupRoleModerator = "Moderator";
    private const string GroupRoleMember = "Member";
    private const string GroupStatusPending = "Pending";
    private const string GroupStatusActive = "Active";
    private const string GroupStatusLeft = "Left";
    private const string GroupPermissionAdminsAndModerators = "AdminsAndModerators";

    public DirectMessagesController(
        ApplicationDbContext dbContext,
        ModerationService moderationService,
        SanctionService sanctionService,
        AuditService auditService,
        AvatarStorageService avatarStorageService)
    {
        _dbContext = dbContext;
        _moderationService = moderationService;
        _sanctionService = sanctionService;
        _auditService = auditService;
        _avatarStorageService = avatarStorageService;
    }

    [HttpGet("conversations")]
    public async Task<ActionResult<IEnumerable<ConversationResponse>>> GetConversations(
        [FromQuery] Guid userId,
        [FromQuery] int messageLimit = 60,
        [FromQuery] bool includeArchived = false,
        CancellationToken cancellationToken = default)
    {
        var accessError = EnsureCurrentUserMatches(userId);
        if (accessError is not null)
        {
            return accessError;
        }

        var currentUserId = CurrentUserId!.Value;
        messageLimit = Math.Clamp(messageLimit, 10, 200);

        var messages = await _dbContext.DirectMessages
            .AsNoTracking()
            .Include(message => message.SenderUser)
            .Include(message => message.RecipientUser)
            .Where(message => message.SenderUserId == currentUserId || message.RecipientUserId == currentUserId)
            .OrderByDescending(message => message.CreatedAtUtc)
            .ThenByDescending(message => message.Id)
            .ToListAsync(cancellationToken);

        var partnerIds = messages
            .Select(message => message.SenderUserId == currentUserId ? message.RecipientUserId : message.SenderUserId)
            .Distinct()
            .ToList();

        var partnerCursorLookup = await _dbContext.DirectMessageReadCursors
            .AsNoTracking()
            .Where(cursor => cursor.PartnerUserId == currentUserId && partnerIds.Contains(cursor.UserId))
            .ToDictionaryAsync(cursor => cursor.UserId, cancellationToken);

        var privateConversations = messages
            .GroupBy(message => message.SenderUserId == currentUserId ? message.RecipientUserId : message.SenderUserId)
            .Select(group =>
            {
                var orderedMessages = group
                    .OrderBy(message => message.CreatedAtUtc)
                    .ThenBy(message => message.Id)
                    .TakeLast(messageLimit)
                    .ToList();
                var firstMessage = orderedMessages.First();
                var partner = firstMessage.SenderUserId == currentUserId
                    ? firstMessage.RecipientUser
                    : firstMessage.SenderUser;

                var unreadCount = group.Count(message => message.RecipientUserId == currentUserId && !message.IsRead);

                return new ConversationResponse
                {
                    ConversationId = partner?.Id ?? Guid.Empty,
                    ConversationType = "Private",
                    PartnerUserId = partner?.Id ?? Guid.Empty,
                    PartnerName = partner?.DisplayName ?? partner?.UserName ?? "Member",
                    PartnerRole = partner?.Role ?? "User",
                    PartnerLastVisibleMessageId = partner != null && partnerCursorLookup.TryGetValue(partner.Id, out var cursor)
                        ? cursor.LastVisibleMessageId
                        : null,
                    LastMessageAtUtc = orderedMessages.Last().CreatedAtUtc,
                    IsPartnerOnline = false,
                    MemberCount = partner == null ? 1 : 2,
                    UnreadCount = unreadCount,
                    Messages = orderedMessages.Select(message => MapMessage(message, partner?.Id ?? Guid.Empty)).ToList()
                };
            })
            .OrderByDescending(conversation => conversation.LastMessageAtUtc)
            .ToList();

        var groupConversations = await LoadGroupConversationResponsesAsync(
            currentUserId,
            messageLimit,
            includeArchived,
            cancellationToken);

        return Ok(privateConversations
            .Concat(groupConversations)
            .OrderByDescending(conversation => conversation.IsPinned)
            .ThenByDescending(conversation => conversation.LastMessageAtUtc)
            .ToList());
    }

    [HttpPost]
    public async Task<ActionResult<SubmissionResult<DirectMessageResponse>>> Send(
        SendDirectMessageRequest request,
        CancellationToken cancellationToken)
    {
        var accessError = EnsureCurrentUserMatches(request.SenderUserId);
        if (accessError is not null)
        {
            return accessError;
        }

        if (request.RecipientUserId == Guid.Empty)
        {
            return BadRequest("RecipientUserId is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return BadRequest("Message content is required.");
        }

        var currentUserId = CurrentUserId!.Value;
        if (currentUserId == request.RecipientUserId)
        {
            return BadRequest("You cannot send a direct message to yourself.");
        }

        var sender = await _dbContext.Users.FirstOrDefaultAsync(user => user.Id == currentUserId, cancellationToken);
        var recipient = await _dbContext.Users.FirstOrDefaultAsync(user => user.Id == request.RecipientUserId, cancellationToken);
        if (sender == null || recipient == null)
        {
            return NotFound("Sender or recipient user not found.");
        }

        var frozenError = EnsureActiveUser(sender);
        if (frozenError is not null)
        {
            return frozenError;
        }

        var entityId = Guid.NewGuid();
        var rawContent = request.Content.Trim();
        var entity = new DirectMessage
        {
            Id = entityId,
            SenderUserId = currentUserId,
            RecipientUserId = request.RecipientUserId,
            Content = rawContent,
            CreatedAtUtc = DateTime.UtcNow,
            IsRead = false
        };

        _dbContext.DirectMessages.Add(entity);
        _dbContext.Notifications.Add(new CommunityNotification
        {
            Id = Guid.NewGuid(),
            UserId = request.RecipientUserId,
            Type = "Message",
            Title = "New direct message",
            Detail = $"{sender.DisplayName} sent you a message.",
            CreatedAtUtc = DateTime.UtcNow
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "directmessage.send",
            nameof(DirectMessage),
            entity.Id.ToString(),
            "action=Allow;score=0.000;flags=none;source=direct-message-bypass",
            currentUserId,
            cancellationToken);

        entity.SenderUser = sender;
        entity.RecipientUser = recipient;

        return Ok(new SubmissionResult<DirectMessageResponse>
        {
            Data = MapMessage(entity, recipient.Id),
            Moderation = new ModerationFeedbackResponse
            {
                Action = "Allow",
                Score = 0,
                Flags = [],
                IsSuppressed = false,
                WarningCount = 0,
                SuppressionCount = 0,
                RemainingViolationsBeforeFreeze = 0,
                AccountFrozen = false
            }
        });
    }

    [HttpPost("groups")]
    public async Task<ActionResult<ConversationResponse>> CreateGroupConversation(
        CreateGroupConversationRequest request,
        CancellationToken cancellationToken)
    {
        var accessError = EnsureCurrentUserMatches(request.CreatorUserId);
        if (accessError is not null)
        {
            return accessError;
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Group name is required.");
        }

        var currentUserId = CurrentUserId!.Value;
        var creator = await _dbContext.Users.FirstOrDefaultAsync(user => user.Id == currentUserId, cancellationToken);
        if (creator == null)
        {
            return NotFound("Creator not found.");
        }

        var frozenError = EnsureActiveUser(creator);
        if (frozenError is not null)
        {
            return frozenError;
        }

        var memberIds = request.MemberUserIds
            .Where(id => id != Guid.Empty)
            .Append(currentUserId)
            .Distinct()
            .Take(100)
            .ToList();

        if (memberIds.Count < 2)
        {
            return BadRequest("Choose at least one other member for the group.");
        }

        var users = await _dbContext.Users
            .Where(user => memberIds.Contains(user.Id))
            .ToListAsync(cancellationToken);

        if (users.Count != memberIds.Count)
        {
            return BadRequest("One or more selected members were not found.");
        }

        var now = DateTime.UtcNow;
        var conversation = new GroupConversation
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            AvatarUrl = string.IsNullOrWhiteSpace(request.AvatarUrl) ? null : request.AvatarUrl.Trim(),
            InviteCode = GenerateInviteCode(),
            CreatedByUserId = currentUserId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        foreach (var user in users)
        {
            var isCreator = user.Id == currentUserId;
            conversation.Members.Add(new GroupConversationMember
            {
                GroupConversationId = conversation.Id,
                UserId = user.Id,
                User = user,
                Role = isCreator ? GroupRoleOwner : GroupRoleMember,
                Status = isCreator ? GroupStatusActive : GroupStatusPending,
                LastReadAtUtc = isCreator ? now : null,
                InvitedByUserId = isCreator ? null : currentUserId,
                InvitedAtUtc = isCreator ? null : now,
                JoinedAtUtc = now
            });

            if (!isCreator)
            {
                _dbContext.Notifications.Add(new CommunityNotification
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    Type = "Group",
                    Title = $"You were added to {conversation.Name}",
                    Detail = $"You were added to {conversation.Name}. Open the group to stay or leave.",
                    RelatedGroupConversationId = conversation.Id,
                    ImageUrl = conversation.AvatarUrl,
                    CreatedAtUtc = now
                });
            }
        }

        _dbContext.GroupConversations.Add(conversation);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "groupconversation.create",
            nameof(GroupConversation),
            conversation.Id.ToString(),
            $"members={conversation.Members.Count}",
            currentUserId,
            cancellationToken);

        conversation.CreatedByUser = creator;
        return Ok(MapGroupConversation(conversation, currentUserId, messageLimit: 60, isArchived: false));
    }

    [HttpPost("groups/{conversationId:guid}/messages")]
    public async Task<ActionResult<SubmissionResult<DirectMessageResponse>>> SendGroupMessage(
        Guid conversationId,
        SendGroupMessageRequest request,
        CancellationToken cancellationToken)
    {
        var accessError = EnsureCurrentUserMatches(request.SenderUserId);
        if (accessError is not null)
        {
            return accessError;
        }

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return BadRequest("Message content is required.");
        }

        var currentUserId = CurrentUserId!.Value;
        var conversation = await _dbContext.GroupConversations
            .Include(item => item.Members)
                .ThenInclude(item => item.User)
            .FirstOrDefaultAsync(item => item.Id == conversationId, cancellationToken);

        if (conversation == null)
        {
            return NotFound("Group conversation not found.");
        }

        var senderMembership = conversation.Members.FirstOrDefault(member => member.UserId == currentUserId);
        if (senderMembership == null || senderMembership.Status == GroupStatusLeft)
        {
            return Forbid();
        }

        if (senderMembership.Status != GroupStatusActive)
        {
            return BadRequest("Stay in the group before sending messages.");
        }

        var sender = await _dbContext.Users.FirstOrDefaultAsync(user => user.Id == currentUserId, cancellationToken);
        if (sender == null)
        {
            return NotFound("Sender not found.");
        }

        var frozenError = EnsureActiveUser(sender);
        if (frozenError is not null)
        {
            return frozenError;
        }

        var entityId = Guid.NewGuid();
        var rawContent = request.Content.Trim();
        var moderation = await _moderationService.EvaluateAsync(entityId, rawContent, "GROUP_MESSAGE", cancellationToken);
        moderation.ContentType = nameof(GroupMessage);
        moderation.UserId = sender.Id;
        moderation.ContentSnapshot = rawContent;
        _dbContext.ModerationResults.Add(moderation);

        var outcome = await _sanctionService.EvaluateAsync(sender, moderation, cancellationToken);
        if (outcome.Warning != null)
        {
            _dbContext.Warnings.Add(outcome.Warning);
        }

        if (outcome.Freeze != null)
        {
            _dbContext.Freezes.Add(outcome.Freeze);
        }

        GroupMessage? entity = null;
        if (moderation.Action == "Allow")
        {
            entity = new GroupMessage
            {
                Id = entityId,
                GroupConversationId = conversation.Id,
                SenderUserId = currentUserId,
                Content = rawContent,
                CreatedAtUtc = DateTime.UtcNow
            };

            _dbContext.GroupMessages.Add(entity);
            conversation.UpdatedAtUtc = entity.CreatedAtUtc;

            foreach (var member in conversation.Members.Where(member =>
                member.UserId != currentUserId &&
                member.Status == GroupStatusActive &&
                !member.IsMuted))
            {
                _dbContext.Notifications.Add(new CommunityNotification
                {
                    Id = Guid.NewGuid(),
                    UserId = member.UserId,
                    Type = "Message",
                    Title = conversation.Name,
                    Detail = $"{sender.DisplayName} sent a message to {conversation.Name}.",
                    RelatedGroupConversationId = conversation.Id,
                    ImageUrl = conversation.AvatarUrl,
                    CreatedAtUtc = DateTime.UtcNow
                });
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "groupmessage.send",
            nameof(GroupMessage),
            entity?.Id.ToString() ?? entityId.ToString(),
            $"conversation={conversation.Id};action={moderation.Action};score={moderation.Score:F3}",
            currentUserId,
            cancellationToken);

        if (entity != null)
        {
            entity.SenderUser = sender;
            entity.GroupConversation = conversation;
        }

        return Ok(new SubmissionResult<DirectMessageResponse>
        {
            Data = entity == null ? null : MapGroupMessage(entity, conversation.Name),
            Moderation = ModerationFeedbackResponse.From(moderation, outcome)
        });
    }

    [HttpPost("groups/{conversationId:guid}/read")]
    public async Task<ActionResult> MarkGroupConversationRead(
        Guid conversationId,
        MarkNotificationReadRequest request,
        CancellationToken cancellationToken)
    {
        var accessError = EnsureCurrentUserMatches(request.UserId);
        if (accessError is not null)
        {
            return accessError;
        }

        var currentUserId = CurrentUserId!.Value;
        var member = await _dbContext.GroupConversationMembers
            .FirstOrDefaultAsync(
                item => item.GroupConversationId == conversationId && item.UserId == currentUserId,
                cancellationToken);

        if (member == null)
        {
            return NotFound("Group conversation membership not found.");
        }

        var lastMessage = await _dbContext.GroupMessages
            .AsNoTracking()
            .Where(item => item.GroupConversationId == conversationId)
            .OrderByDescending(item => item.CreatedAtUtc)
            .ThenByDescending(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken);

        member.LastReadAtUtc = DateTime.UtcNow;
        member.LastReadMessageId = lastMessage?.Id;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new { updated = 1, lastVisibleMessageId = member.LastReadMessageId });
    }

    [HttpPost("conversations/{conversationId:guid}/archive")]
    public async Task<ActionResult> ArchiveConversation(
        Guid conversationId,
        ArchiveConversationRequest request,
        CancellationToken cancellationToken)
    {
        var accessError = EnsureCurrentUserMatches(request.UserId);
        if (accessError is not null)
        {
            return accessError;
        }

        var member = await _dbContext.GroupConversationMembers
            .FirstOrDefaultAsync(
                item => item.GroupConversationId == conversationId && item.UserId == CurrentUserId!.Value,
                cancellationToken);

        if (member == null)
        {
            return Ok(new { updated = 0 });
        }

        member.IsArchived = request.IsArchived;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new { updated = 1, isArchived = member.IsArchived });
    }

    [HttpGet("groups/{conversationId:guid}")]
    public async Task<ActionResult<ConversationResponse>> GetGroupConversation(
        Guid conversationId,
        [FromQuery] Guid userId,
        [FromQuery] int messageLimit = 120,
        CancellationToken cancellationToken = default)
    {
        var accessError = EnsureCurrentUserMatches(userId);
        if (accessError is not null)
        {
            return accessError;
        }

        var conversation = await LoadGroupConversationAsync(conversationId, cancellationToken);
        if (conversation == null)
        {
            return NotFound("Group conversation not found.");
        }

        var currentUserId = CurrentUserId!.Value;
        var membership = conversation.Members.FirstOrDefault(member => member.UserId == currentUserId);
        if (membership == null || membership.Status == GroupStatusLeft)
        {
            return Forbid();
        }

        return Ok(MapGroupConversation(conversation, currentUserId, Math.Clamp(messageLimit, 10, 200), membership.IsArchived));
    }

    [HttpPut("groups/{conversationId:guid}/profile")]
    public async Task<ActionResult<ConversationResponse>> UpdateGroupProfile(
        Guid conversationId,
        UpdateGroupProfileRequest request,
        CancellationToken cancellationToken)
    {
        var accessError = EnsureCurrentUserMatches(request.ActorUserId);
        if (accessError is not null)
        {
            return accessError;
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Group name is required.");
        }

        var currentUserId = CurrentUserId!.Value;
        var conversation = await LoadGroupConversationAsync(conversationId, cancellationToken);
        if (conversation == null)
        {
            return NotFound("Group conversation not found.");
        }

        var actorMembership = GetActiveMembership(conversation, currentUserId);
        if (actorMembership == null || !CanEditGroup(actorMembership))
        {
            return Forbid();
        }

        conversation.Name = request.Name.Trim();
        conversation.AvatarUrl = string.IsNullOrWhiteSpace(request.AvatarUrl) ? null : request.AvatarUrl.Trim();
        conversation.Introduction = NormalizeOptionalText(request.Introduction, 1000);
        conversation.Notice = NormalizeOptionalText(request.Notice, 1000);
        conversation.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.LogAsync(
            "groupconversation.profile",
            nameof(GroupConversation),
            conversation.Id.ToString(),
            "updated=profile",
            currentUserId,
            cancellationToken);

        return Ok(MapGroupConversation(conversation, currentUserId, messageLimit: 120, actorMembership.IsArchived));
    }

    [HttpPost("groups/{conversationId:guid}/avatar")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<ActionResult<ConversationResponse>> UploadGroupAvatar(
        Guid conversationId,
        [FromForm] UploadGroupAvatarRequest request,
        CancellationToken cancellationToken)
    {
        var accessError = EnsureCurrentUserMatches(request.ActorUserId);
        if (accessError is not null)
        {
            return accessError;
        }

        var avatarFile = request.Avatar;
        if (avatarFile == null || avatarFile.Length == 0)
        {
            return BadRequest("Please choose an image file.");
        }

        var currentUserId = CurrentUserId!.Value;
        var conversation = await LoadGroupConversationAsync(conversationId, cancellationToken);
        if (conversation == null)
        {
            return NotFound("Group conversation not found.");
        }

        var actorMembership = GetActiveMembership(conversation, currentUserId);
        if (actorMembership == null || !CanEditGroup(actorMembership))
        {
            return Forbid();
        }

        string? newAvatarUrl = null;
        var previousAvatarUrl = conversation.AvatarUrl;

        try
        {
            newAvatarUrl = await _avatarStorageService.SaveAvatarAsync(conversation.Id, avatarFile, cancellationToken);
            conversation.AvatarUrl = newAvatarUrl;
            conversation.UpdatedAtUtc = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);
            _avatarStorageService.DeleteManagedAvatar(previousAvatarUrl);

            await _auditService.LogAsync(
                "groupconversation.avatar",
                nameof(GroupConversation),
                conversation.Id.ToString(),
                "updated=avatar",
                currentUserId,
                cancellationToken);

            return Ok(MapGroupConversation(conversation, currentUserId, messageLimit: 120, actorMembership.IsArchived));
        }
        catch (InvalidOperationException ex)
        {
            if (newAvatarUrl is not null)
            {
                _avatarStorageService.DeleteManagedAvatar(newAvatarUrl);
            }

            return BadRequest(ex.Message);
        }
        catch
        {
            if (newAvatarUrl is not null)
            {
                _avatarStorageService.DeleteManagedAvatar(newAvatarUrl);
            }

            return StatusCode(500, "Unable to save the group picture.");
        }
    }

    [HttpPost("groups/{conversationId:guid}/members")]
    public async Task<ActionResult<ConversationResponse>> AddGroupMembers(
        Guid conversationId,
        AddGroupMembersRequest request,
        CancellationToken cancellationToken)
    {
        var accessError = EnsureCurrentUserMatches(request.ActorUserId);
        if (accessError is not null)
        {
            return accessError;
        }

        var currentUserId = CurrentUserId!.Value;
        var conversation = await LoadGroupConversationAsync(conversationId, cancellationToken);
        if (conversation == null)
        {
            return NotFound("Group conversation not found.");
        }

        var actorMembership = GetActiveMembership(conversation, currentUserId);
        if (actorMembership == null || !CanAddMembers(actorMembership))
        {
            return Forbid();
        }

        var newMemberIds = request.MemberUserIds
            .Where(id => id != Guid.Empty && id != currentUserId)
            .Distinct()
            .Take(100)
            .ToList();

        if (newMemberIds.Count == 0)
        {
            return BadRequest("Choose at least one member to add.");
        }

        var users = await _dbContext.Users
            .Where(user => newMemberIds.Contains(user.Id))
            .ToListAsync(cancellationToken);

        if (users.Count != newMemberIds.Count)
        {
            return BadRequest("One or more selected members were not found.");
        }

        var now = DateTime.UtcNow;
        foreach (var user in users)
        {
            var existing = conversation.Members.FirstOrDefault(member => member.UserId == user.Id);
            if (existing != null && existing.Status != GroupStatusLeft)
            {
                continue;
            }

            if (existing == null)
            {
                existing = new GroupConversationMember
                {
                    GroupConversationId = conversation.Id,
                    UserId = user.Id,
                    User = user,
                    Role = GroupRoleMember,
                    JoinedAtUtc = now
                };
                conversation.Members.Add(existing);
            }

            existing.Role = GroupRoleMember;
            existing.Status = GroupStatusPending;
            existing.IsArchived = false;
            existing.IsMuted = false;
            existing.IsPinned = false;
            existing.InvitedByUserId = currentUserId;
            existing.InvitedAtUtc = now;
            existing.LeftAtUtc = null;

            _dbContext.Notifications.Add(CreateGroupAddedNotification(conversation, user.Id, now));
        }

        conversation.UpdatedAtUtc = now;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "groupconversation.members.add",
            nameof(GroupConversation),
            conversation.Id.ToString(),
            $"count={newMemberIds.Count}",
            currentUserId,
            cancellationToken);

        return Ok(MapGroupConversation(conversation, currentUserId, messageLimit: 120, actorMembership.IsArchived));
    }

    [HttpPost("groups/{conversationId:guid}/members/{memberUserId:guid}/role")]
    public async Task<ActionResult<ConversationResponse>> UpdateGroupMemberRole(
        Guid conversationId,
        Guid memberUserId,
        UpdateGroupMemberRoleRequest request,
        CancellationToken cancellationToken)
    {
        var accessError = EnsureCurrentUserMatches(request.ActorUserId);
        if (accessError is not null)
        {
            return accessError;
        }

        var currentUserId = CurrentUserId!.Value;
        var conversation = await LoadGroupConversationAsync(conversationId, cancellationToken);
        if (conversation == null)
        {
            return NotFound("Group conversation not found.");
        }

        var actorMembership = GetActiveMembership(conversation, currentUserId);
        if (actorMembership == null || !CanManageMembers(actorMembership))
        {
            return Forbid();
        }

        var targetMembership = conversation.Members.FirstOrDefault(member => member.UserId == memberUserId && member.Status != GroupStatusLeft);
        if (targetMembership == null)
        {
            return NotFound("Group member not found.");
        }

        if (targetMembership.Role == GroupRoleOwner && targetMembership.UserId == conversation.CreatedByUserId)
        {
            return BadRequest("The group creator role cannot be changed.");
        }

        targetMembership.Role = NormalizeGroupRole(request.Role);
        conversation.UpdatedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "groupconversation.member.role",
            nameof(GroupConversation),
            conversation.Id.ToString(),
            $"member={memberUserId};role={targetMembership.Role}",
            currentUserId,
            cancellationToken);

        return Ok(MapGroupConversation(conversation, currentUserId, messageLimit: 120, actorMembership.IsArchived));
    }

    [HttpPost("groups/{conversationId:guid}/members/{memberUserId:guid}/remove")]
    public async Task<ActionResult<ConversationResponse>> RemoveGroupMember(
        Guid conversationId,
        Guid memberUserId,
        RemoveGroupMemberRequest request,
        CancellationToken cancellationToken)
    {
        var accessError = EnsureCurrentUserMatches(request.ActorUserId);
        if (accessError is not null)
        {
            return accessError;
        }

        var currentUserId = CurrentUserId!.Value;
        if (currentUserId == memberUserId)
        {
            return BadRequest("Use leave group to remove yourself.");
        }

        var conversation = await LoadGroupConversationAsync(conversationId, cancellationToken);
        if (conversation == null)
        {
            return NotFound("Group conversation not found.");
        }

        var actorMembership = GetActiveMembership(conversation, currentUserId);
        if (actorMembership == null || !CanManageMembers(actorMembership))
        {
            return Forbid();
        }

        var targetMembership = conversation.Members.FirstOrDefault(member => member.UserId == memberUserId && member.Status != GroupStatusLeft);
        if (targetMembership == null)
        {
            return NotFound("Group member not found.");
        }

        if (targetMembership.UserId == conversation.CreatedByUserId)
        {
            return BadRequest("The group creator cannot be removed.");
        }

        MarkMemberLeft(targetMembership, DateTime.UtcNow);
        conversation.UpdatedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "groupconversation.member.remove",
            nameof(GroupConversation),
            conversation.Id.ToString(),
            $"member={memberUserId}",
            currentUserId,
            cancellationToken);

        return Ok(MapGroupConversation(conversation, currentUserId, messageLimit: 120, actorMembership.IsArchived));
    }

    [HttpPost("groups/{conversationId:guid}/membership/accept")]
    public async Task<ActionResult<ConversationResponse>> AcceptGroupMembership(
        Guid conversationId,
        UpdateGroupMembershipRequest request,
        CancellationToken cancellationToken)
    {
        var accessError = EnsureCurrentUserMatches(request.UserId);
        if (accessError is not null)
        {
            return accessError;
        }

        var currentUserId = CurrentUserId!.Value;
        var conversation = await LoadGroupConversationAsync(conversationId, cancellationToken);
        if (conversation == null)
        {
            return NotFound("Group conversation not found.");
        }

        var member = conversation.Members.FirstOrDefault(item => item.UserId == currentUserId);
        if (member == null || member.Status == GroupStatusLeft)
        {
            return NotFound("Group membership not found.");
        }

        member.Status = GroupStatusActive;
        member.IsArchived = false;
        member.LeftAtUtc = null;
        member.LastReadAtUtc = DateTime.UtcNow;
        conversation.UpdatedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(MapGroupConversation(conversation, currentUserId, messageLimit: 120, member.IsArchived));
    }

    [HttpPost("groups/{conversationId:guid}/membership/leave")]
    public async Task<ActionResult> LeaveGroupConversation(
        Guid conversationId,
        UpdateGroupMembershipRequest request,
        CancellationToken cancellationToken)
    {
        var accessError = EnsureCurrentUserMatches(request.UserId);
        if (accessError is not null)
        {
            return accessError;
        }

        var currentUserId = CurrentUserId!.Value;
        var conversation = await LoadGroupConversationAsync(conversationId, cancellationToken);
        if (conversation == null)
        {
            return NotFound("Group conversation not found.");
        }

        var member = conversation.Members.FirstOrDefault(item => item.UserId == currentUserId);
        if (member == null || member.Status == GroupStatusLeft)
        {
            return Ok(new { updated = 0 });
        }

        MarkMemberLeft(member, DateTime.UtcNow);
        EnsureGroupHasAdmin(conversation);
        conversation.UpdatedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "groupconversation.leave",
            nameof(GroupConversation),
            conversation.Id.ToString(),
            null,
            currentUserId,
            cancellationToken);

        return Ok(new { updated = 1 });
    }

    [HttpPost("groups/{conversationId:guid}/preferences")]
    public async Task<ActionResult<ConversationResponse>> UpdateGroupPreferences(
        Guid conversationId,
        UpdateGroupPreferencesRequest request,
        CancellationToken cancellationToken)
    {
        var accessError = EnsureCurrentUserMatches(request.UserId);
        if (accessError is not null)
        {
            return accessError;
        }

        var currentUserId = CurrentUserId!.Value;
        var conversation = await LoadGroupConversationAsync(conversationId, cancellationToken);
        if (conversation == null)
        {
            return NotFound("Group conversation not found.");
        }

        var member = conversation.Members.FirstOrDefault(item => item.UserId == currentUserId && item.Status != GroupStatusLeft);
        if (member == null)
        {
            return NotFound("Group membership not found.");
        }

        if (request.IsMuted.HasValue)
        {
            member.IsMuted = request.IsMuted.Value;
        }

        if (request.IsPinned.HasValue)
        {
            member.IsPinned = request.IsPinned.Value;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(MapGroupConversation(conversation, currentUserId, messageLimit: 120, member.IsArchived));
    }

    [HttpPost("groups/{conversationId:guid}/create-room-permission")]
    public async Task<ActionResult<ConversationResponse>> UpdateCreateRoomPermission(
        Guid conversationId,
        UpdateGroupCreateRoomPermissionRequest request,
        CancellationToken cancellationToken)
    {
        var accessError = EnsureCurrentUserMatches(request.ActorUserId);
        if (accessError is not null)
        {
            return accessError;
        }

        var currentUserId = CurrentUserId!.Value;
        var conversation = await LoadGroupConversationAsync(conversationId, cancellationToken);
        if (conversation == null)
        {
            return NotFound("Group conversation not found.");
        }

        var actorMembership = GetActiveMembership(conversation, currentUserId);
        if (actorMembership == null || !CanManageMembers(actorMembership))
        {
            return Forbid();
        }

        conversation.CreateRoomPermission = NormalizeCreateRoomPermission(request.CreateRoomPermission);
        conversation.UpdatedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(MapGroupConversation(conversation, currentUserId, messageLimit: 120, actorMembership.IsArchived));
    }

    [HttpPost("groups/{conversationId:guid}/clear-history")]
    public async Task<ActionResult<ConversationResponse>> ClearGroupChatHistory(
        Guid conversationId,
        ClearGroupChatRequest request,
        CancellationToken cancellationToken)
    {
        var accessError = EnsureCurrentUserMatches(request.UserId);
        if (accessError is not null)
        {
            return accessError;
        }

        var currentUserId = CurrentUserId!.Value;
        var conversation = await LoadGroupConversationAsync(conversationId, cancellationToken);
        if (conversation == null)
        {
            return NotFound("Group conversation not found.");
        }

        var member = conversation.Members.FirstOrDefault(item => item.UserId == currentUserId && item.Status != GroupStatusLeft);
        if (member == null)
        {
            return NotFound("Group membership not found.");
        }

        member.ClearedAtUtc = DateTime.UtcNow;
        member.LastReadAtUtc = DateTime.UtcNow;
        member.LastReadMessageId = conversation.Messages.OrderByDescending(item => item.CreatedAtUtc).FirstOrDefault()?.Id;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(MapGroupConversation(conversation, currentUserId, messageLimit: 120, member.IsArchived));
    }

    [HttpPost("groups/{conversationId:guid}/delete")]
    public async Task<ActionResult> DeleteGroupConversation(
        Guid conversationId,
        DeleteGroupConversationRequest request,
        CancellationToken cancellationToken)
    {
        var accessError = EnsureCurrentUserMatches(request.ActorUserId);
        if (accessError is not null)
        {
            return accessError;
        }

        var currentUserId = CurrentUserId!.Value;
        var conversation = await LoadGroupConversationAsync(conversationId, cancellationToken);
        if (conversation == null)
        {
            return NotFound("Group conversation not found.");
        }

        var actorMembership = GetActiveMembership(conversation, currentUserId);
        if (actorMembership == null || !CanDeleteGroup(conversation, actorMembership, currentUserId))
        {
            return Forbid();
        }

        var now = DateTime.UtcNow;
        conversation.DeletedAtUtc = now;
        conversation.UpdatedAtUtc = now;
        foreach (var member in conversation.Members)
        {
            MarkMemberLeft(member, now);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.LogAsync(
            "groupconversation.delete",
            nameof(GroupConversation),
            conversation.Id.ToString(),
            null,
            currentUserId,
            cancellationToken);

        return Ok(new { deleted = true });
    }

    [HttpPost("conversations/{partnerUserId:guid}/cursor")]
    public async Task<ActionResult> UpdateConversationReadCursor(
        Guid partnerUserId,
        UpdateConversationReadCursorRequest request,
        CancellationToken cancellationToken)
    {
        var accessError = EnsureCurrentUserMatches(request.UserId);
        if (accessError is not null)
        {
            return accessError;
        }

        if (request.LastVisibleMessageId == Guid.Empty)
        {
            return BadRequest("LastVisibleMessageId is required.");
        }

        var currentUserId = CurrentUserId!.Value;
        var candidate = await _dbContext.DirectMessages
            .AsNoTracking()
            .FirstOrDefaultAsync(message =>
                message.Id == request.LastVisibleMessageId &&
                message.SenderUserId == partnerUserId &&
                message.RecipientUserId == currentUserId,
                cancellationToken);

        if (candidate is null)
        {
            return NotFound("Message not found in this conversation.");
        }

        var cursor = await _dbContext.DirectMessageReadCursors
            .FirstOrDefaultAsync(
                item => item.UserId == currentUserId && item.PartnerUserId == partnerUserId,
                cancellationToken);

        if (cursor != null && !IsLaterVisibleMessage(candidate, cursor.LastVisibleMessageAtUtc, cursor.LastVisibleMessageId))
        {
            return Ok(new { updated = 0, lastVisibleMessageId = cursor.LastVisibleMessageId });
        }

        if (cursor == null)
        {
            cursor = new DirectMessageReadCursor
            {
                UserId = currentUserId,
                PartnerUserId = partnerUserId,
                LastVisibleMessageId = candidate.Id,
                LastVisibleMessageAtUtc = candidate.CreatedAtUtc,
                UpdatedAtUtc = DateTime.UtcNow
            };
            _dbContext.DirectMessageReadCursors.Add(cursor);
        }
        else
        {
            cursor.LastVisibleMessageId = candidate.Id;
            cursor.LastVisibleMessageAtUtc = candidate.CreatedAtUtc;
            cursor.UpdatedAtUtc = DateTime.UtcNow;
        }

        var targets = await _dbContext.DirectMessages
            .Where(message =>
                message.SenderUserId == partnerUserId &&
                message.RecipientUserId == currentUserId &&
                !message.IsRead &&
                message.CreatedAtUtc <= candidate.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        foreach (var message in targets)
        {
            message.IsRead = true;
            message.ReadAtUtc = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "directmessage.cursor",
            nameof(DirectMessage),
            currentUserId.ToString(),
            $"partner={partnerUserId};message={candidate.Id};count={targets.Count}",
            currentUserId,
            cancellationToken);

        return Ok(new { updated = targets.Count, lastVisibleMessageId = candidate.Id });
    }

    [HttpPost("conversations/{partnerUserId:guid}/read")]
    public async Task<ActionResult> MarkConversationRead(
        Guid partnerUserId,
        MarkNotificationReadRequest request,
        CancellationToken cancellationToken)
    {
        var accessError = EnsureCurrentUserMatches(request.UserId);
        if (accessError is not null)
        {
            return accessError;
        }

        var currentUserId = CurrentUserId!.Value;
        var targets = await _dbContext.DirectMessages
            .Where(message =>
                message.SenderUserId == partnerUserId &&
                message.RecipientUserId == currentUserId &&
                !message.IsRead)
            .ToListAsync(cancellationToken);

        foreach (var message in targets)
        {
            message.IsRead = true;
            message.ReadAtUtc = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "directmessage.read",
            nameof(DirectMessage),
            currentUserId.ToString(),
            $"partner={partnerUserId};count={targets.Count}",
            currentUserId,
            cancellationToken);

        return Ok(new { updated = targets.Count });
    }

    private static bool IsLaterVisibleMessage(
        DirectMessage candidate,
        DateTime? currentVisibleAtUtc,
        Guid? currentVisibleMessageId)
    {
        if (currentVisibleAtUtc == null || currentVisibleMessageId == null)
        {
            return true;
        }

        if (candidate.CreatedAtUtc > currentVisibleAtUtc.Value)
        {
            return true;
        }

        if (candidate.CreatedAtUtc < currentVisibleAtUtc.Value)
        {
            return false;
        }

        return string.CompareOrdinal(candidate.Id.ToString(), currentVisibleMessageId.Value.ToString()) > 0;
    }

    private async Task<GroupConversation?> LoadGroupConversationAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        return await _dbContext.GroupConversations
            .AsSplitQuery()
            .Include(item => item.Members)
                .ThenInclude(item => item.User)
            .Include(item => item.Messages)
                .ThenInclude(item => item.SenderUser)
            .FirstOrDefaultAsync(item => item.Id == conversationId && item.DeletedAtUtc == null, cancellationToken);
    }

    private static GroupConversationMember? GetActiveMembership(GroupConversation conversation, Guid userId)
    {
        return conversation.Members.FirstOrDefault(member => member.UserId == userId && member.Status == GroupStatusActive);
    }

    private static bool CanEditGroup(GroupConversationMember member)
    {
        return member.Status == GroupStatusActive &&
            (member.Role == GroupRoleOwner || member.Role == GroupRoleAdmin || member.Role == GroupRoleModerator);
    }

    private static bool CanAddMembers(GroupConversationMember member)
    {
        return CanEditGroup(member);
    }

    private static bool CanManageMembers(GroupConversationMember member)
    {
        return member.Status == GroupStatusActive &&
            (member.Role == GroupRoleOwner || member.Role == GroupRoleAdmin);
    }

    private static bool CanDeleteGroup(GroupConversation conversation, GroupConversationMember member, Guid currentUserId)
    {
        return member.Status == GroupStatusActive &&
            (conversation.CreatedByUserId == currentUserId || member.Role == GroupRoleOwner || member.Role == GroupRoleAdmin);
    }

    private static string NormalizeGroupRole(string role)
    {
        return role.Trim() switch
        {
            GroupRoleAdmin => GroupRoleAdmin,
            GroupRoleModerator => GroupRoleModerator,
            _ => GroupRoleMember
        };
    }

    private static string NormalizeCreateRoomPermission(string value)
    {
        return value.Trim() switch
        {
            "AdminsOnly" => "AdminsOnly",
            "AllMembers" => "AllMembers",
            _ => GroupPermissionAdminsAndModerators
        };
    }

    private static string? NormalizeOptionalText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static string GenerateInviteCode()
    {
        return Guid.NewGuid().ToString("N")[..12];
    }

    private static void MarkMemberLeft(GroupConversationMember member, DateTime now)
    {
        member.Status = GroupStatusLeft;
        member.IsArchived = true;
        member.IsPinned = false;
        member.LeftAtUtc = now;
        member.LastReadAtUtc = now;
    }

    private static void EnsureGroupHasAdmin(GroupConversation conversation)
    {
        if (conversation.Members.Any(member =>
            member.Status == GroupStatusActive &&
            (member.Role == GroupRoleOwner || member.Role == GroupRoleAdmin)))
        {
            return;
        }

        var nextAdmin = conversation.Members
            .Where(member => member.Status == GroupStatusActive)
            .OrderBy(member => member.JoinedAtUtc)
            .FirstOrDefault();

        if (nextAdmin != null)
        {
            nextAdmin.Role = GroupRoleAdmin;
        }
    }

    private static int RoleSort(string role)
    {
        return role switch
        {
            GroupRoleOwner => 0,
            GroupRoleAdmin => 1,
            GroupRoleModerator => 2,
            _ => 3
        };
    }

    private static CommunityNotification CreateGroupAddedNotification(
        GroupConversation conversation,
        Guid userId,
        DateTime now)
    {
        return new CommunityNotification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = "Group",
            Title = $"You were added to {conversation.Name}",
            Detail = $"You were added to {conversation.Name}. Open the group to stay or leave.",
            RelatedGroupConversationId = conversation.Id,
            ImageUrl = conversation.AvatarUrl,
            CreatedAtUtc = now
        };
    }

    private async Task<List<ConversationResponse>> LoadGroupConversationResponsesAsync(
        Guid currentUserId,
        int messageLimit,
        bool includeArchived,
        CancellationToken cancellationToken)
    {
        var conversations = await _dbContext.GroupConversations
            .AsNoTracking()
            .AsSplitQuery()
            .Include(item => item.Members)
                .ThenInclude(item => item.User)
            .Include(item => item.Messages)
                .ThenInclude(item => item.SenderUser)
            .Where(item =>
                item.DeletedAtUtc == null &&
                item.Members.Any(member =>
                    member.UserId == currentUserId &&
                    member.Status != GroupStatusLeft &&
                    (includeArchived || !member.IsArchived)))
            .ToListAsync(cancellationToken);

        return conversations
            .Select(item =>
            {
                var currentMember = item.Members.First(member => member.UserId == currentUserId);
                return MapGroupConversation(item, currentUserId, messageLimit, currentMember.IsArchived);
            })
            .ToList();
    }

    private static ConversationResponse MapGroupConversation(
        GroupConversation conversation,
        Guid currentUserId,
        int messageLimit,
        bool isArchived)
    {
        var currentMember = conversation.Members.FirstOrDefault(member => member.UserId == currentUserId);
        var orderedMessages = conversation.Messages
            .Where(message => currentMember?.ClearedAtUtc == null || message.CreatedAtUtc > currentMember.ClearedAtUtc.Value)
            .OrderBy(message => message.CreatedAtUtc)
            .ThenBy(message => message.Id)
            .TakeLast(messageLimit)
            .ToList();

        var lastReadAtUtc = currentMember?.LastReadAtUtc;
        var unreadCount = orderedMessages.Count(message =>
            message.SenderUserId != currentUserId &&
            (lastReadAtUtc == null || message.CreatedAtUtc > lastReadAtUtc.Value));
        var lastActivity = orderedMessages.LastOrDefault()?.CreatedAtUtc ?? conversation.UpdatedAtUtc ?? conversation.CreatedAtUtc;
        var currentRole = currentMember?.Role ?? GroupRoleMember;
        var activeMembers = conversation.Members
            .Where(member => member.Status != GroupStatusLeft)
            .ToList();

        return new ConversationResponse
        {
            ConversationId = conversation.Id,
            ConversationType = "Group",
            PartnerUserId = conversation.Id,
            PartnerName = conversation.Name,
            PartnerRole = "Group chat",
            AvatarUrl = conversation.AvatarUrl,
            GroupIntroduction = conversation.Introduction,
            GroupNotice = conversation.Notice,
            CreateRoomPermission = conversation.CreateRoomPermission,
            InviteCode = conversation.InviteCode,
            CurrentUserRole = currentRole,
            CurrentUserMembershipStatus = currentMember?.Status ?? GroupStatusPending,
            CurrentUserCanEditGroup = currentMember != null && CanEditGroup(currentMember),
            CurrentUserCanManageMembers = currentMember != null && CanManageMembers(currentMember),
            CurrentUserCanDeleteGroup = currentMember != null && CanDeleteGroup(conversation, currentMember, currentUserId),
            IsMuted = currentMember?.IsMuted ?? false,
            IsPinned = currentMember?.IsPinned ?? false,
            PartnerLastVisibleMessageId = currentMember?.LastReadMessageId,
            LastMessageAtUtc = lastActivity,
            IsPartnerOnline = false,
            IsArchived = isArchived,
            MemberCount = activeMembers.Count,
            UnreadCount = unreadCount,
            Members = activeMembers
                .OrderBy(member => RoleSort(member.Role))
                .ThenBy(member => member.User?.DisplayName ?? member.User?.UserName)
                .Select(member => new GroupConversationMemberResponse
                {
                    UserId = member.UserId,
                    DisplayName = member.User?.DisplayName ?? member.User?.UserName ?? "Member",
                    Role = member.Role,
                    Status = member.Status,
                    AvatarUrl = member.User?.AvatarUrl,
                    IsCurrentUser = member.UserId == currentUserId,
                    JoinedAtUtc = member.JoinedAtUtc
                })
                .ToList(),
            Messages = orderedMessages
                .Select(message => MapGroupMessage(message, conversation.Name))
                .ToList()
        };
    }

    private static DirectMessageResponse MapMessage(DirectMessage message, Guid conversationId)
    {
        return new DirectMessageResponse
        {
            Id = message.Id,
            ConversationId = conversationId,
            ConversationType = "Private",
            SenderUserId = message.SenderUserId,
            SenderName = message.SenderUser?.DisplayName ?? message.SenderUser?.UserName ?? "Member",
            RecipientUserId = message.RecipientUserId,
            RecipientName = message.RecipientUser?.DisplayName ?? message.RecipientUser?.UserName ?? "Member",
            Content = message.Content,
            CreatedAtUtc = message.CreatedAtUtc,
            IsRead = message.IsRead
        };
    }

    private static DirectMessageResponse MapGroupMessage(GroupMessage message, string groupName)
    {
        return new DirectMessageResponse
        {
            Id = message.Id,
            ConversationId = message.GroupConversationId,
            ConversationType = "Group",
            SenderUserId = message.SenderUserId,
            SenderName = message.SenderUser?.DisplayName ?? message.SenderUser?.UserName ?? "Member",
            RecipientUserId = Guid.Empty,
            RecipientName = groupName,
            Content = message.Content,
            CreatedAtUtc = message.CreatedAtUtc,
            IsRead = true
        };
    }
}
