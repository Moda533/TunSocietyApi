using TunSociety.Api.Infrastructure;
using AppUser = TunSociety.Api.Models.User;

namespace TunSociety.Api.DTOs.Admin;

public sealed class AdminUserRiskSummaryResponse
{
    public Guid Id { get; init; }

    public string UserName { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Gender { get; init; } = string.Empty;

    public int? Age { get; init; }

    public string AvatarUrl { get; init; } = string.Empty;

    public string Role { get; init; } = string.Empty;

    public Guid? DepartmentId { get; init; }

    public string DepartmentName { get; init; } = ClubMembershipDefaults.UnassignedDepartmentName;

    public Guid BadgeId { get; init; } = ClubMembershipDefaults.MemberBadgeId;

    public string BadgeName { get; init; } = ClubMembershipDefaults.MemberBadgeName;

    public bool IsFrozen { get; init; }

    public DateTime CreatedAtUtc { get; init; }

    public int ReportCount { get; init; }

    public int WarningCount { get; init; }

    public int FreezeCount { get; init; }

    public int AppealCount { get; init; }

    public int OpenAppealCount { get; init; }

    public int FlaggedPostCount { get; init; }

    public int FlaggedCommentCount { get; init; }

    public int FlaggedDirectMessageCount { get; init; }

    public bool HasBeenFrozen { get; init; }

    public bool RepeatViolationPattern { get; init; }

    public int RiskScore { get; init; }

    public string RiskLevel { get; init; } = "Low";

    public DateTime? LastViolationAtUtc { get; init; }

    public string? LastViolationLabel { get; init; }

    public IReadOnlyList<string> RiskFactors { get; init; } = Array.Empty<string>();

    public static AdminUserRiskSummaryResponse FromEntity(AppUser user)
    {
        return new AdminUserRiskSummaryResponse
        {
            Id = user.Id,
            UserName = user.UserName,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Gender = user.Gender,
            Age = user.Age,
            AvatarUrl = UserAvatarHelper.Resolve(user.AvatarUrl, user.Gender),
            Role = user.Role,
            DepartmentId = user.DepartmentId,
            DepartmentName = user.Department?.Name ?? ClubMembershipDefaults.UnassignedDepartmentName,
            BadgeId = user.BadgeId == Guid.Empty ? ClubMembershipDefaults.MemberBadgeId : user.BadgeId,
            BadgeName = user.Badge?.Name ?? ClubMembershipDefaults.MemberBadgeName,
            IsFrozen = user.IsFrozen,
            CreatedAtUtc = user.CreatedAtUtc
        };
    }
}
