using SublimaLab.Domain.Common;

namespace SublimaLab.Domain.Customization;

/// <summary>
/// Per-product customization rules. Supporting a new customizable product means adding
/// configuration (print areas, fees, 3D model) rather than writing product-specific code.
/// </summary>
public sealed class CustomizationConfiguration : Entity
{
    public Guid ProductId { get; set; }

    public bool IsEnabled { get; set; } = true;
    public CustomizerKind Kind { get; set; }

    /// <summary>Relative URL of the GLB/GLTF model used by the 3D preview.</summary>
    public string? ModelUrl { get; set; }

    /// <summary>Fee charged per unit when the design has at least one element.</summary>
    public decimal BaseFee { get; set; }

    /// <summary>Fee per unit for every print area used beyond the first one.</summary>
    public decimal AdditionalPrintAreaFee { get; set; }

    public int MaxElementsPerArea { get; set; } = 10;

    /// <summary>Below this effective DPI the customer receives a print-quality warning.</summary>
    public int MinRecommendedDpi { get; set; } = 150;

    public ICollection<PrintArea> PrintAreas { get; } = [];
}

/// <summary>
/// Tells clients which preview renderer / editor behaviour to use. Validation and pricing
/// are identical for all kinds: they are driven by <see cref="PrintArea"/> configuration.
/// </summary>
public enum CustomizerKind
{
    Apparel = 1,
    Drinkware = 2,
    Accessory = 3,
}
