using TunSociety.Api.Models;

namespace TunSociety.Api.DTOs.Profile;

public class ProfilePhotoDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? AlbumId { get; set; }
    public string MediaUrl { get; set; } = string.Empty;
    public string MediaType { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public static ProfilePhotoDto FromEntity(Photo photo)
    {
        return new ProfilePhotoDto
        {
            Id = photo.Id,
            UserId = photo.UserId,
            AlbumId = photo.AlbumId,
            MediaUrl = photo.MediaUrl,
            MediaType = photo.MediaType,
            ContentType = photo.ContentType,
            OriginalFileName = photo.OriginalFileName,
            SizeBytes = photo.SizeBytes,
            CreatedAtUtc = photo.CreatedAtUtc
        };
    }
}
