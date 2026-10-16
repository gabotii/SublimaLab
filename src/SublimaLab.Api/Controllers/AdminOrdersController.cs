using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SublimaLab.Application.Admin;
using SublimaLab.Application.Common;
using SublimaLab.Application.Orders;
using SublimaLab.Domain.Ordering;

namespace SublimaLab.Api.Controllers;

[ApiController]
[Authorize(Policy = Policies.AdminOnly)]
[Route("api/admin/orders")]
public sealed class AdminOrdersController(AdminOrderService orders, ProductionArtworkService artwork) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<AdminOrderSummaryDto>>> List(
        [FromQuery] OrderStatus? status,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        Ok(await orders.ListAsync(new AdminOrderQuery(status, search, page, pageSize), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AdminOrderDetailDto>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await orders.GetAsync(id, cancellationToken));

    [HttpPut("{id:guid}/status")]
    public async Task<ActionResult<AdminOrderDetailDto>> UpdateStatus(Guid id, UpdateOrderStatusRequest request, CancellationToken cancellationToken) =>
        Ok(await orders.UpdateStatusAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/ship")]
    public async Task<ActionResult<AdminOrderDetailDto>> Ship(Guid id, ShipOrderRequest request, CancellationToken cancellationToken) =>
        Ok(await orders.ShipAsync(id, request, cancellationToken));

    /// <summary>Downloads the production package (designs, specs, original artwork) as a ZIP.</summary>
    [HttpGet("{id:guid}/artwork")]
    public async Task<IActionResult> DownloadArtwork(Guid id, CancellationToken cancellationToken)
    {
        var (zip, fileName) = await artwork.BuildAsync(id, cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return File(zip, "application/zip", fileName);
    }
}
