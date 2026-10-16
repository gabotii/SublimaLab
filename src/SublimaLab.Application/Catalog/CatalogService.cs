using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SublimaLab.Application.Abstractions;
using SublimaLab.Application.Common;
using SublimaLab.Application.Customization;
using SublimaLab.Application.Pricing;
using SublimaLab.Domain.Catalog;
using SublimaLab.Domain.Customization;

namespace SublimaLab.Application.Catalog;

/// <summary>Read-side queries for the storefront. All queries are no-tracking projections.</summary>
public sealed class CatalogService(IAppDbContext db, IValidator<ProductQuery> queryValidator, IOptions<CommerceOptions> commerce)
{
    private readonly string _currency = commerce.Value.Currency;

    public async Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(CancellationToken cancellationToken) =>
        await db.Categories
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .Select(c => new CategoryDto(c.Id, c.Name, c.Slug, c.Description, c.ImageUrl, c.Products.Count(p => p.IsActive)))
            .ToListAsync(cancellationToken);

    public async Task<PagedResult<ProductSummaryDto>> SearchProductsAsync(ProductQuery query, CancellationToken cancellationToken)
    {
        await queryValidator.ValidateOrThrowAsync(query, cancellationToken);

        var products = db.Products.AsNoTracking().Where(p => p.IsActive && p.Category!.IsActive);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLowerInvariant();
            products = products.Where(p =>
                p.Name.ToLower().Contains(term) ||
                p.ShortDescription.ToLower().Contains(term) ||
                p.Category!.Name.ToLower().Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            var category = query.Category.Trim().ToLowerInvariant();
            products = products.Where(p => p.Category!.Slug == category);
        }

        if (query.MinPrice is { } min)
        {
            products = products.Where(p => p.BasePrice >= min);
        }

        if (query.MaxPrice is { } max)
        {
            products = products.Where(p => p.BasePrice <= max);
        }

        if (query.Customizable is { } customizable)
        {
            products = products.Where(p => (p.Customization != null && p.Customization.IsEnabled) == customizable);
        }

        if (query.Featured is { } featured)
        {
            products = products.Where(p => p.IsFeatured == featured);
        }

        products = query.Sort switch
        {
            ProductSort.Newest => products.OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Name),
            ProductSort.PriceAsc => products.OrderBy(p => p.BasePrice).ThenBy(p => p.Name),
            ProductSort.PriceDesc => products.OrderByDescending(p => p.BasePrice).ThenBy(p => p.Name),
            ProductSort.Name => products.OrderBy(p => p.Name),
            _ => products.OrderByDescending(p => p.IsFeatured).ThenBy(p => p.Category!.SortOrder).ThenBy(p => p.Name),
        };

        var totalCount = await products.CountAsync(cancellationToken);

        var rows = await products
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Slug,
                p.ShortDescription,
                CategorySlug = p.Category!.Slug,
                CategoryName = p.Category.Name,
                p.BasePrice,
                p.IsFeatured,
                IsCustomizable = p.Customization != null && p.Customization.IsEnabled,
                Image = p.Images.OrderBy(i => i.SortOrder).Select(i => new { i.Url, i.AltText }).FirstOrDefault(),
                Variants = p.Variants.Where(v => v.IsActive).OrderBy(v => v.SortOrder)
                    .Select(v => new { v.ColorName, v.ColorHex, v.PriceAdjustment }).ToList(),
            })
            .ToListAsync(cancellationToken);

        var items = rows.Select(r => new ProductSummaryDto(
                r.Id,
                r.Name,
                r.Slug,
                r.ShortDescription,
                r.CategorySlug,
                r.CategoryName,
                FromPrice(r.BasePrice, r.Variants.Select(v => v.PriceAdjustment)),
                _currency,
                r.Image?.Url,
                r.Image?.AltText,
                r.IsCustomizable,
                r.IsFeatured,
                r.Variants.Select(v => new ColorSwatchDto(v.ColorName, v.ColorHex)).DistinctBy(c => c.Hex).ToList()))
            .ToList();

        return new PagedResult<ProductSummaryDto>(items, query.Page, query.PageSize, totalCount);
    }

    /// <summary>Finds an active product by id or slug.</summary>
    public async Task<ProductDetailDto> GetProductAsync(string idOrSlug, CancellationToken cancellationToken)
    {
        var query = db.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Images)
            .Include(p => p.Variants)
            .Include(p => p.Customization).ThenInclude(c => c!.PrintAreas)
            .Where(p => p.IsActive);

        var product = Guid.TryParse(idOrSlug, out var id)
            ? await query.FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            : await query.FirstOrDefaultAsync(p => p.Slug == idOrSlug.ToLower(), cancellationToken);

        return product is null ? throw new NotFoundException("Product") : ToDetailDto(product);
    }

    private ProductDetailDto ToDetailDto(Product product)
    {
        var variants = product.Variants.Where(v => v.IsActive).OrderBy(v => v.SortOrder).ToList();

        return new ProductDetailDto(
            product.Id,
            product.Name,
            product.Slug,
            product.ShortDescription,
            product.Description,
            product.Category!.Slug,
            product.Category.Name,
            FromPrice(product.BasePrice, variants.Select(v => v.PriceAdjustment)),
            _currency,
            product.IsCustomizable,
            product.ProductionTimeDays,
            product.Images.OrderBy(i => i.SortOrder).Select(i => new ProductImageDto(i.Url, i.AltText)).ToList(),
            variants.Select(v => new VariantDto(v.Id, v.Sku, v.ColorName, v.ColorHex, v.Size, PriceCalculator.UnitPrice(product, v))).ToList(),
            product.Specifications.Select(s => new SpecificationDto(s.Name, s.Value)).ToList(),
            product.IsCustomizable ? ToCustomizationDto(product.Customization!) : null);
    }

    internal static CustomizationDto ToCustomizationDto(CustomizationConfiguration configuration) => new(
        configuration.Kind.ToString(),
        configuration.ModelUrl,
        configuration.BaseFee,
        configuration.AdditionalPrintAreaFee,
        configuration.MaxElementsPerArea,
        configuration.MinRecommendedDpi,
        configuration.PrintAreas
            .OrderBy(a => a.SortOrder)
            .Select(a => new PrintAreaDto(a.Key, a.Name, a.WidthMm, a.HeightMm, a.Shape.ToString(), new UvRectDto(a.UvX, a.UvY, a.UvWidth, a.UvHeight)))
            .ToList(),
        DesignRules.AllowedFonts);

    private static decimal FromPrice(decimal basePrice, IEnumerable<decimal> adjustments)
    {
        var lowestAdjustment = adjustments.DefaultIfEmpty(0m).Min();
        return Domain.Common.Money.Round(Math.Max(basePrice + lowestAdjustment, 0m));
    }
}
