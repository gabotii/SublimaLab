using Microsoft.AspNetCore.Mvc;
using SublimaLab.Application.Carts;

namespace SublimaLab.Api.Controllers;

/// <summary>Cart for guests (session cookie) and customers. All prices are computed server-side.</summary>
[ApiController]
[Route("api/cart")]
public sealed class CartController(CartService carts) : ControllerBase
{
    /// <param name="shippingMethod">Shipping method used for the estimate (defaults to standard).</param>
    [HttpGet]
    public async Task<ActionResult<CartDto>> Get([FromQuery] string? shippingMethod, CancellationToken cancellationToken) =>
        Ok(await carts.GetAsync(shippingMethod, cancellationToken));

    [HttpPost("items")]
    public async Task<ActionResult<CartDto>> AddItem(AddCartItemRequest request, CancellationToken cancellationToken) =>
        Ok(await carts.AddItemAsync(request, cancellationToken));

    [HttpPut("items/{id:guid}")]
    public async Task<ActionResult<CartDto>> UpdateItem(Guid id, UpdateCartItemRequest request, CancellationToken cancellationToken) =>
        Ok(await carts.UpdateItemAsync(id, request, cancellationToken));

    [HttpDelete("items/{id:guid}")]
    public async Task<ActionResult<CartDto>> RemoveItem(Guid id, CancellationToken cancellationToken) =>
        Ok(await carts.RemoveItemAsync(id, cancellationToken));

    [HttpPost("discount")]
    public async Task<ActionResult<CartDto>> ApplyDiscount(ApplyDiscountRequest request, CancellationToken cancellationToken) =>
        Ok(await carts.ApplyDiscountAsync(request, cancellationToken));

    [HttpDelete("discount")]
    public async Task<ActionResult<CartDto>> RemoveDiscount(CancellationToken cancellationToken) =>
        Ok(await carts.RemoveDiscountAsync(cancellationToken));
}
