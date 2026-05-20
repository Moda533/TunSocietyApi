using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TunSociety.Api.Services;

namespace TunSociety.Api.Controllers;

[ApiController]
[Route("api/avatars")]
public class AvatarsController : ControllerBase
{
    private readonly AvatarStorageService _avatarStorageService;

    public AvatarsController(AvatarStorageService avatarStorageService)
    {
        _avatarStorageService = avatarStorageService;
    }

    [HttpGet("{fileName}")]
    [AllowAnonymous]
    public IActionResult Get(string fileName)
    {
        var stream = _avatarStorageService.OpenAvatarReadStream(fileName, out var contentType);
        if (stream == null)
        {
            return NotFound();
        }

        return File(stream, contentType);
    }
}
