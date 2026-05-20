using TunSociety.Api.Models;

namespace TunSociety.Api.DTOs.Profile;

public class GroupPreviewDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? CoverImageUrl { get; set; }
    public string Visibility { get; set; } = "Public";
    public int MemberCount { get; set; }

    public static GroupPreviewDto FromEntity(Group group)
    {
        return new GroupPreviewDto
        {
            Id = group.Id,
            Name = group.Name,
            CoverImageUrl = group.CoverImageUrl,
            Visibility = group.Visibility,
            MemberCount = group.MemberCount
        };
    }
}
