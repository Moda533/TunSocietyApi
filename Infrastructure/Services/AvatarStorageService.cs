using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using System.Text;

namespace TunSociety.Api.Services;

public class AvatarStorageService
{
    private const string AvatarRoutePrefix = "/api/avatars";
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bmp",
        ".gif",
        ".jpeg",
        ".jpg",
        ".jpe",
        ".jfif",
        ".heic",
        ".heif",
        ".avif",
        ".png",
        ".webp"
    };

    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();

    private readonly string _avatarRootPath;

    public AvatarStorageService(IWebHostEnvironment environment)
    {
        _avatarRootPath = Path.Combine(environment.ContentRootPath, "App_Data", "avatars");
        Directory.CreateDirectory(_avatarRootPath);
    }

    public async Task<string> SaveAvatarAsync(Guid userId, IFormFile file, CancellationToken cancellationToken)
    {
        var extension = ResolveExtension(file);
        if (extension == null)
        {
            throw new InvalidOperationException("Please choose a JPG, PNG, GIF, WEBP, BMP, HEIC, HEIF, or AVIF image.");
        }

        var fileName = $"{userId:N}_{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}{extension}";
        var filePath = Path.Combine(_avatarRootPath, fileName);

        await using (var stream = File.Create(filePath))
        {
            await file.CopyToAsync(stream, cancellationToken);
        }

        return $"{AvatarRoutePrefix}/{Uri.EscapeDataString(fileName)}";
    }

    public Stream? OpenAvatarReadStream(string fileName, out string contentType)
    {
        var safeFileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName))
        {
            contentType = "application/octet-stream";
            return null;
        }

        var filePath = Path.Combine(_avatarRootPath, safeFileName);
        if (!File.Exists(filePath))
        {
            contentType = "application/octet-stream";
            return null;
        }

        var resolvedContentType = "application/octet-stream";
        if (ContentTypeProvider.TryGetContentType(filePath, out var detectedContentType) &&
            !string.IsNullOrWhiteSpace(detectedContentType))
        {
            resolvedContentType = detectedContentType;
        }
        else
        {
            resolvedContentType = Path.GetExtension(filePath).ToLowerInvariant() switch
            {
                ".avif" => "image/avif",
                ".heic" => "image/heic",
                ".heif" => "image/heif",
                ".jfif" => "image/jpeg",
                ".jpe" => "image/jpeg",
                _ => resolvedContentType
            };
        }

        contentType = resolvedContentType;

        return File.OpenRead(filePath);
    }

    public void DeleteManagedAvatar(string? avatarUrl)
    {
        var normalized = avatarUrl?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        var pathWithoutQuery = normalized.Split('?', '#')[0];
        if (!pathWithoutQuery.StartsWith($"{AvatarRoutePrefix}/", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var fileName = Path.GetFileName(Uri.UnescapeDataString(pathWithoutQuery[(AvatarRoutePrefix.Length + 1)..]));
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        var filePath = Path.Combine(_avatarRootPath, fileName);
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
            // Best effort cleanup. Leave the file in place if the OS refuses deletion.
        }
    }

    private static string? ResolveExtension(IFormFile file)
    {
        var extension = Path.GetExtension(file.FileName);
        if (!string.IsNullOrWhiteSpace(extension) && AllowedExtensions.Contains(extension))
        {
            return extension.ToLowerInvariant();
        }

        var contentType = file.ContentType?.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            var extensionFromContentType = contentType switch
            {
                "image/bmp" or "image/x-ms-bmp" => ".bmp",
                "image/gif" => ".gif",
                "image/jpeg" or "image/jpg" or "image/jpe" or "image/pjpeg" => ".jpg",
                "image/jfif" => ".jfif",
                "image/heic" => ".heic",
                "image/heif" => ".heif",
                "image/avif" => ".avif",
                "image/png" or "image/x-png" => ".png",
                "image/webp" => ".webp",
                _ => null
            };

            if (extensionFromContentType != null)
            {
                return extensionFromContentType;
            }
        }

        return DetectExtensionFromSignature(file);
    }

    private static string? DetectExtensionFromSignature(IFormFile file)
    {
        try
        {
            using var stream = file.OpenReadStream();
            Span<byte> header = stackalloc byte[32];
            var bytesRead = stream.Read(header);
            if (bytesRead >= 8 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 &&
                header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
            {
                return ".png";
            }

            if (bytesRead >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            {
                return ".jpg";
            }

            if (bytesRead >= 6 &&
                ((header[0] == (byte)'G' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'8' && header[4] == (byte)'7' && header[5] == (byte)'a') ||
                 (header[0] == (byte)'G' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'8' && header[4] == (byte)'9' && header[5] == (byte)'a')))
            {
                return ".gif";
            }

            if (bytesRead >= 2 && header[0] == (byte)'B' && header[1] == (byte)'M')
            {
                return ".bmp";
            }

            if (bytesRead >= 12 &&
                header[0] == (byte)'R' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'F' &&
                header[8] == (byte)'W' && header[9] == (byte)'E' && header[10] == (byte)'B' && header[11] == (byte)'P')
            {
                return ".webp";
            }

            if (bytesRead >= 12 &&
                header[4] == (byte)'f' && header[5] == (byte)'t' && header[6] == (byte)'y' && header[7] == (byte)'p')
            {
                var brands = GetBrands(header, bytesRead);
                if (brands.Any(brand => brand is "avif" or "avis"))
                {
                    return ".avif";
                }

                if (brands.Any(brand => brand is "heic" or "heix" or "hevc" or "hevx"))
                {
                    return ".heic";
                }

                if (brands.Any(brand => brand is "mif1" or "msf1" or "heif"))
                {
                    return ".heif";
                }
            }
        }
        catch
        {
            // Fall through to a validation failure below.
        }

        return null;
    }

    private static IEnumerable<string> GetBrands(ReadOnlySpan<byte> header, int bytesRead)
    {
        var brands = new List<string>();
        if (bytesRead >= 12)
        {
            var majorBrand = Encoding.ASCII.GetString(header.Slice(8, 4)).TrimEnd('\0').ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(majorBrand))
            {
                brands.Add(majorBrand);
            }
        }

        for (var index = 16; index + 4 <= bytesRead; index += 4)
        {
            var brand = Encoding.ASCII.GetString(header.Slice(index, 4)).TrimEnd('\0').ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(brand))
            {
                brands.Add(brand);
            }
        }

        return brands;
    }
}
