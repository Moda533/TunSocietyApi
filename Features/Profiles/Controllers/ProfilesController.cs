using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TunSociety.Api.Data;
using TunSociety.Api.DTOs.Profile;
using TunSociety.Api.Models;
using TunSociety.Api.Services;

namespace TunSociety.Api.Controllers;

[ApiController]
[Route("api/profiles")]
[Authorize]
public class ProfilesController : AppControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ProfileMediaStorageService _profileMediaStorageService;
    private readonly AuditService _auditService;

    public ProfilesController(
        ApplicationDbContext dbContext,
        ProfileMediaStorageService profileMediaStorageService,
        AuditService auditService)
    {
        _dbContext = dbContext;
        _profileMediaStorageService = profileMediaStorageService;
        _auditService = auditService;
    }

    [HttpGet("{userId:guid}/photos")]
    public async Task<ActionResult<IEnumerable<ProfilePhotoDto>>> GetPhotos(
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (CurrentUserId is null)
        {
            return Unauthorized();
        }

        var userExists = await _dbContext.Users
            .AsNoTracking()
            .AnyAsync(user => user.Id == userId, cancellationToken);
        if (!userExists)
        {
            return NotFound("User not found.");
        }

        var photos = await _dbContext.Photos
            .AsNoTracking()
            .Where(photo => photo.UserId == userId)
            .OrderByDescending(photo => photo.CreatedAtUtc)
            .ThenByDescending(photo => photo.Id)
            .ToListAsync(cancellationToken);

        return Ok(photos.Select(ProfilePhotoDto.FromEntity).ToList());
    }

    [HttpPost("{userId:guid}/photos")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(80 * 1024 * 1024)]
    public async Task<ActionResult<ProfilePhotoDto>> UploadPhoto(
        Guid userId,
        [FromForm] PhotoUploadRequest request,
        CancellationToken cancellationToken)
    {
        var accessError = EnsureResourceAccess(userId, allowAdmin: true);
        if (accessError is not null)
        {
            return accessError;
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);
        if (user == null)
        {
            return NotFound("User not found.");
        }

        var frozenError = EnsureActiveUser(user);
        if (frozenError is not null)
        {
            return frozenError;
        }

        if (request.Media == null || request.Media.Length == 0)
        {
            return BadRequest("Please choose an image or video file.");
        }

        if (request.AlbumId is Guid albumId)
        {
            var albumExists = await _dbContext.Albums.AnyAsync(
                album => album.Id == albumId && album.UserId == userId,
                cancellationToken);
            if (!albumExists)
            {
                return BadRequest("Album not found.");
            }
        }

        ProfileMediaStorageResult? media = null;

        try
        {
            media = await _profileMediaStorageService.SaveAsync(userId, request.Media, cancellationToken);

            var photo = new Photo
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                AlbumId = request.AlbumId,
                MediaUrl = media.MediaUrl,
                MediaType = media.MediaType,
                ContentType = media.ContentType,
                OriginalFileName = media.OriginalFileName,
                SizeBytes = media.SizeBytes,
                CreatedAtUtc = DateTime.UtcNow
            };

            _dbContext.Photos.Add(photo);
            await _dbContext.SaveChangesAsync(cancellationToken);

            await _auditService.LogAsync(
                "profile.photo.upload",
                nameof(Photo),
                photo.Id.ToString(),
                $"ownerUserId={userId};mediaType={photo.MediaType}",
                CurrentUserId,
                cancellationToken);

            return Ok(ProfilePhotoDto.FromEntity(photo));
        }
        catch (InvalidOperationException ex)
        {
            if (media is not null)
            {
                _profileMediaStorageService.DeleteManagedMedia(media.MediaUrl);
            }

            return BadRequest(ex.Message);
        }
        catch
        {
            if (media is not null)
            {
                _profileMediaStorageService.DeleteManagedMedia(media.MediaUrl);
            }

            return StatusCode(StatusCodes.Status500InternalServerError, "Unable to save your profile media.");
        }
    }

    [HttpGet("{userId:guid}/albums")]
    public async Task<ActionResult<IEnumerable<AlbumDto>>> GetAlbums(
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (CurrentUserId is null)
        {
            return Unauthorized();
        }

        var userExists = await _dbContext.Users
            .AsNoTracking()
            .AnyAsync(user => user.Id == userId, cancellationToken);
        if (!userExists)
        {
            return NotFound("User not found.");
        }

        var albums = await _dbContext.Albums
            .AsNoTracking()
            .Include(album => album.Photos)
            .Where(album => album.UserId == userId)
            .OrderByDescending(album => album.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return Ok(albums.Select(AlbumDto.FromEntity).ToList());
    }

    [HttpGet("{userId:guid}/groups")]
    public async Task<ActionResult<IEnumerable<GroupPreviewDto>>> GetGroups(
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (CurrentUserId is null)
        {
            return Unauthorized();
        }

        var userExists = await _dbContext.Users
            .AsNoTracking()
            .AnyAsync(user => user.Id == userId, cancellationToken);
        if (!userExists)
        {
            return NotFound("User not found.");
        }

        var groups = await _dbContext.Groups
            .AsNoTracking()
            .Where(group => group.OwnerUserId == userId && group.Visibility == "Public")
            .OrderBy(group => group.Name)
            .ToListAsync(cancellationToken);

        return Ok(groups.Select(GroupPreviewDto.FromEntity).ToList());
    }
}
