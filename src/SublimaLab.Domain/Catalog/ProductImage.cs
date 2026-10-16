using SublimaLab.Domain.Common;

namespace SublimaLab.Domain.Catalog;

public sealed class ProductImage : Entity
{
    public Guid ProductId { get; set; }

    /// <summary>Public URL: a static asset path, or <c>/api/product-images/{id}</c> for uploaded photos.</summary>
    public required string Url { get; set; }

    public required string AltText { get; set; }
    public int SortOrder { get; set; }

    /// <summary>Storage key when the image was uploaded by an administrator; null for static assets.</summary>
    public string? StorageKey { get; set; }

    public string? ContentType { get; set; }

    public static string UploadedUrl(Guid imageId) => $"/api/product-images/{imageId}";
}
