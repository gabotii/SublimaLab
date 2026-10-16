using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using SublimaLab.Application.Catalog;
using SublimaLab.Application.Common;
using SublimaLab.Application.Shipping;

namespace SublimaLab.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class CatalogController(CatalogService catalog, ShippingService shipping) : ControllerBase
{
    public const string CachePolicy = "Catalog";

    [HttpGet("categories")]
    [OutputCache(PolicyName = CachePolicy)]
    public async Task<ActionResult<IReadOnlyList<CategoryDto>>> GetCategories(CancellationToken cancellationToken) =>
        Ok(await catalog.GetCategoriesAsync(cancellationToken));

    /// <summary>Searches active products with filters, sorting and pagination.</summary>
    [HttpGet("products")]
    [OutputCache(PolicyName = CachePolicy)]
    public async Task<ActionResult<PagedResult<ProductSummaryDto>>> GetProducts([FromQuery] ProductQuery query, CancellationToken cancellationToken) =>
        Ok(await catalog.SearchProductsAsync(query, cancellationToken));

    /// <summary>Gets a product by id or slug, including variants and customization settings.</summary>
    [HttpGet("products/{idOrSlug}")]
    [OutputCache(PolicyName = CachePolicy)]
    public async Task<ActionResult<ProductDetailDto>> GetProduct(string idOrSlug, CancellationToken cancellationToken) =>
        Ok(await catalog.GetProductAsync(idOrSlug, cancellationToken));

    [HttpGet("shipping/methods")]
    [OutputCache(PolicyName = CachePolicy)]
    public async Task<ActionResult<IReadOnlyList<ShippingMethodDto>>> GetShippingMethods(CancellationToken cancellationToken) =>
        Ok(await shipping.GetMethodsAsync(cancellationToken));
}
