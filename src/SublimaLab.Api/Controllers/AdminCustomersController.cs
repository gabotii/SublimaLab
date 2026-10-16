using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SublimaLab.Application.Admin;
using SublimaLab.Application.Common;

namespace SublimaLab.Api.Controllers;

[ApiController]
[Authorize(Policy = Policies.AdminOnly)]
[Route("api/admin/customers")]
public sealed class AdminCustomersController(AdminCustomerService customers) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<AdminCustomerSummaryDto>>> List([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default) =>
        Ok(await customers.ListAsync(search, page, pageSize, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AdminCustomerDto>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await customers.GetAsync(id, cancellationToken));

    /// <summary>Locks (blocks sign-in and ends sessions) or unlocks a customer account.</summary>
    [HttpPut("{id:guid}/lock")]
    public async Task<ActionResult<AdminCustomerDto>> SetLock(Guid id, SetLockRequest request, CancellationToken cancellationToken) =>
        Ok(await customers.SetLockedAsync(id, request.Locked, cancellationToken));
}
