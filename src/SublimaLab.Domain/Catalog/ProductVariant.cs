using SublimaLab.Domain.Common;

namespace SublimaLab.Domain.Catalog;

/// <summary>A purchasable combination of options (colour / size) with its own SKU.</summary>
public sealed class ProductVariant : Entity
{
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }

    public required string Sku { get; set; }
    public required string ColorName { get; set; }

    /// <summary>Hex colour (#RRGGBB) used to tint the product in previews.</summary>
    public required string ColorHex { get; set; }

    /// <summary>Size label (S, M, 11 oz...). Null for one-size products.</summary>
    public string? Size { get; set; }

    /// <summary>Amount added to (or subtracted from) the product base price.</summary>
    public decimal PriceAdjustment { get; set; }

    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    public string Label => Size is null ? ColorName : $"{ColorName} / {Size}";
}
