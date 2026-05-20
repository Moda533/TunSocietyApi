using Microsoft.AspNetCore.Http;

namespace TunSociety.Api.DTOs.User;

public class UploadAvatarRequest
{
    public IFormFile? Avatar { get; set; }
}
