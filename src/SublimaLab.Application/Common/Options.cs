using System.ComponentModel.DataAnnotations;

namespace SublimaLab.Application.Common;

public sealed class CommerceOptions
{
    public const string SectionName = "Commerce";

    [Required, RegularExpression("^[A-Z]{3}$")]
    public string Currency { get; set; } = "USD";

    /// <summary>Sales tax applied to the discounted merchandise subtotal (0.08 = 8%).</summary>
    [Range(0, 1)]
    public decimal TaxRate { get; set; } = 0.08m;

    [Required]
    public string DefaultShippingMethodCode { get; set; } = "standard";
}

public sealed class UploadOptions
{
    public const string SectionName = "Uploads";

    [Range(1, 100 * 1024 * 1024)]
    public long MaxArtworkBytes { get; set; } = 15 * 1024 * 1024;

    [Range(1, 20 * 1024 * 1024)]
    public long MaxPreviewBytes { get; set; } = 2 * 1024 * 1024;

    [Range(1, 10_000)]
    public int MinDimensionPx { get; set; } = 64;

    [Range(1, 50_000)]
    public int MaxDimensionPx { get; set; } = 12_000;

    /// <summary>Guards against decompression bombs: width * height must stay below this.</summary>
    [Range(1, 500)]
    public int MaxMegapixels { get; set; } = 80;

    /// <summary>Artwork whose longest side is below this gets a generic print-quality warning.</summary>
    [Range(1, 50_000)]
    public int LowResolutionWarningPx { get; set; } = 1500;
}

public sealed class AppUrlOptions
{
    public const string SectionName = "App";

    /// <summary>Public URL of the storefront, used to build links in emails.</summary>
    [Required, Url]
    public string PublicBaseUrl { get; set; } = "https://localhost:7132";
}
