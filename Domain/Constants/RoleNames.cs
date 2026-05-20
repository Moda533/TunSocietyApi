namespace TunSociety.Api.Infrastructure;

public static class RoleNames
{
    public const string User = "User";
    public const string Moderator = "Moderator";
    public const string Admin = "Admin";
    public const string AdminOrModerator = Admin + "," + Moderator;

    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "user" => User,
            "moderator" => Moderator,
            "admin" => Admin,
            _ => null
        };
    }
}
