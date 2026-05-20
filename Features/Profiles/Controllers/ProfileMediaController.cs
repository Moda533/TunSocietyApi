using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TunSociety.Api.Services;

namespace TunSociety.Api.Controllers;

[ApiController]
[Route("api/profile-media")]
public class ProfileMediaController : ControllerBase
{
    private readonly ProfileMediaStorageService _profileMediaStorageService;

    public ProfileMediaController(ProfileMediaStorageService profileMediaStorageService)
    {
        _profileMediaStorageService = profileMediaStorageService;
    }

    [HttpGet("{fileName}")]
    [AllowAnonymous]
    public IActionResult Get(string fileName)
    {
        var stream = _profileMediaStorageService.OpenReadStream(fileName, out var contentType);
        if (stream == null)
        {
            return NotFound();
        }

        return File(stream, contentType);
    }
}
