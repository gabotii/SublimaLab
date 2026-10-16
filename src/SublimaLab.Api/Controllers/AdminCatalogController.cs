using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using SublimaLab.Application.Admin;
using SublimaLab.Application.Common;

namespace SublimaLab.Api.Controllers;

[ApiController]
[Authorize(Policy = Policies.AdminOnly)]
[Route("api/admin")]
public sealed class AdminCatalogController(AdminCatalogService catalog, AdminDashboardService dashboard, IOutputCacheStore cache) : ControllerBase
{
    private const long MaxImageRequestBytes = 9 * 1024 * 1024;

    [HttpGet("dashboard")]
    public async Task<ActionResult<DashboardDto>> Dashboard(CancellationToken cancellationToken) =>
        Ok(await dashboard.GetAsync(cancellationToken));

    [HttpGet("categories")]
    public async Task<ActionResult<IReadOnlyList<AdminCategoryDto>>> Categories(CancellationToken cancellationToken) =>
        Ok(await catalog.GetCategoriesAsync(cancellationToken));

    [HttpGet("products")]
    public async Task<ActionResult<PagedResult<AdminProductSummaryDto>>> Products(
        [FromQuery] string? search, [FromQuery] Guid? categoryId, [FromQuery] bool? active,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default) =>
        Ok(await catalog.ListAsync(search, categoryId, active, page, pageSize, cancellationToken));

    [HttpGet("products/{id:guid}")]
    public async Task<ActionResult<AdminProductDto>> Product(Guid id, CancellationToken cancellationToken) =>
        Ok(await catalog.GetAsync(id, cancellationToken));

    [HttpPost("products")]
    public async Task<ActionResult<AdminProductDto>> Create(SaveProductRequest request, CancellationToken cancellationToken)
    {
        var product = await catalog.CreateAsync(request, cancellationToken);
        await EvictCatalogAsync(cancellationToken);
        return CreatedAtAction(nameof(Product), new { id = product.Id }, product);
    }

    [HttpPut("products/{id:guid}")]
    public Task<ActionResult<AdminProductDto>> Update(Guid id, SaveProductRequest request, CancellationToken cancellationToken) =>
        ChangedAsync(catalog.UpdateAsync(id, request, cancellationToken), cancellationToken);

    [HttpPut("products/{id:guid}/status")]
    public Task<ActionResult<AdminProductDto>> SetStatus(Guid id, SetActiveRequest request, CancellationToken cancellationToken) =>
        ChangedAsync(catalog.SetActiveAsync(id, request.IsActive, cancellationToken), cancellationToken);

    [HttpPut("products/{id:guid}/variants")]
    public Task<ActionResult<AdminProductDto>> SaveVariants(Guid id, SaveVariantsRequest request, CancellationToken cancellationToken) =>
        ChangedAsync(catalog.SaveVariantsAsync(id, request, cancellationToken), cancellationToken);

    [HttpPut("products/{id:guid}/customization")]
    public Task<ActionResult<AdminProductDto>> SaveCustomization(Guid id, SaveCustomizationRequest request, CancellationToken cancellationToken) =>
        ChangedAsync(catalog.SaveCustomizationAsync(id, request, cancellationToken), cancellationToken);

    /// <summary>Uploads a product photo (multipart field "file", optional "altText").</summary>
    [HttpPost("products/{id:guid}/images")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxImageRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxImageRequestBytes)]
    public async Task<ActionResult<AdminProductDto>> AddImage(Guid id, IFormFile? file, [FromForm] string? altText, CancellationToken cancellationToken)
    {
        if (file is null)
        {
            throw RequestValidationException.For("file", "Choose an image to upload.");
        }

        await using var stream = file.OpenReadStream();
        return await ChangedAsync(catalog.AddImageAsync(id, stream, file.FileName, file.Length, altText, cancellationToken), cancellationToken);
    }

    [HttpDelete("products/{id:guid}/images/{imageId:guid}")]
    public Task<ActionResult<AdminProductDto>> RemoveImage(Guid id, Guid imageId, CancellationToken cancellationToken) =>
        ChangedAsync(catalog.RemoveImageAsync(id, imageId, cancellationToken), cancellationToken);

    /// <summary>Product changes must show up in the storefront immediately, not after the cache expires.</summary>
    private async Task<ActionResult<AdminProductDto>> ChangedAsync(Task<AdminProductDto> change, CancellationToken cancellationToken)
    {
        var product = await change;
        await EvictCatalogAsync(cancellationToken);
        return Ok(product);
    }

    private ValueTask EvictCatalogAsync(CancellationToken cancellationToken) => cache.EvictByTagAsync("catalog", cancellationToken);
}

[ApiController]
[Route("api/product-images")]
public sealed class ProductImagesController(AdminCatalogService catalog) : ControllerBase
{
    /// <summary>Public product photo uploaded by an administrator.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var (content, contentType) = await catalog.OpenImageAsync(id, cancellationToken);
        Response.Headers.CacheControl = "public, max-age=86400";
        Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
        return File(content, contentType);
    }
}
