using TunSociety.Api.Infrastructure;

namespace TunSociety.Api.DTOs.User;

public class UserLookupResponse
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public int? Age { get; set; }
    public string AvatarUrl { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public Guid? DepartmentId { get; set; }
    public string DepartmentName { get; set; } = ClubMembershipDefaults.UnassignedDepartmentName;
    public Guid BadgeId { get; set; } = ClubMembershipDefaults.MemberBadgeId;
    public string BadgeName { get; set; } = ClubMembershipDefaults.MemberBadgeName;
    public DateTime CreatedAtUtc { get; set; }

    public static UserLookupResponse FromEntity(Models.User user)
    {
        return new UserLookupResponse
        {
            Id = user.Id,
            DisplayName = user.DisplayName,
            Email = user.Email,
            Gender = user.Gender,
            Age = user.Age,
            AvatarUrl = UserAvatarHelper.Resolve(user.AvatarUrl, user.Gender),
            Role = user.Role,
            DepartmentId = user.DepartmentId,
            DepartmentName = user.Department?.Name ?? ClubMembershipDefaults.UnassignedDepartmentName,
            BadgeId = user.BadgeId == Guid.Empty ? ClubMembershipDefaults.MemberBadgeId : user.BadgeId,
            BadgeName = user.Badge?.Name ?? ClubMembershipDefaults.MemberBadgeName,
            CreatedAtUtc = user.CreatedAtUtc
        };
    }
}
