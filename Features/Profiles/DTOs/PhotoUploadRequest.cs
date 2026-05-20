using Microsoft.AspNetCore.Http;

namespace TunSociety.Api.DTOs.Profile;

public class PhotoUploadRequest
{
    public IFormFile? Media { get; set; }
    public Guid? AlbumId { get; set; }
}
