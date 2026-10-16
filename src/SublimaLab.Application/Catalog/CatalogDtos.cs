namespace SublimaLab.Application.Catalog;

public sealed record CategoryDto(Guid Id, string Name, string Slug, string Description, string? ImageUrl, int ProductCount);

public sealed record ColorSwatchDto(string Name, string Hex);

public sealed record ProductSummaryDto(
    Guid Id,
    string Name,
    string Slug,
    string ShortDescription,
    string CategorySlug,
    string CategoryName,
    decimal Price,
    string Currency,
    string? ImageUrl,
    string? ImageAlt,
    bool IsCustomizable,
    bool IsFeatured,
    IReadOnlyList<ColorSwatchDto> Colors);

public sealed record ProductImageDto(string Url, string AltText);

public sealed record VariantDto(Guid Id, string Sku, string ColorName, string ColorHex, string? Size, decimal Price);

public sealed record SpecificationDto(string Name, string Value);

public sealed record UvRectDto(decimal X, decimal Y, decimal Width, decimal Height);

public sealed record PrintAreaDto(string Key, string Name, decimal WidthMm, decimal HeightMm, string Shape, UvRectDto Uv);

public sealed record CustomizationDto(
    string Kind,
    string? ModelUrl,
    decimal BaseFee,
    decimal AdditionalPrintAreaFee,
    int MaxElementsPerArea,
    int MinRecommendedDpi,
    IReadOnlyList<PrintAreaDto> PrintAreas,
    IReadOnlyList<string> Fonts);

public sealed record ProductDetailDto(
    Guid Id,
    string Name,
    string Slug,
    string ShortDescription,
    string Description,
    string CategorySlug,
    string CategoryName,
    decimal Price,
    string Currency,
    bool IsCustomizable,
    int ProductionTimeDays,
    IReadOnlyList<ProductImageDto> Images,
    IReadOnlyList<VariantDto> Variants,
    IReadOnlyList<SpecificationDto> Specifications,
    CustomizationDto? Customization);

public enum ProductSort
{
    Featured,
    Newest,
    PriceAsc,
    PriceDesc,
    Name,
}

public sealed record ProductQuery
{
    public string? Search { get; init; }
    public string? Category { get; init; }
    public decimal? MinPrice { get; init; }
    public decimal? MaxPrice { get; init; }
    public bool? Customizable { get; init; }
    public bool? Featured { get; init; }
    public ProductSort Sort { get; init; } = ProductSort.Featured;
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 12;
}
