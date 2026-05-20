using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;

namespace TunSociety.Api.Services;

public class ProfileMediaStorageService
{
    private const string MediaRoutePrefix = "/api/profile-media";
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".avif",
        ".bmp",
        ".gif",
        ".heic",
        ".heif",
        ".jpeg",
        ".jpg",
        ".jpe",
        ".jfif",
        ".png",
        ".webp",
        ".m4v",
        ".mov",
        ".mp4",
        ".ogv",
        ".webm"
    };

    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();

    private readonly string _mediaRootPath;

    public ProfileMediaStorageService(IWebHostEnvironment environment)
    {
        _mediaRootPath = Path.Combine(environment.ContentRootPath, "App_Data", "profile-media");
        Directory.CreateDirectory(_mediaRootPath);
    }

    public async Task<ProfileMediaStorageResult> SaveAsync(Guid userId, IFormFile file, CancellationToken cancellationToken)
    {
        var extension = ResolveExtension(file);
        if (extension == null)
        {
            throw new InvalidOperationException("Please choose a supported image or video file.");
        }

        var contentType = ResolveContentType(file, extension);
        var mediaType = contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) ? "Video" : "Image";
        var fileName = $"{userId:N}_{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}{extension}";
        var filePath = Path.Combine(_mediaRootPath, fileName);

        await using (var stream = File.Create(filePath))
        {
            await file.CopyToAsync(stream, cancellationToken);
        }

        return new ProfileMediaStorageResult(
            $"{MediaRoutePrefix}/{Uri.EscapeDataString(fileName)}",
            mediaType,
            contentType,
            Path.GetFileName(file.FileName),
            file.Length);
    }

    public Stream? OpenReadStream(string fileName, out string contentType)
    {
        var safeFileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName))
        {
            contentType = "application/octet-stream";
            return null;
        }

        var filePath = Path.Combine(_mediaRootPath, safeFileName);
        if (!File.Exists(filePath))
        {
            contentType = "application/octet-stream";
            return null;
        }

        contentType = ResolveContentType(filePath);
        return File.OpenRead(filePath);
    }

    public void DeleteManagedMedia(string? mediaUrl)
    {
        var normalized = mediaUrl?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        var pathWithoutQuery = normalized.Split('?', '#')[0];
        if (!pathWithoutQuery.StartsWith($"{MediaRoutePrefix}/", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var fileName = Path.GetFileName(Uri.UnescapeDataString(pathWithoutQuery[(MediaRoutePrefix.Length + 1)..]));
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        var filePath = Path.Combine(_mediaRootPath, fileName);
        if (!File.Exists(filePath))
        {
            return;
        }

        try
        {
            File.Delete(filePath);
        }
        catch
        {
            // Leave the media in place if the OS refuses cleanup after a failed save.
        }
    }

    private static string? ResolveExtension(IFormFile file)
    {
        var extension = Path.GetExtension(file.FileName);
        if (!string.IsNullOrWhiteSpace(extension) && AllowedExtensions.Contains(extension))
        {
            return extension.ToLowerInvariant();
        }

        return file.ContentType?.Trim().ToLowerInvariant() switch
        {
            "image/avif" => ".avif",
            "image/bmp" or "image/x-ms-bmp" => ".bmp",
            "image/gif" => ".gif",
            "image/heic" => ".heic",
            "image/heif" => ".heif",
            "image/jpeg" or "image/jpg" or "image/jpe" or "image/pjpeg" => ".jpg",
            "image/jfif" => ".jfif",
            "image/png" or "image/x-png" => ".png",
            "image/webp" => ".webp",
            "video/mp4" => ".mp4",
            "video/quicktime" => ".mov",
            "video/ogg" => ".ogv",
            "video/webm" => ".webm",
            "video/x-m4v" => ".m4v",
            _ => null
        };
    }

    private static string ResolveContentType(IFormFile file, string extension)
    {
        var contentType = file.ContentType?.Trim();
        if (!string.IsNullOrWhiteSpace(contentType) &&
            (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
             contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)))
        {
            return contentType;
        }

        return ResolveContentType($"file{extension}");
    }

    private static string ResolveContentType(string filePath)
    {
        if (ContentTypeProvider.TryGetContentType(filePath, out var contentType) &&
            !string.IsNullOrWhiteSpace(contentType))
        {
            return contentType;
        }

        return Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".avif" => "image/avif",
            ".heic" => "image/heic",
            ".heif" => "image/heif",
            ".jfif" => "image/jpeg",
            ".jpe" => "image/jpeg",
            ".m4v" => "video/x-m4v",
            ".mov" => "video/quicktime",
            ".ogv" => "video/ogg",
            _ => "application/octet-stream"
        };
    }
}

public record ProfileMediaStorageResult(
    string MediaUrl,
    string MediaType,
    string ContentType,
    string OriginalFileName,
    long SizeBytes);
