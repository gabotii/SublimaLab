using SublimaLab.Domain.Common;

namespace SublimaLab.Domain.Catalog;

public sealed class ProductCategory : Entity, IAuditable
{
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<Product> Products { get; } = [];

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
