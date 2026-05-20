using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TunSociety.Api.Data;
using TunSociety.Api.DTOs.Admin;
using TunSociety.Api.Models;

namespace TunSociety.Api.Services;

public sealed class AuditService
{
    private static readonly JsonSerializerOptions MetadataSerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] SubjectMetadataKeys =
    [
        "userId",
        "subjectUserId",
        "targetUserId",
        "recipientUserId",
        "ownerUserId"
    ];

    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<AuditService> _logger;

    public AuditService(ApplicationDbContext dbContext, ILogger<AuditService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public Task LogAsync(
        string action,
        string entityType,
        string entityId,
        string? data,
        Guid? actorUserId,
        CancellationToken cancellationToken = default)
    {
        var metadata = ParseLegacyData(data);

        return LogAsync(new AuditLogEntry
        {
            ActorUserId = actorUserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Data = data,
            Metadata = metadata
        }, cancellationToken);
    }

    public async Task LogAsync(AuditLogEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var metadata = NormalizeMetadata(entry.Metadata);
        var category = NormalizeCategory(entry.Category) ?? ResolveCategory(entry.Action);
        var subjectUserId = entry.SubjectUserId ?? ResolveSubjectUserId(entry.EntityType, entry.EntityId, metadata);
        var targetDisplayName = NormalizeValue(entry.TargetDisplayName);
        var summary = NormalizeValue(entry.Summary) ?? BuildSummary(entry.Action, metadata, entry.EntityType, entry.EntityId, targetDisplayName, null);

        var audit = new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = entry.ActorUserId,
            SubjectUserId = subjectUserId,
            Category = category,
            Action = entry.Action.Trim(),
            EntityType = entry.EntityType.Trim(),
            EntityId = entry.EntityId.Trim(),
            TargetDisplayName = targetDisplayName,
            Summary = summary,
            Data = NormalizeValue(entry.Data) ?? SerializeLegacyData(metadata),
            MetadataJson = SerializeMetadata(metadata),
            CreatedAtUtc = entry.CreatedAtUtc?.ToUniversalTime() ?? DateTime.UtcNow
        };

        try
        {
            _dbContext.AuditLogs.Add(audit);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to persist audit log for action {Action} on {EntityType}/{EntityId}",
                audit.Action,
                audit.EntityType,
                audit.EntityId);
        }
    }

    public async Task<List<AdminActivityLogResponse>> GetRecentActivityAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 200);

        var query = ApplyMeaningfulModerationActivityFilter(_dbContext.AuditLogs.AsNoTracking());

        var logs = await query
            .OrderByDescending(log => log.CreatedAtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return await MapResponsesAsync(logs, cancellationToken);
    }

    public async Task<List<AdminActivityLogResponse>> GetUserRecentActivityAsync(
        Guid userId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 200);
        var userIdString = userId.ToString();

        var query = ApplyMeaningfulModerationActivityFilter(_dbContext.AuditLogs.AsNoTracking());

        var logs = await query
            .Where(log =>
                log.ActorUserId == userId ||
                log.SubjectUserId == userId ||
                (log.EntityType == nameof(User) && log.EntityId == userIdString))
            .OrderByDescending(log => log.CreatedAtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return await MapResponsesAsync(logs, cancellationToken);
    }

    public Task<AdminActivityFeedResponse> GetActivityFeedAsync(
        AdminActivityQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        return GetActivityFeedInternalAsync(null, request, cancellationToken);
    }

    public Task<AdminActivityFeedResponse> GetUserActivityFeedAsync(
        Guid userId,
        AdminActivityQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        return GetActivityFeedInternalAsync(userId, request, cancellationToken);
    }

    private async Task<AdminActivityFeedResponse> GetActivityFeedInternalAsync(
        Guid? involvedUserId,
        AdminActivityQueryRequest request,
        CancellationToken cancellationToken)
    {
        request ??= new AdminActivityQueryRequest();

        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var query = await BuildFilteredQueryAsync(request, involvedUserId, cancellationToken);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(log => log.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new AdminActivityFeedResponse
        {
            Items = await MapResponsesAsync(items, cancellationToken),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    private async Task<IQueryable<AuditLog>> BuildFilteredQueryAsync(
        AdminActivityQueryRequest request,
        Guid? involvedUserId,
        CancellationToken cancellationToken)
    {
        var query = ApplyMeaningfulModerationActivityFilter(_dbContext.AuditLogs.AsNoTracking().AsQueryable());

        if (involvedUserId.HasValue)
        {
            query = ApplyInvolvedUserFilter(query, involvedUserId.Value);
        }

        if (request.UserId.HasValue)
        {
            query = ApplyInvolvedUserFilter(query, request.UserId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.UserQuery))
        {
            var normalizedUserQuery = request.UserQuery.Trim();
            var matchingUserIds = await _dbContext.Users
                .AsNoTracking()
                .Where(user =>
                    EF.Functions.Like(user.DisplayName, $"%{normalizedUserQuery}%") ||
                    EF.Functions.Like(user.UserName, $"%{normalizedUserQuery}%") ||
                    EF.Functions.Like(user.Email, $"%{normalizedUserQuery}%"))
                .Select(user => user.Id)
                .Take(100)
                .ToListAsync(cancellationToken);

            if (matchingUserIds.Count == 0)
            {
                return query.Where(_ => false);
            }

            var matchingUserIdStrings = matchingUserIds
                .Select(id => id.ToString())
                .ToList();

            query = query.Where(log =>
                (log.ActorUserId.HasValue && matchingUserIds.Contains(log.ActorUserId.Value)) ||
                (log.SubjectUserId.HasValue && matchingUserIds.Contains(log.SubjectUserId.Value)) ||
                (log.EntityType == nameof(User) && matchingUserIdStrings.Contains(log.EntityId)));
        }

        if (!string.IsNullOrWhiteSpace(request.Category) &&
            !string.Equals(request.Category, "All", StringComparison.OrdinalIgnoreCase))
        {
            var category = request.Category.Trim().ToLowerInvariant();
            query = category switch
            {
                "content" => query.Where(log => log.Category == "content" || log.Action.StartsWith("post.")),
                "moderation" => query.Where(log => log.Category == "moderation" || log.Action.StartsWith("moderation.")),
                "appeal" => query.Where(log => log.Category == "appeal" || log.Action.StartsWith("appeal.")),
                "admin" => query.Where(log => log.Category == "admin" || log.Action.StartsWith("admin.")),
                "profile" => query.Where(log => log.Category == "profile" || log.Action.StartsWith("user.")),
                "social" => query.Where(log => log.Category == "social" || log.Action.StartsWith("friendrequest.")),
                "messaging" => query.Where(log =>
                    log.Category == "messaging" ||
                    log.Action.StartsWith("directmessage.") ||
                    log.Action.StartsWith("message.")),
                "notification" => query.Where(log => log.Category == "notification" || log.Action.StartsWith("notification.")),
                _ => query.Where(log => log.Category == category)
            };
        }

        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            var normalizedQuery = request.Query.Trim();
            query = query.Where(log =>
                EF.Functions.Like(log.Action, $"%{normalizedQuery}%") ||
                EF.Functions.Like(log.Category, $"%{normalizedQuery}%") ||
                EF.Functions.Like(log.EntityType, $"%{normalizedQuery}%") ||
                EF.Functions.Like(log.EntityId, $"%{normalizedQuery}%") ||
                (log.TargetDisplayName != null && EF.Functions.Like(log.TargetDisplayName, $"%{normalizedQuery}%")) ||
                (log.Summary != null && EF.Functions.Like(log.Summary, $"%{normalizedQuery}%")) ||
                (log.Data != null && EF.Functions.Like(log.Data, $"%{normalizedQuery}%")) ||
                (log.MetadataJson != null && EF.Functions.Like(log.MetadataJson, $"%{normalizedQuery}%")));
        }

        if (request.FromUtc.HasValue)
        {
            query = query.Where(log => log.CreatedAtUtc >= request.FromUtc.Value.ToUniversalTime());
        }

        if (request.ToUtc.HasValue)
        {
            query = query.Where(log => log.CreatedAtUtc <= request.ToUtc.Value.ToUniversalTime());
        }

        return query;
    }

    private static IQueryable<AuditLog> ApplyInvolvedUserFilter(IQueryable<AuditLog> query, Guid userId)
    {
        var userIdString = userId.ToString();
        return query.Where(log =>
            log.ActorUserId == userId ||
            log.SubjectUserId == userId ||
            (log.EntityType == nameof(User) && log.EntityId == userIdString));
    }

    private async Task<List<AdminActivityLogResponse>> MapResponsesAsync(
        IReadOnlyCollection<AuditLog> logs,
        CancellationToken cancellationToken)
    {
        if (logs.Count == 0)
        {
            return [];
        }

        var inferredSubjectIds = logs
            .Select(log => ResolveSubjectUserId(log.EntityType, log.EntityId, ParseMetadata(log)))
            .Where(id => id.HasValue)
            .Select(id => id!.Value);

        var userIds = logs
            .SelectMany(log => new[] { log.ActorUserId, log.SubjectUserId })
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Concat(inferredSubjectIds)
            .Distinct()
            .ToList();

        var users = userIds.Count == 0
            ? new Dictionary<Guid, UserSnapshot>()
            : await _dbContext.Users
                .AsNoTracking()
                .Where(user => userIds.Contains(user.Id))
                .Select(user => new UserSnapshot
                {
                    Id = user.Id,
                    DisplayName = user.DisplayName,
                    UserName = user.UserName,
                    Email = user.Email
                })
                .ToDictionaryAsync(user => user.Id, cancellationToken);

        return logs
            .Select(log => MapResponse(log, users))
            .ToList();
    }

    private AdminActivityLogResponse MapResponse(
        AuditLog log,
        IReadOnlyDictionary<Guid, UserSnapshot> users)
    {
        var metadata = ParseMetadata(log);
        var category = NormalizeCategory(log.Category) ?? ResolveCategory(log.Action);
        var subjectUserId = log.SubjectUserId ?? ResolveSubjectUserId(log.EntityType, log.EntityId, metadata);

        users.TryGetValue(log.ActorUserId ?? Guid.Empty, out var actor);
        users.TryGetValue(subjectUserId ?? Guid.Empty, out var subject);

        var subjectDisplayName = subject == null ? null : ResolveUserDisplayName(subject);
        var targetDisplayName = NormalizeValue(log.TargetDisplayName)
            ?? ResolveTargetDisplayName(log.EntityType, log.EntityId, metadata, subjectDisplayName);

        return new AdminActivityLogResponse
        {
            Id = log.Id,
            ActorUserId = log.ActorUserId,
            ActorDisplayName = actor == null ? null : ResolveUserDisplayName(actor),
            ActorEmail = actor?.Email,
            SubjectUserId = subjectUserId,
            SubjectDisplayName = subjectDisplayName,
            SubjectEmail = subject?.Email,
            Category = category,
            Action = log.Action,
            ActionLabel = BuildActionLabel(log.Action, metadata),
            EntityType = log.EntityType,
            EntityId = log.EntityId,
            TargetDisplayName = targetDisplayName,
            Summary = NormalizeValue(log.Summary)
                ?? BuildSummary(log.Action, metadata, log.EntityType, log.EntityId, targetDisplayName, subjectDisplayName),
            Data = log.Data,
            Metadata = metadata,
            CreatedAtUtc = log.CreatedAtUtc
        };
    }

    private static string ResolveCategory(string action)
    {
        if (action.StartsWith("post.", StringComparison.OrdinalIgnoreCase))
        {
            return "content";
        }

        if (action.StartsWith("moderation.", StringComparison.OrdinalIgnoreCase))
        {
            return "moderation";
        }

        if (action.StartsWith("appeal.", StringComparison.OrdinalIgnoreCase))
        {
            return "appeal";
        }

        if (action.StartsWith("admin.", StringComparison.OrdinalIgnoreCase))
        {
            return "admin";
        }

        if (action.StartsWith("user.", StringComparison.OrdinalIgnoreCase))
        {
            return "profile";
        }

        if (action.StartsWith("friendrequest.", StringComparison.OrdinalIgnoreCase))
        {
            return "social";
        }

        if (action.StartsWith("directmessage.", StringComparison.OrdinalIgnoreCase) ||
            action.StartsWith("message.", StringComparison.OrdinalIgnoreCase))
        {
            return "messaging";
        }

        if (action.StartsWith("notification.", StringComparison.OrdinalIgnoreCase))
        {
            return "notification";
        }

        return "system";
    }

    private static string BuildActionLabel(string action, IReadOnlyDictionary<string, string?> metadata)
    {
        if (TryGetMetadata(metadata, "action", out var moderationAction) &&
            !string.Equals(moderationAction, "Allow", StringComparison.OrdinalIgnoreCase))
        {
            return action switch
            {
                "post.create" => string.Equals(moderationAction, "Block", StringComparison.OrdinalIgnoreCase)
                    ? "Blocked post"
                    : "Flagged post",
                "post.update" => string.Equals(moderationAction, "Block", StringComparison.OrdinalIgnoreCase)
                    ? "Blocked post update"
                    : "Flagged post update",
                "post.comment" => string.Equals(moderationAction, "Block", StringComparison.OrdinalIgnoreCase)
                    ? "Blocked comment"
                    : "Flagged comment",
                "directmessage.send" => string.Equals(moderationAction, "Block", StringComparison.OrdinalIgnoreCase)
                    ? "Blocked direct message"
                    : "Flagged direct message",
                "message.create" => string.Equals(moderationAction, "Block", StringComparison.OrdinalIgnoreCase)
                    ? "Blocked message"
                    : "Flagged message",
                _ => action.Replace('.', ' ')
            };
        }

        return action switch
        {
            "post.create" => "Created post",
            "post.update" => "Updated post",
            "post.delete" => "Deleted post",
            "post.comment" => "Added comment",
            "post.react" => "Reacted to post",
            "moderation.dismiss" => "Dismissed moderation report",
            "moderation.warn" => "Issued moderation warning",
            "moderation.freeze" => "Applied moderation freeze",
            "moderation.escalate" => "Marked for deeper review",
            "appeal.review" => "Reviewed appeal",
            "admin.warning.issue" => "Issued manual warning",
            "admin.freeze.issue" => "Applied manual freeze",
            "admin.freeze.release" => "Released account freeze",
            "user.displayname.rejected" => "Rejected display name update",
            "friendrequest.create" => "Sent friend request",
            "friendrequest.update" => "Updated friend request",
            "friendrequest.cancel" => "Canceled friend request",
            "directmessage.send" => "Sent direct message",
            "directmessage.cursor" => "Updated message cursor",
            "directmessage.read" => "Read direct messages",
            _ => action.Replace('.', ' ')
        };
    }

    private static IQueryable<AuditLog> ApplyMeaningfulModerationActivityFilter(IQueryable<AuditLog> query)
    {
        return query.Where(log =>
            log.Action.StartsWith("moderation.") ||
            log.Action.StartsWith("appeal.") ||
            log.Action == "admin.warning.issue" ||
            log.Action == "admin.freeze.issue" ||
            log.Action == "admin.freeze.release" ||
            log.Action == "user.displayname.rejected" ||
            (
                (log.Action == "post.create" ||
                 log.Action == "post.update" ||
                 log.Action == "post.comment" ||
                 log.Action == "directmessage.send" ||
                 log.Action == "message.create") &&
                (
                    (log.Data != null &&
                     (EF.Functions.Like(log.Data, "%action=Flag%") ||
                      EF.Functions.Like(log.Data, "%action=Block%"))) ||
                    (log.MetadataJson != null &&
                     (EF.Functions.Like(log.MetadataJson, "%\"action\":\"Flag\"%") ||
                      EF.Functions.Like(log.MetadataJson, "%\"action\":\"Block\"%")))
                )
            ));
    }

    private static string BuildSummary(
        string action,
        IReadOnlyDictionary<string, string?> metadata,
        string entityType,
        string entityId,
        string? targetDisplayName,
        string? subjectDisplayName)
    {
        if (TryGetMetadata(metadata, "reason", out var reason))
        {
            return reason!;
        }

        if (TryGetMetadata(metadata, "status", out var status))
        {
            return $"Status: {status}";
        }

        if (TryGetMetadata(metadata, "visibility", out var visibility))
        {
            return $"Visibility: {visibility}";
        }

        if (TryGetMetadata(metadata, "reaction", out var reaction))
        {
            return $"Reaction: {reaction}";
        }

        if (TryGetMetadata(metadata, "flags", out var flags) && !string.Equals(flags, "none", StringComparison.OrdinalIgnoreCase))
        {
            return $"Flags: {flags}";
        }

        if (TryGetMetadata(metadata, "action", out var moderationAction) &&
            action.StartsWith("post.", StringComparison.OrdinalIgnoreCase))
        {
            return $"Moderation action: {moderationAction}";
        }

        if (!string.IsNullOrWhiteSpace(targetDisplayName))
        {
            return targetDisplayName;
        }

        if (!string.IsNullOrWhiteSpace(subjectDisplayName))
        {
            return subjectDisplayName;
        }

        return $"{entityType} {entityId}";
    }

    private static string? ResolveTargetDisplayName(
        string entityType,
        string entityId,
        IReadOnlyDictionary<string, string?> metadata,
        string? subjectDisplayName)
    {
        if (TryGetMetadata(metadata, "postTitle", out var postTitle))
        {
            return postTitle;
        }

        if (TryGetMetadata(metadata, "recipientName", out var recipientName))
        {
            return recipientName;
        }

        if (TryGetMetadata(metadata, "targetName", out var targetName))
        {
            return targetName;
        }

        if (entityType == nameof(User) && !string.IsNullOrWhiteSpace(subjectDisplayName))
        {
            return subjectDisplayName;
        }

        return $"{entityType} {entityId}";
    }

    private static string? ResolveUserDisplayName(UserSnapshot snapshot)
    {
        return NormalizeValue(snapshot.DisplayName) ?? NormalizeValue(snapshot.UserName);
    }

    private static Guid? ResolveSubjectUserId(
        string entityType,
        string entityId,
        IReadOnlyDictionary<string, string?> metadata)
    {
        if (entityType == nameof(User) && Guid.TryParse(entityId, out var entityUserId))
        {
            return entityUserId;
        }

        foreach (var key in SubjectMetadataKeys)
        {
            if (TryGetMetadata(metadata, key, out var value) && Guid.TryParse(value, out var parsedUserId))
            {
                return parsedUserId;
            }
        }

        return null;
    }

    private static Dictionary<string, string?> ParseMetadata(AuditLog log)
    {
        if (!string.IsNullOrWhiteSpace(log.MetadataJson))
        {
            try
            {
                var metadata = JsonSerializer.Deserialize<Dictionary<string, string?>>(log.MetadataJson, MetadataSerializerOptions);
                if (metadata is { Count: > 0 })
                {
                    return metadata;
                }
            }
            catch
            {
                // Fall back to the legacy data string.
            }
        }

        return ParseLegacyData(log.Data);
    }

    private static Dictionary<string, string?> ParseLegacyData(string? data)
    {
        if (string.IsNullOrWhiteSpace(data))
        {
            return [];
        }

        return data
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(entry => entry.Split('=', 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]))
            .GroupBy(parts => parts[0], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => NormalizeValue(group.Last()[1]), StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string?> NormalizeMetadata(IReadOnlyDictionary<string, string?>? metadata)
    {
        if (metadata == null || metadata.Count == 0)
        {
            return [];
        }

        return metadata
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
            .ToDictionary(
                entry => entry.Key.Trim(),
                entry => NormalizeValue(entry.Value),
                StringComparer.OrdinalIgnoreCase);
    }

    private static string? SerializeLegacyData(IReadOnlyDictionary<string, string?> metadata)
    {
        if (metadata.Count == 0)
        {
            return null;
        }

        return string.Join(
            ';',
            metadata
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Value))
                .Select(entry => $"{entry.Key}={entry.Value}"));
    }

    private static string? SerializeMetadata(IReadOnlyDictionary<string, string?> metadata)
    {
        if (metadata.Count == 0)
        {
            return null;
        }

        return JsonSerializer.Serialize(metadata, MetadataSerializerOptions);
    }

    private static bool TryGetMetadata(
        IReadOnlyDictionary<string, string?> metadata,
        string key,
        out string? value)
    {
        if (metadata.TryGetValue(key, out value) && !string.IsNullOrWhiteSpace(value))
        {
            value = value.Trim();
            return true;
        }

        value = null;
        return false;
    }

    private static string? NormalizeCategory(string? value)
    {
        var normalized = NormalizeValue(value);
        return normalized?.ToLowerInvariant();
    }

    private static string? NormalizeValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private sealed class UserSnapshot
    {
        public Guid Id { get; init; }

        public string DisplayName { get; init; } = string.Empty;

        public string UserName { get; init; } = string.Empty;

        public string Email { get; init; } = string.Empty;
    }
}

public sealed class AuditLogEntry
{
    public Guid? ActorUserId { get; init; }

    public Guid? SubjectUserId { get; init; }

    public string Category { get; init; } = string.Empty;

    public string Action { get; init; } = string.Empty;

    public string EntityType { get; init; } = string.Empty;

    public string EntityId { get; init; } = string.Empty;

    public string? TargetDisplayName { get; init; }

    public string? Summary { get; init; }

    public string? Data { get; init; }

    public IReadOnlyDictionary<string, string?>? Metadata { get; init; }

    public DateTime? CreatedAtUtc { get; init; }
}
