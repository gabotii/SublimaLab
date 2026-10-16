using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SublimaLab.Application.Abstractions;
using SublimaLab.Application.Common;
using SublimaLab.Domain.Uploads;

namespace SublimaLab.Application.Uploads;

public sealed record UploadDto(
    Guid Id,
    string Url,
    string Kind,
    string FileName,
    string ContentType,
    long SizeBytes,
    int Width,
    int Height,
    string? QualityWarning);

public sealed record UploadContent(Stream Content, string ContentType, string FileName);

public static class UploadUrls
{
    public static string Content(Guid uploadId) => $"/api/uploads/{uploadId}/content";
}

/// <summary>
/// Accepts customer images. Nothing the client says about the file is trusted: the type is
/// determined from the binary signature, dimensions are read from the image header, the
/// storage key is generated server-side and the original name is kept for display only.
/// The stored file is re-encoded from the decoded pixels, so it carries no client metadata.
/// </summary>
public sealed class UploadService(
    IAppDbContext db,
    IFileStorage storage,
    IImageInspector imageInspector,
    IImageSanitizer imageSanitizer,
    ICurrentUser currentUser,
    IClientSession session,
    IOptions<UploadOptions> options,
    TimeProvider clock,
    ILogger<UploadService> logger)
{
    private static readonly Dictionary<string, ImageFormat> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = ImageFormat.Png,
        [".jpg"] = ImageFormat.Jpeg,
        [".jpeg"] = ImageFormat.Jpeg,
        [".webp"] = ImageFormat.WebP,
    };

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/webp",
    };

    private readonly UploadOptions _options = options.Value;

    public async Task<UploadDto> UploadAsync(Stream content, string fileName, string? declaredContentType, long length, UploadKind kind, CancellationToken cancellationToken)
    {
        var maxBytes = kind == UploadKind.Preview ? _options.MaxPreviewBytes : _options.MaxArtworkBytes;
        if (length <= 0)
        {
            throw Invalid("The file is empty.");
        }

        if (length > maxBytes)
        {
            throw Invalid($"The file is too large. The maximum size is {maxBytes / (1024 * 1024)} MB.");
        }

        var extension = Path.GetExtension(fileName);
        if (!AllowedExtensions.TryGetValue(extension, out var expectedFormat))
        {
            throw Invalid("Only PNG, JPG and WebP images are supported.");
        }

        if (declaredContentType is null || !AllowedContentTypes.Contains(declaredContentType))
        {
            throw Invalid("Only PNG, JPG and WebP images are supported.");
        }

        // Buffer once so we can sniff, hash and store the exact same bytes.
        await using var buffer = new MemoryStream(capacity: (int)Math.Min(length, maxBytes));
        await CopyWithLimitAsync(content, buffer, maxBytes, cancellationToken);
        buffer.Position = 0;

        var image = imageInspector.Inspect(buffer);
        if (image is null || image.Format != expectedFormat || !string.Equals(image.ContentType, declaredContentType, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Rejected upload {FileName}: content does not match its declared type {ContentType}", SafeFileName(fileName), declaredContentType);
            StoreMetrics.Uploads.Add(1, new("kind", kind.ToString()), new("outcome", "rejected"));
            throw Invalid("The file content does not match a valid PNG, JPG or WebP image.");
        }

        ValidateDimensions(image);

        buffer.Position = 0;
        var sanitized = await imageSanitizer.SanitizeAsync(buffer, image, cancellationToken);
        if (sanitized is null)
        {
            logger.LogWarning("Rejected upload {FileName}: the image could not be decoded", SafeFileName(fileName));
            StoreMetrics.Uploads.Add(1, new("kind", kind.ToString()), new("outcome", "rejected"));
            throw Invalid("The image appears to be damaged and could not be read. Try exporting it again.");
        }

        image = sanitized.Info;
        await using var clean = new MemoryStream(sanitized.Content, writable: false);
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(sanitized.Content));
        var storageKey = await storage.SaveAsync(clean, image.Extension, cancellationToken);

        var userId = currentUser.UserId;
        var upload = new ArtworkUpload(
            userId,
            userId is null ? session.GetOrCreateSessionId() : null,
            kind,
            storageKey,
            SafeFileName(fileName),
            image.ContentType,
            sanitized.Content.LongLength,
            image.Width,
            image.Height,
            sha256,
            clock.GetUtcNow().UtcDateTime);

        db.Uploads.Add(upload);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Stored {Kind} upload {UploadId} ({Width}x{Height}, {Size} bytes)", kind, upload.Id, image.Width, image.Height, sanitized.Content.Length);
        StoreMetrics.Uploads.Add(1, new("kind", kind.ToString()), new("outcome", "accepted"));
        return ToDto(upload);
    }

    public async Task<UploadDto> GetAsync(Guid id, CancellationToken cancellationToken) =>
        ToDto(await FindAccessibleAsync(id, cancellationToken));

    public async Task<UploadContent> OpenContentAsync(Guid id, CancellationToken cancellationToken)
    {
        var upload = await FindAccessibleAsync(id, cancellationToken);
        var stream = await storage.OpenReadAsync(upload.StorageKey, cancellationToken);
        if (stream is null)
        {
            logger.LogError("Upload {UploadId} has metadata but its file is missing from storage", id);
            throw new NotFoundException("Upload");
        }

        return new UploadContent(stream, upload.ContentType, upload.OriginalFileName);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var sessionId = session.SessionId;
        var upload = await db.Uploads.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

        if (upload is null || !upload.IsAccessibleBy(userId, sessionId))
        {
            throw new NotFoundException("Upload");
        }

        if (upload.IsRetained || await IsReferencedByOwnerAsync(upload, cancellationToken))
        {
            throw new ConflictException("UPLOAD_IN_USE", "This image is used by a saved design, cart item or order and cannot be deleted.");
        }

        db.Uploads.Remove(upload);
        await db.SaveChangesAsync(cancellationToken);
        await storage.DeleteAsync(upload.StorageKey, cancellationToken);
    }

    private async Task<bool> IsReferencedByOwnerAsync(ArtworkUpload upload, CancellationToken cancellationToken)
    {
        // Design documents are JSON, so references are checked in memory over the owner's
        // (small) set of saved designs and cart lines.
        var designs = upload.OwnerUserId is { } ownerId
            ? await db.Designs.AsNoTracking().Where(d => d.UserId == ownerId).Select(d => new { d.Document, d.PreviewUploadId }).ToListAsync(cancellationToken)
            : [];

        var cartItems = await db.Carts
            .AsNoTracking()
            .Where(c => (upload.OwnerUserId != null && c.UserId == upload.OwnerUserId) || (upload.SessionId != null && c.SessionId == upload.SessionId))
            .SelectMany(c => c.Items)
            .Select(i => new { i.Design, i.PreviewUploadId })
            .ToListAsync(cancellationToken);

        return designs.Any(d => d.PreviewUploadId == upload.Id || d.Document.ReferencedUploadIds().Contains(upload.Id)) ||
               cartItems.Any(i => i.PreviewUploadId == upload.Id || (i.Design?.ReferencedUploadIds().Contains(upload.Id) ?? false));
    }

    private async Task<ArtworkUpload> FindAccessibleAsync(Guid id, CancellationToken cancellationToken)
    {
        var upload = await db.Uploads.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        var allowed = upload is not null && (currentUser.IsAdmin || upload.IsAccessibleBy(currentUser.UserId, session.SessionId));
        return allowed ? upload! : throw new NotFoundException("Upload");
    }

    private void ValidateDimensions(ImageInfo image)
    {
        if (image.Width < _options.MinDimensionPx || image.Height < _options.MinDimensionPx)
        {
            throw Invalid($"The image is too small. It must be at least {_options.MinDimensionPx}×{_options.MinDimensionPx} pixels.");
        }

        if (image.Width > _options.MaxDimensionPx || image.Height > _options.MaxDimensionPx ||
            (long)image.Width * image.Height > _options.MaxMegapixels * 1_000_000L)
        {
            throw Invalid($"The image is too large. Use at most {_options.MaxDimensionPx} pixels per side and {_options.MaxMegapixels} megapixels.");
        }
    }

    private UploadDto ToDto(ArtworkUpload upload) => new(
        upload.Id,
        UploadUrls.Content(upload.Id),
        upload.Kind.ToString(),
        upload.OriginalFileName,
        upload.ContentType,
        upload.SizeBytes,
        upload.WidthPx,
        upload.HeightPx,
        upload.Kind == UploadKind.Artwork && Math.Max(upload.WidthPx, upload.HeightPx) < _options.LowResolutionWarningPx
            ? Customization.DesignRules.LowResolutionMessage
            : null);

    /// <summary>Keeps a display-only version of the client file name; never used for paths.</summary>
    internal static string SafeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName.Replace('\\', '/'));
        var cleaned = new string(name.Where(c => !char.IsControl(c) && !Path.GetInvalidFileNameChars().Contains(c) && c is not ('<' or '>' or '"')).ToArray()).Trim();
        if (cleaned.Length > 120)
        {
            cleaned = cleaned[^120..];
        }

        return string.IsNullOrWhiteSpace(cleaned) ? "image" : cleaned;
    }

    private static async Task CopyWithLimitAsync(Stream source, Stream destination, long maxBytes, CancellationToken cancellationToken)
    {
        var chunk = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(chunk, cancellationToken)) > 0)
        {
            total += read;
            if (total > maxBytes)
            {
                throw Invalid($"The file is too large. The maximum size is {maxBytes / (1024 * 1024)} MB.");
            }

            await destination.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
    }

    private static RequestValidationException Invalid(string message) => RequestValidationException.For("file", message);
}
