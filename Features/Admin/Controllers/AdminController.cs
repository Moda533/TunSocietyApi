using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TunSociety.Api.Data;
using TunSociety.Api.DTOs.Admin;
using TunSociety.Api.DTOs.Community;
using TunSociety.Api.DTOs.Moderation;
using TunSociety.Api.Infrastructure;
using TunSociety.Api.Models;
using TunSociety.Api.Services;

namespace TunSociety.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AdminController : AppControllerBase
{
    private static readonly string[] AllowActionAliases = ["Allow", "ALLOW", "allow", "Allowed", "ALLOWED", "allowed"];
    private static readonly string[] BlockActionAliases = ["Block", "BLOCK", "block", "Blocked", "BLOCKED", "blocked"];
    private readonly ApplicationDbContext _dbContext;
    private readonly AuditService _auditService;
    private readonly RolePermissionService _rolePermissionService;
    private readonly IAuthorizationService _authorizationService;

    public AdminController(
        ApplicationDbContext dbContext,
        AuditService auditService,
        RolePermissionService rolePermissionService,
        IAuthorizationService authorizationService)
    {
        _dbContext = dbContext;
        _auditService = auditService;
        _rolePermissionService = rolePermissionService;
        _authorizationService = authorizationService;
    }

    [Authorize(Policy = PermissionNames.RolePermissionsRead)]
    [HttpGet("role-permissions")]
    public async Task<ActionResult<AdminRolePermissionCatalogResponse>> GetRolePermissions(CancellationToken cancellationToken = default)
    {
        var matrix = await _rolePermissionService.GetPermissionMatrixAsync(cancellationToken);

        return Ok(new AdminRolePermissionCatalogResponse
        {
            Roles = PermissionNames.SystemRoles,
            Permissions = PermissionNames.All,
            RolePermissions = PermissionNames.SystemRoles
                .Select(role => new AdminRolePermissionSetResponse
                {
                    Role = role,
                    Permissions = matrix.TryGetValue(role, out var permissions) ? permissions : []
                })
                .ToList()
        });
    }

    [Authorize(Policy = PermissionNames.RolePermissionsManage)]
    [HttpPut("role-permissions/{role}")]
    public async Task<ActionResult<AdminRolePermissionSetResponse>> UpdateRolePermissions(
        string role,
        UpdateRolePermissionsRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalizedRole = RoleNames.Normalize(role);
        if (normalizedRole == null || !PermissionNames.SystemRoles.Contains(normalizedRole, StringComparer.Ordinal))
        {
            return BadRequest("Role must be User, Moderator, or Admin.");
        }

        var requestedPermissions = (request.Permissions ?? [])
            .Where(permission => !string.IsNullOrWhiteSpace(permission))
            .Select(permission => permission.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var unknownPermissions = requestedPermissions
            .Where(permission => !PermissionNames.IsKnown(permission))
            .ToList();

        if (unknownPermissions.Count > 0)
        {
            return BadRequest($"Unknown permissions: {string.Join(", ", unknownPermissions)}");
        }

        if (normalizedRole == RoleNames.Admin)
        {
            requestedPermissions.Add(PermissionNames.RolePermissionsRead);
            requestedPermissions.Add(PermissionNames.RolePermissionsManage);
            requestedPermissions = requestedPermissions.Distinct(StringComparer.Ordinal).ToList();
        }

        var savedPermissions = await _rolePermissionService.ReplacePermissionsForRoleAsync(
            normalizedRole,
            requestedPermissions,
            cancellationToken);

        return Ok(new AdminRolePermissionSetResponse
        {
            Role = normalizedRole,
            Permissions = savedPermissions
        });
    }

    [Authorize(Policy = PermissionNames.UsersRead)]
    [HttpGet("overview")]
    public async Task<ActionResult<AdminOverviewResponse>> GetOverview(CancellationToken cancellationToken = default)
    {
        var todayUtc = DateTime.UtcNow.Date;
        var weekStartUtc = todayUtc.AddDays(-6);
        var activityStartUtc = DateTime.UtcNow.AddHours(-24);

        var totalUsers = await _dbContext.Users.CountAsync(cancellationToken);
        var activeUsers = await GetActiveUserCountAsync(activityStartUtc, cancellationToken);
        var postsToday = await _dbContext.Posts.CountAsync(post => post.CreatedAtUtc >= todayUtc, cancellationToken);
        var warningsIssued = await _dbContext.Warnings.CountAsync(warning => warning.IssuedAtUtc >= weekStartUtc, cancellationToken);
        var frozenAccounts = await _dbContext.Users.CountAsync(user => user.IsFrozen, cancellationToken);
        var newUsersThisWeek = await _dbContext.Users.CountAsync(user => user.CreatedAtUtc >= weekStartUtc, cancellationToken);
        var unassignedMembers = await _dbContext.Users.CountAsync(user => user.DepartmentId == null, cancellationToken);

        var reviewedReportIds = await _dbContext.AuditLogs
            .AsNoTracking()
            .Where(log => log.EntityType == nameof(ModerationResult) && log.Action.StartsWith("moderation."))
            .Select(log => log.EntityId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var moderationResults = await _dbContext.ModerationResults
            .AsNoTracking()
            .Where(result => result.Action != "Allow")
            .Select(result => new
            {
                result.Id,
                result.IsEscalated
            })
            .ToListAsync(cancellationToken);

        var reportsPending = moderationResults.Count(result =>
            result.IsEscalated || !reviewedReportIds.Contains(result.Id.ToString()));

        var userTrend = await BuildDailyActivityTrendAsync(weekStartUtc, cancellationToken);
        var moderationTrend = await BuildDailyModerationTrendAsync(weekStartUtc, cancellationToken);

        return Ok(new AdminOverviewResponse
        {
            TotalUsers = totalUsers,
            ActiveUsers = activeUsers,
            PostsToday = postsToday,
            ReportsPending = reportsPending,
            WarningsIssued = warningsIssued,
            FrozenAccounts = frozenAccounts,
            NewUsersThisWeek = newUsersThisWeek,
            UnassignedMembers = unassignedMembers,
            UserActivityTrend = userTrend,
            ModerationTrend = moderationTrend
        });
    }

    [Authorize(Policy = PermissionNames.UsersRead)]
    [HttpGet("statistics/overview")]
    public async Task<ActionResult<AdminStatisticsOverviewResponse>> GetStatisticsOverview(
        [FromQuery] string? range = "30d",
        CancellationToken cancellationToken = default)
    {
        var (normalizedRange, days) = NormalizeStatisticsRange(range);
        var todayUtc = DateTime.UtcNow.Date;
        var startUtc = todayUtc.AddDays(-(days - 1));
        var endUtc = todayUtc.AddDays(1);

        var activityStartUtc = startUtc;
        var totalUsers = await _dbContext.Users.CountAsync(cancellationToken);
        var activeUsers = await GetActiveUserCountAsync(activityStartUtc, cancellationToken);
        var unassignedMembers = await _dbContext.Users.CountAsync(user => user.DepartmentId == null, cancellationToken);

        var flaggedContent = await _dbContext.ModerationResults
            .CountAsync(result => result.CreatedAtUtc >= startUtc && result.CreatedAtUtc < endUtc && !AllowActionAliases.Contains(result.Action), cancellationToken);

        var blockedUserIdsFromModeration = await _dbContext.ModerationResults
            .AsNoTracking()
            .Where(result => result.CreatedAtUtc >= startUtc && result.CreatedAtUtc < endUtc && BlockActionAliases.Contains(result.Action))
            .Select(result => result.UserId)
            .ToListAsync(cancellationToken);

        var blockedUserIdsFromFreezes = await _dbContext.Freezes
            .AsNoTracking()
            .Where(freeze => freeze.StartsAtUtc >= startUtc && freeze.StartsAtUtc < endUtc)
            .Select(freeze => freeze.UserId)
            .ToListAsync(cancellationToken);

        var blockedUsers = blockedUserIdsFromModeration
            .Concat(blockedUserIdsFromFreezes)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .Count();

        var pendingAppeals = await _dbContext.Appeals
            .CountAsync(appeal => appeal.Status == "Open" && appeal.CreatedAtUtc >= startUtc && appeal.CreatedAtUtc < endUtc, cancellationToken);

        var resolvedAppeals = await _dbContext.Appeals
            .CountAsync(appeal => appeal.Status != "Open" && appeal.ResolvedAtUtc != null && appeal.ResolvedAtUtc >= startUtc && appeal.ResolvedAtUtc < endUtc, cancellationToken);

        var eventEvaluations = await _dbContext.EventEvaluations
            .AsNoTracking()
            .Where(evaluation => evaluation.CreatedAtUtc >= startUtc && evaluation.CreatedAtUtc < endUtc)
            .Select(evaluation => evaluation.Rating)
            .ToListAsync(cancellationToken);

        var eventAttendanceCount = await _dbContext.EventParticipants
            .AsNoTracking()
            .CountAsync(participant => participant.Status == "Going" || participant.Status == "GoingInterested", cancellationToken);

        var eventParticipationCount = await _dbContext.EventParticipants
            .AsNoTracking()
            .CountAsync(participant => participant.CreatedAtUtc >= startUtc && participant.CreatedAtUtc < endUtc, cancellationToken);

        var eventCommentCount = await _dbContext.EventComments
            .AsNoTracking()
            .CountAsync(comment => comment.CreatedAtUtc >= startUtc && comment.CreatedAtUtc < endUtc, cancellationToken);

        var trends = await BuildStatisticsTrendAsync(startUtc, days, cancellationToken);

        return Ok(new AdminStatisticsOverviewResponse
        {
            Range = normalizedRange,
            Summary = new AdminStatisticsSummaryResponse
            {
                TotalUsers = totalUsers,
                ActiveUsers = activeUsers,
                FlaggedContent = flaggedContent,
                BlockedUsers = blockedUsers,
                PendingAppeals = pendingAppeals,
                ResolvedAppeals = resolvedAppeals,
                UnassignedMembers = unassignedMembers,
                AverageEventRating = eventEvaluations.Count == 0 ? null : Math.Round(eventEvaluations.Average(), 2),
                EventAttendanceCount = eventAttendanceCount,
                EventEngagement = eventParticipationCount + eventCommentCount + eventEvaluations.Count,
                EventEvaluationCount = eventEvaluations.Count
            },
            Trends = trends
        });
    }

    [Authorize(Policy = PermissionNames.EventsManage)]
    [HttpGet("event-evaluations")]
    public async Task<ActionResult<AdminEventEvaluationDashboardResponse>> GetEventEvaluations(
        [FromQuery] int take = 24,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 100);
        var nowUtc = DateTime.UtcNow;

        var ratings = await _dbContext.EventEvaluations
            .AsNoTracking()
            .Select(evaluation => evaluation.Rating)
            .ToListAsync(cancellationToken);

        var feedbackCount = await _dbContext.EventEvaluations
            .AsNoTracking()
            .CountAsync(evaluation => evaluation.Feedback != null && evaluation.Feedback != string.Empty, cancellationToken);

        var eventsWithEvaluations = await _dbContext.EventEvaluations
            .AsNoTracking()
            .Select(evaluation => evaluation.EventId)
            .Distinct()
            .CountAsync(cancellationToken);

        var pastEventsWithoutEvaluations = await _dbContext.Events
            .AsNoTracking()
            .CountAsync(eventItem => eventItem.StartsAtUtc <= nowUtc && !eventItem.Evaluations.Any(), cancellationToken);

        var events = await _dbContext.Events
            .AsNoTracking()
            .Include(eventItem => eventItem.CreatedByUser)
            .Include(eventItem => eventItem.Participants)
            .Include(eventItem => eventItem.Comments)
            .Include(eventItem => eventItem.Evaluations)
            .OrderByDescending(eventItem => eventItem.StartsAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

        var recentFeedback = await _dbContext.EventEvaluations
            .AsNoTracking()
            .Include(evaluation => evaluation.Event)
            .Include(evaluation => evaluation.User)
            .Where(evaluation => evaluation.Feedback != null && evaluation.Feedback != string.Empty)
            .OrderByDescending(evaluation => evaluation.UpdatedAtUtc ?? evaluation.CreatedAtUtc)
            .Take(12)
            .ToListAsync(cancellationToken);

        return Ok(new AdminEventEvaluationDashboardResponse
        {
            Summary = new AdminEventEvaluationSummaryResponse
            {
                TotalEvaluations = ratings.Count,
                AverageRating = ratings.Count == 0 ? null : Math.Round(ratings.Average(), 2),
                EventsWithEvaluations = eventsWithEvaluations,
                PastEventsWithoutEvaluations = pastEventsWithoutEvaluations,
                FeedbackCount = feedbackCount
            },
            Events = events.Select(MapEventEvaluationEvent).ToList(),
            RecentFeedback = recentFeedback.Select(MapEventEvaluationFeedback).ToList()
        });
    }

    [Authorize(Policy = PermissionNames.UsersRead)]
    [HttpGet("audit-logs")]
    public async Task<ActionResult<IEnumerable<AdminActivityLogResponse>>> GetAuditLogs(
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var logs = await _auditService.GetRecentActivityAsync(limit, cancellationToken);
        return Ok(logs);
    }

    [Authorize(Policy = PermissionNames.UsersRead)]
    [HttpGet("activity")]
    public async Task<ActionResult<AdminActivityFeedResponse>> GetActivity(
        [FromQuery] AdminActivityQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        var feed = await _auditService.GetActivityFeedAsync(request, cancellationToken);
        return Ok(feed);
    }

    [Authorize(Policy = PermissionNames.UsersRead)]
    [HttpGet("users/{id:guid}/audit-logs")]
    public async Task<ActionResult<IEnumerable<AdminActivityLogResponse>>> GetUserAuditLogs(
        Guid id,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var logs = await _auditService.GetUserRecentActivityAsync(id, limit, cancellationToken);
        return Ok(logs);
    }

    [Authorize(Policy = PermissionNames.UsersRead)]
    [HttpGet("users/{id:guid}/activity")]
    public async Task<ActionResult<AdminActivityFeedResponse>> GetUserActivity(
        Guid id,
        [FromQuery] AdminActivityQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        var feed = await _auditService.GetUserActivityFeedAsync(id, request, cancellationToken);
        return Ok(feed);
    }

    [Authorize(Policy = PermissionNames.UsersRead)]
    [HttpGet("users")]
    public async Task<ActionResult<IEnumerable<AdminUserRiskSummaryResponse>>> GetUsers([FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 200);
        var users = await _dbContext.Users
            .AsNoTracking()
            .Include(user => user.Department)
            .Include(user => user.Badge)
            .OrderBy(user => user.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        var summaries = await BuildUserRiskSummariesAsync(users, cancellationToken);
        return Ok(summaries
            .OrderByDescending(summary => summary.RiskScore)
            .ThenByDescending(summary => summary.LastViolationAtUtc)
            .ThenBy(summary => summary.DisplayName)
            .ToList());
    }

    [Authorize(Policy = PermissionNames.UsersRead)]
    [HttpGet("users/unassigned")]
    public async Task<ActionResult<IEnumerable<AdminUserRiskSummaryResponse>>> GetUnassignedUsers(CancellationToken cancellationToken = default)
    {
        var users = await _dbContext.Users
            .AsNoTracking()
            .Include(user => user.Department)
            .Include(user => user.Badge)
            .Where(user => user.DepartmentId == null)
            .OrderBy(user => user.DisplayName)
            .ThenBy(user => user.Email)
            .ToListAsync(cancellationToken);

        return Ok(await BuildUserRiskSummariesAsync(users, cancellationToken));
    }

    [Authorize(Policy = PermissionNames.UsersEdit)]
    [HttpPut("users/{id:guid}/membership")]
    public async Task<ActionResult<TunSociety.Api.DTOs.User.UserResponse>> UpdateUserMembership(
        Guid id,
        UpdateUserMembershipRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .Include(current => current.Department)
            .Include(current => current.Badge)
            .FirstOrDefaultAsync(current => current.Id == id, cancellationToken);

        if (user == null)
        {
            return NotFound("User not found.");
        }

        Department? department = null;
        if (request.DepartmentId is Guid departmentId)
        {
            department = await _dbContext.Departments
                .FirstOrDefaultAsync(current => current.Id == departmentId && !current.IsArchived, cancellationToken);

            if (department == null)
            {
                return BadRequest("Department must exist and be active.");
            }
        }

        var badgeId = request.BadgeId.GetValueOrDefault();
        var badge = badgeId == Guid.Empty
            ? await EnsureDefaultBadgeAsync(cancellationToken)
            : await _dbContext.UserBadges
                .Include(current => current.Department)
                .FirstOrDefaultAsync(current => current.Id == badgeId && !current.IsArchived, cancellationToken);

        if (badge == null)
        {
            return BadRequest("Badge must exist and be active.");
        }

        if (badge.DepartmentId is Guid badgeDepartmentId)
        {
            if (badge.Department?.IsArchived == true)
            {
                return BadRequest("Badge department is archived.");
            }

            if (department?.Id != badgeDepartmentId)
            {
                department = badge.Department ?? await _dbContext.Departments
                    .FirstOrDefaultAsync(current => current.Id == badgeDepartmentId && !current.IsArchived, cancellationToken);

                if (department == null)
                {
                    return BadRequest("Badge department must exist and be active.");
                }
            }
        }

        user.DepartmentId = department?.Id;
        user.Department = department;
        user.BadgeId = badge.Id;
        user.Badge = badge;

        await _dbContext.SaveChangesAsync(cancellationToken);
        var permissions = await _rolePermissionService.GetPermissionsForRoleAsync(user.Role, cancellationToken);
        return Ok(TunSociety.Api.DTOs.User.UserResponse.FromEntity(user).WithPermissions(permissions));
    }

    [Authorize(Policy = PermissionNames.DepartmentsRead)]
    [HttpGet("departments")]
    public async Task<ActionResult<IEnumerable<DepartmentResponse>>> GetDepartments(
        [FromQuery] bool includeArchived = false,
        CancellationToken cancellationToken = default)
    {
        var departments = await _dbContext.Departments
            .AsNoTracking()
            .Include(department => department.CreatedBy)
            .Include(department => department.Users)
            .Where(department => includeArchived || !department.IsArchived)
            .OrderBy(department => department.Name)
            .ToListAsync(cancellationToken);

        return Ok(departments.Select(MapDepartment).ToList());
    }

    [Authorize(Policy = PermissionNames.DepartmentsManage)]
    [HttpPost("departments")]
    public async Task<ActionResult<DepartmentResponse>> CreateDepartment(
        DepartmentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (CurrentUserId is not Guid currentUserId)
        {
            return Unauthorized();
        }

        var name = NormalizeRequiredText(request.Name);
        if (name == null)
        {
            return BadRequest("Department name is required.");
        }

        var exists = await _dbContext.Departments.AnyAsync(
            department => department.Name == name,
            cancellationToken);

        if (exists)
        {
            return Conflict("Department name already exists.");
        }

        var now = DateTime.UtcNow;
        var department = new Department
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = NormalizeOptionalText(request.Description),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatedById = currentUserId
        };

        _dbContext.Departments.Add(department);
        await _dbContext.SaveChangesAsync(cancellationToken);

        department.CreatedBy = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(user => user.Id == currentUserId, cancellationToken);

        return CreatedAtAction(nameof(GetDepartments), new { id = department.Id }, MapDepartment(department));
    }

    [Authorize(Policy = PermissionNames.DepartmentsManage)]
    [HttpPut("departments/{id:guid}")]
    public async Task<ActionResult<DepartmentResponse>> UpdateDepartment(
        Guid id,
        DepartmentRequest request,
        CancellationToken cancellationToken = default)
    {
        var department = await _dbContext.Departments
            .Include(current => current.CreatedBy)
            .Include(current => current.Users)
            .FirstOrDefaultAsync(current => current.Id == id, cancellationToken);

        if (department == null)
        {
            return NotFound("Department not found.");
        }

        var name = NormalizeRequiredText(request.Name);
        if (name == null)
        {
            return BadRequest("Department name is required.");
        }

        var exists = await _dbContext.Departments.AnyAsync(
            current => current.Id != id && current.Name == name,
            cancellationToken);

        if (exists)
        {
            return Conflict("Department name already exists.");
        }

        department.Name = name;
        department.Description = NormalizeOptionalText(request.Description);
        department.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(MapDepartment(department));
    }

    [Authorize(Policy = PermissionNames.DepartmentsManage)]
    [HttpDelete("departments/{id:guid}")]
    public async Task<ActionResult> ArchiveDepartment(Guid id, CancellationToken cancellationToken = default)
    {
        var department = await _dbContext.Departments.FirstOrDefaultAsync(current => current.Id == id, cancellationToken);
        if (department == null)
        {
            return NotFound("Department not found.");
        }

        department.IsArchived = true;
        department.UpdatedAtUtc = DateTime.UtcNow;

        var assignedUsers = await _dbContext.Users
            .Where(user => user.DepartmentId == id)
            .ToListAsync(cancellationToken);

        foreach (var user in assignedUsers)
        {
            user.DepartmentId = null;
        }

        var departmentBadges = await _dbContext.UserBadges
            .Where(badge => badge.DepartmentId == id)
            .ToListAsync(cancellationToken);

        foreach (var badge in departmentBadges)
        {
            badge.DepartmentId = null;
            badge.UpdatedAtUtc = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = PermissionNames.DepartmentsRead)]
    [HttpGet("departments/{id:guid}/users")]
    public async Task<ActionResult<IEnumerable<AdminUserRiskSummaryResponse>>> GetDepartmentUsers(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var departmentExists = await _dbContext.Departments.AnyAsync(department => department.Id == id, cancellationToken);
        if (!departmentExists)
        {
            return NotFound("Department not found.");
        }

        var users = await _dbContext.Users
            .AsNoTracking()
            .Include(user => user.Department)
            .Include(user => user.Badge)
            .Where(user => user.DepartmentId == id)
            .OrderBy(user => user.DisplayName)
            .ThenBy(user => user.Email)
            .ToListAsync(cancellationToken);

        return Ok(await BuildUserRiskSummariesAsync(users, cancellationToken));
    }

    [Authorize(Policy = PermissionNames.BadgesRead)]
    [HttpGet("badges")]
    public async Task<ActionResult<IEnumerable<BadgeResponse>>> GetBadges(
        [FromQuery] bool includeArchived = false,
        CancellationToken cancellationToken = default)
    {
        var badges = await _dbContext.UserBadges
            .AsNoTracking()
            .Include(badge => badge.Department)
            .Include(badge => badge.Users)
            .Where(badge => includeArchived || !badge.IsArchived)
            .OrderBy(badge => badge.Name == ClubMembershipDefaults.MemberBadgeName ? 0 : 1)
            .ThenBy(badge => badge.Name)
            .ToListAsync(cancellationToken);

        return Ok(badges.Select(MapBadge).ToList());
    }

    [Authorize(Policy = PermissionNames.BadgesManage)]
    [HttpPost("badges")]
    public async Task<ActionResult<BadgeResponse>> CreateBadge(
        BadgeRequest request,
        CancellationToken cancellationToken = default)
    {
        var name = NormalizeRequiredText(request.Name);
        if (name == null)
        {
            return BadRequest("Badge name is required.");
        }

        var department = await FindActiveDepartmentAsync(request.DepartmentId, cancellationToken);
        if (request.DepartmentId.HasValue && department == null)
        {
            return BadRequest("Department must exist and be active.");
        }

        var exists = await _dbContext.UserBadges.AnyAsync(
            badge => badge.Name == name && badge.DepartmentId == request.DepartmentId,
            cancellationToken);

        if (exists)
        {
            return Conflict("Badge name already exists for this department.");
        }

        var now = DateTime.UtcNow;
        var badge = new UserBadge
        {
            Id = Guid.NewGuid(),
            Name = name,
            DepartmentId = department?.Id,
            Department = department,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        _dbContext.UserBadges.Add(badge);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetBadges), new { id = badge.Id }, MapBadge(badge));
    }

    [Authorize(Policy = PermissionNames.BadgesManage)]
    [HttpPut("badges/{id:guid}")]
    public async Task<ActionResult<BadgeResponse>> UpdateBadge(
        Guid id,
        BadgeRequest request,
        CancellationToken cancellationToken = default)
    {
        var badge = await _dbContext.UserBadges
            .Include(current => current.Department)
            .Include(current => current.Users)
            .FirstOrDefaultAsync(current => current.Id == id, cancellationToken);

        if (badge == null)
        {
            return NotFound("Badge not found.");
        }

        var name = NormalizeRequiredText(request.Name);
        if (name == null)
        {
            return BadRequest("Badge name is required.");
        }

        if (badge.Id == ClubMembershipDefaults.MemberBadgeId &&
            (!string.Equals(name, ClubMembershipDefaults.MemberBadgeName, StringComparison.Ordinal) || request.DepartmentId.HasValue))
        {
            return BadRequest("The default Member badge cannot be renamed or attached to a department.");
        }

        var department = await FindActiveDepartmentAsync(request.DepartmentId, cancellationToken);
        if (request.DepartmentId.HasValue && department == null)
        {
            return BadRequest("Department must exist and be active.");
        }

        var exists = await _dbContext.UserBadges.AnyAsync(
            current => current.Id != id && current.Name == name && current.DepartmentId == request.DepartmentId,
            cancellationToken);

        if (exists)
        {
            return Conflict("Badge name already exists for this department.");
        }

        badge.Name = name;
        badge.DepartmentId = department?.Id;
        badge.Department = department;
        badge.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(MapBadge(badge));
    }

    [Authorize(Policy = PermissionNames.BadgesManage)]
    [HttpDelete("badges/{id:guid}")]
    public async Task<ActionResult> DeleteBadge(Guid id, CancellationToken cancellationToken = default)
    {
        if (id == ClubMembershipDefaults.MemberBadgeId)
        {
            return BadRequest("The default Member badge cannot be deleted.");
        }

        var badge = await _dbContext.UserBadges.FirstOrDefaultAsync(current => current.Id == id, cancellationToken);
        if (badge == null)
        {
            return NotFound("Badge not found.");
        }

        var defaultBadge = await EnsureDefaultBadgeAsync(cancellationToken);
        var assignedUsers = await _dbContext.Users
            .Where(user => user.BadgeId == id)
            .ToListAsync(cancellationToken);

        foreach (var user in assignedUsers)
        {
            user.BadgeId = defaultBadge.Id;
        }

        _dbContext.UserBadges.Remove(badge);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = PermissionNames.UsersRead)]
    [HttpGet("users/{id:guid}/risk-summary")]
    public async Task<ActionResult<AdminUserRiskSummaryResponse>> GetUserRiskSummary(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .AsNoTracking()
            .Include(current => current.Department)
            .Include(current => current.Badge)
            .FirstOrDefaultAsync(current => current.Id == id, cancellationToken);

        if (user == null)
        {
            return NotFound("User not found.");
        }

        var summary = (await BuildUserRiskSummariesAsync(new[] { user }, cancellationToken)).Single();
        return Ok(summary);
    }

    [Authorize(Policy = PermissionNames.UsersRead)]
    [HttpGet("users/{id:guid}/posts")]
    public async Task<ActionResult<IEnumerable<PostResponse>>> GetUserPosts(
        Guid id,
        [FromQuery] int take = 12,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 50);

        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(current => current.Id == id, cancellationToken);

        if (user == null)
        {
            return NotFound("User not found.");
        }

        var posts = await _dbContext.Posts
            .AsNoTracking()
            .Include(post => post.User)
                .ThenInclude(user => user!.Badge)
            .Include(post => post.Comments)
                .ThenInclude(comment => comment.User)
            .Include(post => post.Reactions)
            .Where(post => post.UserId == id)
            .OrderByDescending(post => post.CreatedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

        var currentUserId = CurrentUserId ?? id;
        var mappedPosts = posts
            .Select(post => MapPost(post, currentUserId, post.User?.DisplayName, post.User?.Badge?.Name))
            .ToList();

        return Ok(mappedPosts);
    }

    [Authorize(Policy = PermissionNames.ModerationReview)]
    [HttpPost("users/{id:guid}/warnings")]
    public async Task<ActionResult<WarningReviewResponse>> IssueWarning(
        Guid id,
        IssueUserActionRequest request,
        CancellationToken cancellationToken)
    {
        var reason = NormalizeReason(request.Reason, "Admin issued a warning.");
        var user = await _dbContext.Users.FirstOrDefaultAsync(current => current.Id == id, cancellationToken);
        if (user == null)
        {
            return NotFound("User not found.");
        }

        var warning = new Warning
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Reason = reason,
            IssuedAtUtc = DateTime.UtcNow
        };

        _dbContext.Warnings.Add(warning);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(new AuditLogEntry
        {
            ActorUserId = CurrentUserId,
            SubjectUserId = user.Id,
            Category = "admin",
            Action = "admin.warning.issue",
            EntityType = nameof(User),
            EntityId = user.Id.ToString(),
            TargetDisplayName = user.DisplayName,
            Metadata = BuildUserActionMetadata(reason)
        }, cancellationToken);

        return Ok(new WarningReviewResponse
        {
            Id = warning.Id,
            UserId = user.Id,
            UserDisplayName = user.DisplayName,
            UserEmail = user.Email,
            Reason = warning.Reason,
            IssuedAtUtc = warning.IssuedAtUtc
        });
    }

    [Authorize(Policy = PermissionNames.ModerationFreeze)]
    [HttpPost("users/{id:guid}/freeze")]
    public async Task<ActionResult<FreezeReviewResponse>> FreezeUser(
        Guid id,
        IssueUserActionRequest request,
        CancellationToken cancellationToken)
    {
        var reason = NormalizeReason(request.Reason, "Admin froze the account.");
        var user = await _dbContext.Users.FirstOrDefaultAsync(current => current.Id == id, cancellationToken);
        if (user == null)
        {
            return NotFound("User not found.");
        }

        var freeze = await _dbContext.Freezes
            .FirstOrDefaultAsync(current => current.UserId == id && current.IsActive, cancellationToken);

        if (freeze == null)
        {
            freeze = new Freeze
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Reason = reason,
                StartsAtUtc = DateTime.UtcNow,
                IsActive = true
            };
            _dbContext.Freezes.Add(freeze);
        }
        else
        {
            freeze.Reason = reason;
            freeze.StartsAtUtc = freeze.StartsAtUtc == default ? DateTime.UtcNow : freeze.StartsAtUtc;
            freeze.IsActive = true;
        }

        user.IsFrozen = true;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(new AuditLogEntry
        {
            ActorUserId = CurrentUserId,
            SubjectUserId = user.Id,
            Category = "admin",
            Action = "admin.freeze.issue",
            EntityType = nameof(User),
            EntityId = user.Id.ToString(),
            TargetDisplayName = user.DisplayName,
            Metadata = BuildUserActionMetadata(reason)
        }, cancellationToken);

        return Ok(new FreezeReviewResponse
        {
            Id = freeze.Id,
            UserId = user.Id,
            UserDisplayName = user.DisplayName,
            UserEmail = user.Email,
            Reason = freeze.Reason,
            StartsAtUtc = freeze.StartsAtUtc,
            EndsAtUtc = freeze.EndsAtUtc,
            IsActive = freeze.IsActive
        });
    }

    [Authorize(Policy = PermissionNames.ModerationFreeze)]
    [HttpPost("users/{id:guid}/unfreeze")]
    public async Task<ActionResult> UnfreezeUser(Guid id, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(current => current.Id == id, cancellationToken);
        if (user == null)
        {
            return NotFound("User not found.");
        }

        var activeFreezes = await _dbContext.Freezes
            .Where(current => current.UserId == id && current.IsActive)
            .ToListAsync(cancellationToken);

        foreach (var freeze in activeFreezes)
        {
            freeze.IsActive = false;
            freeze.EndsAtUtc = DateTime.UtcNow;
        }

        user.IsFrozen = false;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(new AuditLogEntry
        {
            ActorUserId = CurrentUserId,
            SubjectUserId = user.Id,
            Category = "admin",
            Action = "admin.freeze.release",
            EntityType = nameof(User),
            EntityId = user.Id.ToString(),
            TargetDisplayName = user.DisplayName,
            Metadata = BuildUserActionMetadata("manual unfreeze")
        }, cancellationToken);

        return NoContent();
    }

    [Authorize(Policy = PermissionNames.ModerationReview)]
    [HttpPost("moderation/{moderationResultId:guid}/review")]
    public async Task<ActionResult> ReviewModerationResult(
        Guid moderationResultId,
        ReviewModerationResultRequest request,
        CancellationToken cancellationToken)
    {
        var action = NormalizeReviewAction(request.Action);
        if (action == null)
        {
            return BadRequest("Action must be Dismiss, Warn, Freeze, or Escalate.");
        }

        var moderationResult = await _dbContext.ModerationResults
            .Include(result => result.User)
            .FirstOrDefaultAsync(result => result.Id == moderationResultId, cancellationToken);

        if (moderationResult == null)
        {
            return NotFound("Moderation result not found.");
        }

        var user = moderationResult.User ?? await _dbContext.Users.FirstOrDefaultAsync(current => current.Id == moderationResult.UserId, cancellationToken);
        if (user == null)
        {
            return NotFound("User not found.");
        }

        var reason = NormalizeReason(request.Reason, moderationResult.Reason ?? moderationResult.ContentSnapshot);
        switch (action)
        {
            case "Dismiss":
                moderationResult.IsEscalated = false;
                await _dbContext.SaveChangesAsync(cancellationToken);
                await _auditService.LogAsync(new AuditLogEntry
                {
                    ActorUserId = CurrentUserId,
                    SubjectUserId = user.Id,
                    Category = "moderation",
                    Action = "moderation.dismiss",
                    EntityType = nameof(ModerationResult),
                    EntityId = moderationResult.Id.ToString(),
                    TargetDisplayName = user.DisplayName,
                    Metadata = BuildModerationReviewMetadata(moderationResult, user.Id, reason)
                }, cancellationToken);
                break;
            case "Warn":
                moderationResult.IsEscalated = false;
                _dbContext.Warnings.Add(new Warning
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    Reason = reason,
                    IssuedAtUtc = DateTime.UtcNow
                });
                await _dbContext.SaveChangesAsync(cancellationToken);
                await _auditService.LogAsync(new AuditLogEntry
                {
                    ActorUserId = CurrentUserId,
                    SubjectUserId = user.Id,
                    Category = "moderation",
                    Action = "moderation.warn",
                    EntityType = nameof(ModerationResult),
                    EntityId = moderationResult.Id.ToString(),
                    TargetDisplayName = user.DisplayName,
                    Metadata = BuildModerationReviewMetadata(moderationResult, user.Id, reason)
                }, cancellationToken);
                return NoContent();
            case "Freeze":
                var canFreeze = await _authorizationService.AuthorizeAsync(User, PermissionNames.ModerationFreeze);
                if (!canFreeze.Succeeded)
                {
                    return Forbid();
                }

                await FreezeUserForModerationAsync(user, moderationResult, reason, cancellationToken);
                return NoContent();
            case "Escalate":
                moderationResult.IsEscalated = true;
                moderationResult.EscalatedAtUtc = DateTime.UtcNow;
                moderationResult.EscalationNote = reason;
                await _dbContext.SaveChangesAsync(cancellationToken);
                await _auditService.LogAsync(new AuditLogEntry
                {
                    ActorUserId = CurrentUserId,
                    SubjectUserId = user.Id,
                    Category = "moderation",
                    Action = "moderation.escalate",
                    EntityType = nameof(ModerationResult),
                    EntityId = moderationResult.Id.ToString(),
                    TargetDisplayName = user.DisplayName,
                    Metadata = BuildModerationReviewMetadata(moderationResult, user.Id, reason)
                }, cancellationToken);
                break;
        }

        return NoContent();
    }

    private async Task<UserBadge> EnsureDefaultBadgeAsync(CancellationToken cancellationToken)
    {
        var badge = await _dbContext.UserBadges
            .FirstOrDefaultAsync(current => current.Id == ClubMembershipDefaults.MemberBadgeId, cancellationToken);

        if (badge != null)
        {
            badge.Name = ClubMembershipDefaults.MemberBadgeName;
            badge.DepartmentId = null;
            badge.IsArchived = false;
            return badge;
        }

        badge = new UserBadge
        {
            Id = ClubMembershipDefaults.MemberBadgeId,
            Name = ClubMembershipDefaults.MemberBadgeName,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            IsArchived = false
        };

        _dbContext.UserBadges.Add(badge);
        return badge;
    }

    private async Task<Department?> FindActiveDepartmentAsync(Guid? departmentId, CancellationToken cancellationToken)
    {
        if (departmentId is not Guid id)
        {
            return null;
        }

        return await _dbContext.Departments
            .FirstOrDefaultAsync(department => department.Id == id && !department.IsArchived, cancellationToken);
    }

    private static DepartmentResponse MapDepartment(Department department)
    {
        return new DepartmentResponse
        {
            Id = department.Id,
            Name = department.Name,
            Description = department.Description,
            CreatedAtUtc = department.CreatedAtUtc,
            UpdatedAtUtc = department.UpdatedAtUtc,
            CreatedById = department.CreatedById,
            CreatedByName = department.CreatedBy?.DisplayName ?? department.CreatedBy?.UserName,
            IsArchived = department.IsArchived,
            UserCount = department.Users.Count
        };
    }

    private static BadgeResponse MapBadge(UserBadge badge)
    {
        return new BadgeResponse
        {
            Id = badge.Id,
            Name = badge.Name,
            DepartmentId = badge.DepartmentId,
            DepartmentName = badge.Department?.Name,
            CreatedAtUtc = badge.CreatedAtUtc,
            UpdatedAtUtc = badge.UpdatedAtUtc,
            IsArchived = badge.IsArchived,
            IsDefault = badge.Id == ClubMembershipDefaults.MemberBadgeId,
            UserCount = badge.Users.Count
        };
    }

    private static AdminEventEvaluationEventResponse MapEventEvaluationEvent(CommunityEvent eventItem)
    {
        var evaluations = eventItem.Evaluations.ToList();

        return new AdminEventEvaluationEventResponse
        {
            EventId = eventItem.Id,
            Title = eventItem.Title,
            Location = eventItem.Location,
            StartsAtUtc = eventItem.StartsAtUtc,
            CreatedByName = eventItem.CreatedByUser?.DisplayName ?? eventItem.CreatedByUser?.UserName ?? "Moderator",
            GoingCount = eventItem.Participants.Count(participant => participant.Status == "Going" || participant.Status == "GoingInterested"),
            InterestedCount = eventItem.Participants.Count(participant => participant.Status == "Interested" || participant.Status == "GoingInterested"),
            CommentsCount = eventItem.Comments.Count,
            EvaluationCount = evaluations.Count,
            AverageRating = evaluations.Count == 0 ? null : Math.Round(evaluations.Average(evaluation => evaluation.Rating), 2),
            FeedbackCount = evaluations.Count(evaluation => !string.IsNullOrWhiteSpace(evaluation.Feedback)),
            LatestEvaluationAtUtc = evaluations.Count == 0
                ? null
                : evaluations.Max(evaluation => evaluation.UpdatedAtUtc ?? evaluation.CreatedAtUtc),
            RatingBreakdown = Enumerable.Range(1, 5)
                .Select(rating => evaluations.Count(evaluation => evaluation.Rating == rating))
                .ToList()
        };
    }

    private static AdminEventEvaluationFeedbackResponse MapEventEvaluationFeedback(EventEvaluation evaluation)
    {
        return new AdminEventEvaluationFeedbackResponse
        {
            Id = evaluation.Id,
            EventId = evaluation.EventId,
            EventTitle = evaluation.Event?.Title ?? "Event",
            UserId = evaluation.UserId,
            UserName = evaluation.User?.DisplayName ?? evaluation.User?.UserName ?? "Member",
            Rating = evaluation.Rating,
            Feedback = evaluation.Feedback?.Trim() ?? string.Empty,
            CreatedAtUtc = evaluation.CreatedAtUtc,
            UpdatedAtUtc = evaluation.UpdatedAtUtc
        };
    }

    private static string? NormalizeRequiredText(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string NormalizeOptionalText(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    private async Task<int> GetActiveUserCountAsync(DateTime startUtc, CancellationToken cancellationToken)
    {
        var seeds = new List<ActivitySeed>();

        seeds.AddRange(await _dbContext.Posts
            .AsNoTracking()
            .Where(post => post.CreatedAtUtc >= startUtc)
            .Select(post => new ActivitySeed
            {
                Day = post.CreatedAtUtc.Date,
                UserId = post.UserId
            })
            .ToListAsync(cancellationToken));

        seeds.AddRange(await _dbContext.PostComments
            .AsNoTracking()
            .Where(comment => comment.CreatedAtUtc >= startUtc)
            .Select(comment => new ActivitySeed
            {
                Day = comment.CreatedAtUtc.Date,
                UserId = comment.UserId
            })
            .ToListAsync(cancellationToken));

        seeds.AddRange(await _dbContext.PostReactions
            .AsNoTracking()
            .Where(reaction => reaction.CreatedAtUtc >= startUtc)
            .Select(reaction => new ActivitySeed
            {
                Day = reaction.CreatedAtUtc.Date,
                UserId = reaction.UserId
            })
            .ToListAsync(cancellationToken));

        seeds.AddRange(await _dbContext.DirectMessages
            .AsNoTracking()
            .Where(message => message.CreatedAtUtc >= startUtc)
            .Select(message => new ActivitySeed
            {
                Day = message.CreatedAtUtc.Date,
                UserId = message.SenderUserId
            })
            .ToListAsync(cancellationToken));

        seeds.AddRange(await _dbContext.DirectMessages
            .AsNoTracking()
            .Where(message => message.CreatedAtUtc >= startUtc)
            .Select(message => new ActivitySeed
            {
                Day = message.CreatedAtUtc.Date,
                UserId = message.RecipientUserId
            })
            .ToListAsync(cancellationToken));

        return seeds
            .Where(seed => seed.UserId != Guid.Empty)
            .Select(seed => seed.UserId)
            .Distinct()
            .Count();
    }

    private async Task<List<AdminTrendPointResponse>> BuildDailyActivityTrendAsync(DateTime startUtc, CancellationToken cancellationToken)
    {
        var seeds = new List<ActivitySeed>();

        seeds.AddRange(await _dbContext.Posts
            .AsNoTracking()
            .Where(post => post.CreatedAtUtc >= startUtc)
            .Select(post => new ActivitySeed
            {
                Day = post.CreatedAtUtc.Date,
                UserId = post.UserId
            })
            .ToListAsync(cancellationToken));

        seeds.AddRange(await _dbContext.PostComments
            .AsNoTracking()
            .Where(comment => comment.CreatedAtUtc >= startUtc)
            .Select(comment => new ActivitySeed
            {
                Day = comment.CreatedAtUtc.Date,
                UserId = comment.UserId
            })
            .ToListAsync(cancellationToken));

        seeds.AddRange(await _dbContext.PostReactions
            .AsNoTracking()
            .Where(reaction => reaction.CreatedAtUtc >= startUtc)
            .Select(reaction => new ActivitySeed
            {
                Day = reaction.CreatedAtUtc.Date,
                UserId = reaction.UserId
            })
            .ToListAsync(cancellationToken));

        seeds.AddRange(await _dbContext.DirectMessages
            .AsNoTracking()
            .Where(message => message.CreatedAtUtc >= startUtc)
            .Select(message => new ActivitySeed
            {
                Day = message.CreatedAtUtc.Date,
                UserId = message.SenderUserId
            })
            .ToListAsync(cancellationToken));

        seeds.AddRange(await _dbContext.DirectMessages
            .AsNoTracking()
            .Where(message => message.CreatedAtUtc >= startUtc)
            .Select(message => new ActivitySeed
            {
                Day = message.CreatedAtUtc.Date,
                UserId = message.RecipientUserId
            })
            .ToListAsync(cancellationToken));

        var days = Enumerable.Range(0, 7)
            .Select(offset => startUtc.AddDays(offset).Date)
            .ToList();

        return days
            .Select(day => new AdminTrendPointResponse
            {
                Label = day.ToString("MMM d", CultureInfo.InvariantCulture),
                Value = seeds.Where(seed => seed.Day == day).Select(seed => seed.UserId).Distinct().Count(),
                SecondaryValue = 0
            })
            .ToList();
    }

    private async Task<List<AdminTrendPointResponse>> BuildDailyModerationTrendAsync(DateTime startUtc, CancellationToken cancellationToken)
    {
        var moderationResults = await _dbContext.ModerationResults
            .AsNoTracking()
            .Where(result => result.CreatedAtUtc >= startUtc && result.Action != "Allow")
            .Select(result => new
            {
                Day = result.CreatedAtUtc.Date,
                result.Id
            })
            .ToListAsync(cancellationToken);

        var reviewLogs = await _dbContext.AuditLogs
            .AsNoTracking()
            .Where(log => log.CreatedAtUtc >= startUtc && log.EntityType == nameof(ModerationResult) && log.Action.StartsWith("moderation."))
            .Select(log => new
            {
                Day = log.CreatedAtUtc.Date,
                log.EntityId
            })
            .ToListAsync(cancellationToken);

        var days = Enumerable.Range(0, 7)
            .Select(offset => startUtc.AddDays(offset).Date)
            .ToList();

        return days
            .Select(day => new AdminTrendPointResponse
            {
                Label = day.ToString("MMM d", CultureInfo.InvariantCulture),
                Value = moderationResults.Count(result => result.Day == day),
                SecondaryValue = reviewLogs.Count(log => log.Day == day)
            })
            .ToList();
    }

    private async Task<List<AdminStatisticsTrendPointResponse>> BuildStatisticsTrendAsync(
        DateTime startUtc,
        int days,
        CancellationToken cancellationToken)
    {
        var endUtc = startUtc.AddDays(days);

        var users = await _dbContext.Users
            .AsNoTracking()
            .Where(user => user.CreatedAtUtc >= startUtc && user.CreatedAtUtc < endUtc)
            .Select(user => new
            {
                Day = user.CreatedAtUtc.Date
            })
            .ToListAsync(cancellationToken);

        var moderationResults = await _dbContext.ModerationResults
            .AsNoTracking()
            .Where(result => result.CreatedAtUtc >= startUtc && result.CreatedAtUtc < endUtc && !AllowActionAliases.Contains(result.Action))
            .Select(result => new
            {
                Day = result.CreatedAtUtc.Date,
                result.Action,
                result.UserId
            })
            .ToListAsync(cancellationToken);

        var freezes = await _dbContext.Freezes
            .AsNoTracking()
            .Where(freeze => freeze.StartsAtUtc >= startUtc && freeze.StartsAtUtc < endUtc)
            .Select(freeze => new
            {
                Day = freeze.StartsAtUtc.Date,
                freeze.UserId
            })
            .ToListAsync(cancellationToken);

        var appeals = await _dbContext.Appeals
            .AsNoTracking()
            .Where(appeal =>
                (appeal.CreatedAtUtc >= startUtc && appeal.CreatedAtUtc < endUtc) ||
                (appeal.ResolvedAtUtc != null && appeal.ResolvedAtUtc >= startUtc && appeal.ResolvedAtUtc < endUtc))
            .Select(appeal => new
            {
                CreatedDay = appeal.CreatedAtUtc.Date,
                ResolvedDay = appeal.ResolvedAtUtc.HasValue ? appeal.ResolvedAtUtc.Value.Date : (DateTime?)null,
                appeal.Status
            })
            .ToListAsync(cancellationToken);

        var eventEvaluations = await _dbContext.EventEvaluations
            .AsNoTracking()
            .Where(evaluation => evaluation.CreatedAtUtc >= startUtc && evaluation.CreatedAtUtc < endUtc)
            .Select(evaluation => new
            {
                Day = evaluation.CreatedAtUtc.Date,
                evaluation.Rating
            })
            .ToListAsync(cancellationToken);

        return Enumerable.Range(0, days)
            .Select(offset => startUtc.AddDays(offset).Date)
            .Select(day =>
            {
                var blockedFromModeration = moderationResults
                    .Where(result => result.Day == day && BlockActionAliases.Contains(result.Action))
                    .Select(result => result.UserId);
                var blockedFromFreezes = freezes
                    .Where(freeze => freeze.Day == day)
                    .Select(freeze => freeze.UserId);

                return new AdminStatisticsTrendPointResponse
                {
                    Date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    Users = users.Count(user => user.Day == day),
                    Flagged = moderationResults.Count(result => result.Day == day),
                    Blocked = blockedFromModeration.Concat(blockedFromFreezes).Where(id => id != Guid.Empty).Distinct().Count(),
                    AppealsSubmitted = appeals.Count(appeal => appeal.CreatedDay == day),
                    AppealsResolved = appeals.Count(appeal => appeal.ResolvedDay == day && appeal.Status != "Open"),
                    EventEvaluations = eventEvaluations.Count(evaluation => evaluation.Day == day),
                    EventAverageRating = eventEvaluations.Any(evaluation => evaluation.Day == day)
                        ? Math.Round(eventEvaluations.Where(evaluation => evaluation.Day == day).Average(evaluation => evaluation.Rating), 2)
                        : null
                };
            })
            .ToList();
    }

    private static (string Range, int Days) NormalizeStatisticsRange(string? range)
    {
        return range?.Trim().ToLowerInvariant() switch
        {
            "7d" => ("7d", 7),
            "30d" => ("30d", 30),
            "90d" => ("90d", 90),
            _ => ("30d", 30)
        };
    }

    private async Task FreezeUserForModerationAsync(
        User user,
        ModerationResult moderationResult,
        string reason,
        CancellationToken cancellationToken)
    {
        var freeze = await _dbContext.Freezes
            .FirstOrDefaultAsync(current => current.UserId == user.Id && current.IsActive, cancellationToken);

        if (freeze == null)
        {
            freeze = new Freeze
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Reason = reason,
                StartsAtUtc = DateTime.UtcNow,
                IsActive = true
            };
            _dbContext.Freezes.Add(freeze);
        }
        else
        {
            freeze.Reason = reason;
            freeze.IsActive = true;
        }

        moderationResult.IsEscalated = false;
        user.IsFrozen = true;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(new AuditLogEntry
        {
            ActorUserId = CurrentUserId,
            SubjectUserId = user.Id,
            Category = "moderation",
            Action = "moderation.freeze",
            EntityType = nameof(ModerationResult),
            EntityId = moderationResult.Id.ToString(),
            TargetDisplayName = user.DisplayName,
            Metadata = BuildModerationReviewMetadata(moderationResult, user.Id, reason)
        }, cancellationToken);
    }

    private static string NormalizeReason(string? reason, string fallback)
    {
        return string.IsNullOrWhiteSpace(reason)
            ? fallback
            : reason.Trim();
    }

    private static string? NormalizeReviewAction(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "dismiss" => "Dismiss",
            "warn" => "Warn",
            "freeze" => "Freeze",
            "escalate" => "Escalate",
            _ => null
        };
    }

    private static IReadOnlyDictionary<string, string?> BuildUserActionMetadata(string reason)
    {
        return new Dictionary<string, string?>
        {
            ["reason"] = reason
        };
    }

    private static IReadOnlyDictionary<string, string?> BuildModerationReviewMetadata(
        ModerationResult moderationResult,
        Guid userId,
        string reason)
    {
        return new Dictionary<string, string?>
        {
            ["userId"] = userId.ToString(),
            ["reason"] = reason,
            ["contentType"] = moderationResult.ContentType,
            ["contentId"] = moderationResult.ContentId.ToString(),
            ["score"] = moderationResult.Score.ToString("F3", CultureInfo.InvariantCulture),
            ["action"] = moderationResult.Action,
            ["flags"] = moderationResult.Flags.Count == 0 ? "none" : string.Join(',', moderationResult.Flags)
        };
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
            Comments = post.Comments
                .OrderBy(comment => comment.CreatedAtUtc)
                .Select(comment => new PostCommentResponse
                {
                    Id = comment.Id,
                    UserId = comment.UserId,
                    AuthorName = comment.User?.DisplayName ?? comment.User?.UserName ?? "Member",
                    Content = comment.Content,
                    CreatedAtUtc = comment.CreatedAtUtc
                })
                .ToList()
        };
    }

    private sealed class ActivitySeed
    {
        public DateTime Day { get; set; }

        public Guid UserId { get; set; }
    }

    private async Task<List<AdminUserRiskSummaryResponse>> BuildUserRiskSummariesAsync(
        IReadOnlyCollection<User> users,
        CancellationToken cancellationToken)
    {
        if (users.Count == 0)
        {
            return [];
        }

        var userIds = users
            .Select(user => user.Id)
            .Distinct()
            .ToList();

        var moderationSignals = await _dbContext.ModerationResults
            .AsNoTracking()
            .Where(result => userIds.Contains(result.UserId) && result.Action != "Allow")
            .Select(result => new ModerationSignalSeed
            {
                UserId = result.UserId,
                ContentType = result.ContentType,
                CreatedAtUtc = result.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        var warnings = await _dbContext.Warnings
            .AsNoTracking()
            .Where(warning => userIds.Contains(warning.UserId))
            .Select(warning => new WarningSeed
            {
                UserId = warning.UserId,
                IssuedAtUtc = warning.IssuedAtUtc
            })
            .ToListAsync(cancellationToken);

        var freezes = await _dbContext.Freezes
            .AsNoTracking()
            .Where(freeze => userIds.Contains(freeze.UserId))
            .Select(freeze => new FreezeSeed
            {
                UserId = freeze.UserId,
                StartsAtUtc = freeze.StartsAtUtc
            })
            .ToListAsync(cancellationToken);

        var appeals = await _dbContext.Appeals
            .AsNoTracking()
            .Where(appeal => userIds.Contains(appeal.UserId))
            .Select(appeal => new AppealSeed
            {
                UserId = appeal.UserId,
                Status = appeal.Status,
                CreatedAtUtc = appeal.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        var moderationLookup = moderationSignals.ToLookup(signal => signal.UserId);
        var warningLookup = warnings.ToLookup(warning => warning.UserId);
        var freezeLookup = freezes.ToLookup(freeze => freeze.UserId);
        var appealLookup = appeals.ToLookup(appeal => appeal.UserId);

        return users
            .Select(user => BuildUserRiskSummary(
                user,
                moderationLookup[user.Id].ToList(),
                warningLookup[user.Id].ToList(),
                freezeLookup[user.Id].ToList(),
                appealLookup[user.Id].ToList()))
            .ToList();
    }

    private static AdminUserRiskSummaryResponse BuildUserRiskSummary(
        User user,
        IReadOnlyList<ModerationSignalSeed> moderationSignals,
        IReadOnlyList<WarningSeed> warnings,
        IReadOnlyList<FreezeSeed> freezes,
        IReadOnlyList<AppealSeed> appeals)
    {
        var reportableSignals = moderationSignals
            .Where(signal => IsReportableContentType(signal.ContentType))
            .ToList();

        var flaggedPostCount = reportableSignals.Count(signal => NormalizeRiskContentType(signal.ContentType) == "post");
        var flaggedCommentCount = reportableSignals.Count(signal => NormalizeRiskContentType(signal.ContentType) == "comment");
        var flaggedDirectMessageCount = reportableSignals.Count(signal => NormalizeRiskContentType(signal.ContentType) == "directmessage");
        var reportCount = reportableSignals.Count;
        var warningCount = warnings.Count;
        var freezeCount = freezes.Count;
        var appealCount = appeals.Count;
        var openAppealCount = appeals.Count(appeal => string.Equals(appeal.Status, "Open", StringComparison.OrdinalIgnoreCase));
        var hasBeenFrozen = user.IsFrozen || freezeCount > 0;
        var violationChannels = reportableSignals
            .Select(signal => NormalizeRiskContentType(signal.ContentType))
            .Where(kind => !string.IsNullOrWhiteSpace(kind))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var repeatViolationPattern =
            reportCount >= 3 ||
            warningCount >= 2 ||
            freezeCount >= 2 ||
            flaggedDirectMessageCount >= 2 ||
            violationChannels >= 2;

        var lastViolation = ResolveLastViolation(reportableSignals, warnings, freezes);
        var riskFactors = BuildRiskFactors(
            reportCount,
            warningCount,
            freezeCount,
            openAppealCount,
            flaggedDirectMessageCount,
            hasBeenFrozen,
            repeatViolationPattern);

        var riskScore = ComputeRiskScore(
            user.IsFrozen,
            hasBeenFrozen,
            reportCount,
            warningCount,
            freezeCount,
            flaggedDirectMessageCount,
            repeatViolationPattern,
            lastViolation.Timestamp);

        return new AdminUserRiskSummaryResponse
        {
            Id = user.Id,
            UserName = user.UserName,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Gender = user.Gender,
            Age = user.Age,
            AvatarUrl = Infrastructure.UserAvatarHelper.Resolve(user.AvatarUrl, user.Gender),
            Role = user.Role,
            DepartmentId = user.DepartmentId,
            DepartmentName = user.Department?.Name ?? ClubMembershipDefaults.UnassignedDepartmentName,
            BadgeId = user.BadgeId == Guid.Empty ? ClubMembershipDefaults.MemberBadgeId : user.BadgeId,
            BadgeName = user.Badge?.Name ?? ClubMembershipDefaults.MemberBadgeName,
            IsFrozen = user.IsFrozen,
            CreatedAtUtc = user.CreatedAtUtc,
            ReportCount = reportCount,
            WarningCount = warningCount,
            FreezeCount = freezeCount,
            AppealCount = appealCount,
            OpenAppealCount = openAppealCount,
            FlaggedPostCount = flaggedPostCount,
            FlaggedCommentCount = flaggedCommentCount,
            FlaggedDirectMessageCount = flaggedDirectMessageCount,
            HasBeenFrozen = hasBeenFrozen,
            RepeatViolationPattern = repeatViolationPattern,
            RiskScore = riskScore,
            RiskLevel = ResolveRiskLevel(riskScore),
            LastViolationAtUtc = lastViolation.Timestamp,
            LastViolationLabel = lastViolation.Label,
            RiskFactors = riskFactors
        };
    }

    private static (DateTime? Timestamp, string? Label) ResolveLastViolation(
        IReadOnlyList<ModerationSignalSeed> reportableSignals,
        IReadOnlyList<WarningSeed> warnings,
        IReadOnlyList<FreezeSeed> freezes)
    {
        var violationEvents = new List<(DateTime Timestamp, string Label)>();

        violationEvents.AddRange(reportableSignals.Select(signal => (
            signal.CreatedAtUtc,
            NormalizeRiskContentType(signal.ContentType) switch
            {
                "post" => "Flagged post",
                "comment" => "Flagged comment",
                "directmessage" => "Flagged direct message",
                _ => "Flagged content"
            })));

        violationEvents.AddRange(warnings.Select(warning => (warning.IssuedAtUtc, "Warning issued")));
        violationEvents.AddRange(freezes.Select(freeze => (freeze.StartsAtUtc, "Account freeze")));

        if (violationEvents.Count == 0)
        {
            return (null, null);
        }

        var latest = violationEvents
            .OrderByDescending(item => item.Timestamp)
            .First();

        return (latest.Timestamp, latest.Label);
    }

    private static IReadOnlyList<string> BuildRiskFactors(
        int reportCount,
        int warningCount,
        int freezeCount,
        int openAppealCount,
        int flaggedDirectMessageCount,
        bool hasBeenFrozen,
        bool repeatViolationPattern)
    {
        var factors = new List<string>();

        if (reportCount > 0)
        {
            factors.Add($"{reportCount} flagged report{(reportCount == 1 ? string.Empty : "s")}");
        }

        if (warningCount > 0)
        {
            factors.Add($"{warningCount} warning{(warningCount == 1 ? string.Empty : "s")}");
        }

        if (hasBeenFrozen)
        {
            factors.Add(freezeCount > 1 ? $"{freezeCount} freeze periods" : "Frozen before");
        }

        if (flaggedDirectMessageCount > 0)
        {
            factors.Add($"{flaggedDirectMessageCount} flagged message{(flaggedDirectMessageCount == 1 ? string.Empty : "s")}");
        }

        if (repeatViolationPattern)
        {
            factors.Add("Repeated violations");
        }

        if (openAppealCount > 0)
        {
            factors.Add($"{openAppealCount} open appeal{(openAppealCount == 1 ? string.Empty : "s")}");
        }

        return factors.Take(5).ToList();
    }

    private static int ComputeRiskScore(
        bool isFrozen,
        bool hasBeenFrozen,
        int reportCount,
        int warningCount,
        int freezeCount,
        int flaggedDirectMessageCount,
        bool repeatViolationPattern,
        DateTime? lastViolationAtUtc)
    {
        var score = 0;

        score += Math.Min(reportCount * 14, 42);
        score += Math.Min(warningCount * 16, 32);
        score += Math.Min(freezeCount * 24, 48);

        if (isFrozen)
        {
            score += 18;
        }
        else if (hasBeenFrozen)
        {
            score += 10;
        }

        if (flaggedDirectMessageCount > 0)
        {
            score += Math.Min(flaggedDirectMessageCount * 6, 12);
        }

        if (repeatViolationPattern)
        {
            score += 12;
        }

        if (lastViolationAtUtc.HasValue)
        {
            var age = DateTime.UtcNow - lastViolationAtUtc.Value;
            if (age <= TimeSpan.FromDays(7))
            {
                score += 12;
            }
            else if (age <= TimeSpan.FromDays(30))
            {
                score += 6;
            }
        }

        return Math.Clamp(score, 0, 100);
    }

    private static string ResolveRiskLevel(int riskScore)
    {
        if (riskScore >= 70)
        {
            return "High";
        }

        if (riskScore >= 35)
        {
            return "Medium";
        }

        return "Low";
    }

    private static bool IsReportableContentType(string contentType)
    {
        return NormalizeRiskContentType(contentType) is "post" or "comment" or "directmessage";
    }

    private static string NormalizeRiskContentType(string? contentType)
    {
        var normalized = contentType?.Trim().ToLowerInvariant() ?? string.Empty;
        return normalized switch
        {
            "post" => "post",
            "comment" => "comment",
            "postcomment" => "comment",
            "directmessage" => "directmessage",
            "message" => "directmessage",
            _ => string.Empty
        };
    }

    private sealed class ModerationSignalSeed
    {
        public Guid UserId { get; init; }

        public string ContentType { get; init; } = string.Empty;

        public DateTime CreatedAtUtc { get; init; }
    }

    private sealed class WarningSeed
    {
        public Guid UserId { get; init; }

        public DateTime IssuedAtUtc { get; init; }
    }

    private sealed class FreezeSeed
    {
        public Guid UserId { get; init; }

        public DateTime StartsAtUtc { get; init; }
    }

    private sealed class AppealSeed
    {
        public Guid UserId { get; init; }

        public string Status { get; init; } = string.Empty;

        public DateTime CreatedAtUtc { get; init; }
    }
}
