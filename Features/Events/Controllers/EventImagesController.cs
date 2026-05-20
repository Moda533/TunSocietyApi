using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TunSociety.Api.Services;

namespace TunSociety.Api.Controllers;

[ApiController]
[Route("api/event-images")]
public class EventImagesController : ControllerBase
{
    private readonly EventImageStorageService _eventImageStorageService;

    public EventImagesController(EventImageStorageService eventImageStorageService)
    {
        _eventImageStorageService = eventImageStorageService;
    }

    [HttpGet("{fileName}")]
    [AllowAnonymous]
    public IActionResult Get(string fileName)
    {
        var stream = _eventImageStorageService.OpenEventImageReadStream(fileName, out var contentType);
        if (stream == null)
        {
            return NotFound();
        }

        return File(stream, contentType);
    }
}
