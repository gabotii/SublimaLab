using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SublimaLab.Application.Common;
using SublimaLab.Application.Orders;
using SublimaLab.Application.Payments;

namespace SublimaLab.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/orders")]
public sealed class OrdersController(OrderService orders) : ControllerBase
{
    /// <summary>Places an order from the current cart. Idempotent per <c>idempotencyKey</c>.</summary>
    [HttpPost]
    public async Task<ActionResult<OrderDto>> Place(PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        var order = await orders.PlaceOrderAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = order.Id }, order);
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<OrderSummaryDto>>> List([FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken cancellationToken = default) =>
        Ok(await orders.ListMineAsync(page, pageSize, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderDto>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await orders.GetMineAsync(id, cancellationToken));

    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<OrderDto>> Cancel(Guid id, CancellationToken cancellationToken) =>
        Ok(await orders.CancelMineAsync(id, cancellationToken));
}

[ApiController]
[Authorize]
[Route("api/payments")]
public sealed class PaymentsController(PaymentService payments) : ControllerBase
{
    /// <summary>Starts a payment for an order with the configured payment provider.</summary>
    [HttpPost]
    public async Task<ActionResult<PaymentDto>> Create(CreatePaymentRequest request, CancellationToken cancellationToken)
    {
        var payment = await payments.CreateAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, payment);
    }

    /// <summary>Confirms a payment with the provider token collected client-side.</summary>
    [HttpPost("{id:guid}/confirm")]
    public async Task<ActionResult<PaymentOutcomeDto>> Confirm(Guid id, ConfirmPaymentRequest request, CancellationToken cancellationToken) =>
        Ok(await payments.ConfirmAsync(id, request, cancellationToken));
}
