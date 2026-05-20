using TunSociety.Api.Models;

namespace TunSociety.Api.DTOs.Profile;

public class AlbumDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? CoverImageUrl { get; set; }
    public int PhotoCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public static AlbumDto FromEntity(Album album)
    {
        var coverImageUrl = !string.IsNullOrWhiteSpace(album.CoverImageUrl)
            ? album.CoverImageUrl
            : album.Photos
                .OrderByDescending(photo => photo.CreatedAtUtc)
                .FirstOrDefault(photo => photo.MediaType == "Image")
                ?.MediaUrl;

        return new AlbumDto
        {
            Id = album.Id,
            UserId = album.UserId,
            Name = album.Name,
            CoverImageUrl = coverImageUrl,
            PhotoCount = album.Photos.Count,
            CreatedAtUtc = album.CreatedAtUtc
        };
    }
}
