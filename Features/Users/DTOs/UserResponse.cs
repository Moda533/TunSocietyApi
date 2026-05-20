using TunSociety.Api.Models;
using TunSociety.Api.Infrastructure;

namespace TunSociety.Api.DTOs.User;

public class UserResponse
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public int? Age { get; set; }
    public string AvatarUrl { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public Guid? DepartmentId { get; set; }
    public string DepartmentName { get; set; } = ClubMembershipDefaults.UnassignedDepartmentName;
    public Guid BadgeId { get; set; } = ClubMembershipDefaults.MemberBadgeId;
    public string BadgeName { get; set; } = ClubMembershipDefaults.MemberBadgeName;
    public List<string> Permissions { get; set; } = [];
    public bool IsFrozen { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public static UserResponse FromEntity(Models.User user)
    {
        return new UserResponse
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

    public UserResponse WithPermissions(IEnumerable<string> permissions)
    {
        Permissions = permissions
            .Distinct(StringComparer.Ordinal)
            .OrderBy(permission => permission)
            .ToList();

        return this;
    }
}
