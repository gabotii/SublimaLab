using SublimaLab.Domain.Common;

namespace SublimaLab.Domain.Uploads;

public enum UploadKind
{
    /// <summary>Customer artwork placed on a design.</summary>
    Artwork = 1,

    /// <summary>A client-rendered preview thumbnail of a design.</summary>
    Preview = 2,
}

/// <summary>
/// Metadata for an uploaded image. The binary lives in file storage under a server-generated
/// key; the original file name is kept only for display and is never used to build paths.
/// Uploads belong either to a user or to an anonymous browser session (guest customization).
/// </summary>
public sealed class ArtworkUpload : Entity
{
    private ArtworkUpload() { }

    public ArtworkUpload(
        Guid? ownerUserId,
        string? sessionId,
        UploadKind kind,
        string storageKey,
        string originalFileName,
        string contentType,
        long sizeBytes,
        int widthPx,
        int heightPx,
        string sha256,
        DateTime createdAt)
    {
        if (ownerUserId is null && string.IsNullOrEmpty(sessionId))
        {
            throw new DomainException("UPLOAD_OWNER_REQUIRED", "An upload must belong to a user or a session.");
        }

        OwnerUserId = ownerUserId;
        SessionId = sessionId;
        Kind = kind;
        StorageKey = storageKey;
        OriginalFileName = originalFileName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        WidthPx = widthPx;
        HeightPx = heightPx;
        Sha256 = sha256;
        CreatedAt = createdAt;
    }

    public Guid? OwnerUserId { get; private set; }
    public string? SessionId { get; private set; }
    public UploadKind Kind { get; private set; }
    public string StorageKey { get; private set; } = string.Empty;
    public string OriginalFileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public int WidthPx { get; private set; }
    public int HeightPx { get; private set; }
    public string Sha256 { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; }

    /// <summary>True once referenced by an order: the file is production artwork and must be kept.</summary>
    public bool IsRetained { get; private set; }

    public bool IsAccessibleBy(Guid? userId, string? sessionId) =>
        (userId is not null && OwnerUserId == userId) ||
        (OwnerUserId is null && !string.IsNullOrEmpty(sessionId) && SessionId == sessionId);

    /// <summary>Transfers a guest upload to the user who just signed in.</summary>
    public void ClaimFor(Guid userId) => OwnerUserId ??= userId;

    public void Retain() => IsRetained = true;
}
