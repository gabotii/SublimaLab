using SublimaLab.Domain.Common;
using SublimaLab.Domain.Customization;

namespace SublimaLab.Domain.Catalog;

/// <summary>
/// A sellable product. Catalog entities are intentionally simple data holders: their rules
/// (non-negative prices, unique slugs...) are validated at the application boundary, while
/// aggregates with real behaviour (Cart, Order) protect their own invariants.
/// </summary>
public sealed class Product : Entity, IAuditable
{
    public Guid CategoryId { get; set; }
    public ProductCategory? Category { get; set; }

    public required string Name { get; set; }
    public required string Slug { get; set; }
    public string ShortDescription { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>Base price before variant adjustments and customization fees.</summary>
    public decimal BasePrice { get; set; }

    public bool IsActive { get; set; } = true;
    public bool IsFeatured { get; set; }

    /// <summary>Business days needed to produce the item before it ships.</summary>
    public int ProductionTimeDays { get; set; } = 2;

    public List<ProductSpecification> Specifications { get; set; } = [];

    public ICollection<ProductVariant> Variants { get; } = [];
    public ICollection<ProductImage> Images { get; } = [];
    public CustomizationConfiguration? Customization { get; set; }

    public bool IsCustomizable => Customization is { IsEnabled: true };

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed record ProductSpecification(string Name, string Value);
