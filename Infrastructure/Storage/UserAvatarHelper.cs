namespace TunSociety.Api.Infrastructure;

public static class UserAvatarHelper
{
    public const string MaleAvatarPath = "/b.png";
    public const string FemaleAvatarPath = "/g.png";

    public static string Resolve(string? avatarUrl, string? gender)
    {
        var normalizedAvatarUrl = avatarUrl?.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedAvatarUrl) &&
            !string.Equals(normalizedAvatarUrl, MaleAvatarPath, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(normalizedAvatarUrl, FemaleAvatarPath, StringComparison.OrdinalIgnoreCase))
        {
            return normalizedAvatarUrl;
        }

        return ResolveForGender(gender);
    }

    public static string ResolveForGender(string? gender)
    {
        return IsFemale(gender) ? FemaleAvatarPath : MaleAvatarPath;
    }

    public static bool IsValidGender(string? gender)
    {
        return IsFemale(gender) || IsMale(gender);
    }

    private static bool IsFemale(string? gender)
    {
        return string.Equals(gender?.Trim(), "Female", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMale(string? gender)
    {
        return string.Equals(gender?.Trim(), "Male", StringComparison.OrdinalIgnoreCase);
    }
}
