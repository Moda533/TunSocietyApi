namespace TunSociety.Api.Infrastructure;

public static class PermissionNames
{
    public const string UsersRead = "users.read";
    public const string UsersEdit = "users.edit";
    public const string UsersDelete = "users.delete";
    public const string DepartmentsRead = "departments.read";
    public const string DepartmentsManage = "departments.manage";
    public const string BadgesRead = "badges.read";
    public const string BadgesManage = "badges.manage";
    public const string EventsRead = "events.read";
    public const string EventsManage = "events.manage";
    public const string AppealsRead = "appeals.read";
    public const string AppealsReview = "appeals.review";
    public const string ModerationReview = "moderation.review";
    public const string ModerationFreeze = "moderation.freeze";
    public const string ModerationBan = "moderation.ban";
    public const string RolePermissionsRead = "role-permissions.read";
    public const string RolePermissionsManage = "role-permissions.manage";

    public static readonly IReadOnlyList<string> All =
    [
        UsersRead,
        UsersEdit,
        UsersDelete,
        DepartmentsRead,
        DepartmentsManage,
        BadgesRead,
        BadgesManage,
        EventsRead,
        EventsManage,
        AppealsRead,
        AppealsReview,
        ModerationReview,
        ModerationFreeze,
        ModerationBan,
        RolePermissionsRead,
        RolePermissionsManage
    ];

    public static readonly IReadOnlyList<string> SystemRoles =
    [
        RoleNames.Admin,
        RoleNames.Moderator,
        RoleNames.User
    ];

    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> DefaultPermissionsByRole =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [RoleNames.Admin] = All,
            [RoleNames.Moderator] =
            [
                UsersRead,
                DepartmentsRead,
                BadgesRead,
                EventsRead,
                EventsManage,
                AppealsRead,
                AppealsReview,
                ModerationReview,
                ModerationFreeze
            ],
            [RoleNames.User] =
            [
                EventsRead
            ]
        };

    public static bool IsKnown(string permission)
    {
        return All.Contains(permission, StringComparer.Ordinal);
    }
}
