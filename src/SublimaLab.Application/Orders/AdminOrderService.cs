using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SublimaLab.Application.Abstractions;
using SublimaLab.Application.Common;
using SublimaLab.Domain.Ordering;

namespace SublimaLab.Application.Orders;

public sealed record AdminOrderQuery(OrderStatus? Status, string? Search, int Page = 1, int PageSize = 20);

public sealed record AdminOrderSummaryDto(
    Guid Id,
    string OrderNumber,
    OrderStatus Status,
    DateTime PlacedAt,
    string CustomerName,
    string CustomerEmail,
    int ItemCount,
    bool HasCustomization,
    decimal Total,
    string Currency);

/// <summary>An order as administrators see it: the customer and the statuses it can move to next.</summary>
public sealed record AdminOrderDetailDto(OrderDto Order, Guid CustomerId, IReadOnlyList<OrderStatus> NextStatuses, bool CanShip, bool HasCustomization);

public sealed record UpdateOrderStatusRequest(OrderStatus Status, string? Note);

public sealed record ShipOrderRequest(string Carrier, string TrackingNumber);

public sealed class UpdateOrderStatusRequestValidator : AbstractValidator<UpdateOrderStatusRequest>
{
    public UpdateOrderStatusRequestValidator()
    {
        RuleFor(r => r.Status).IsInEnum();
        RuleFor(r => r.Note).MaximumLength(500);
    }
}

public sealed class ShipOrderRequestValidator : AbstractValidator<ShipOrderRequest>
{
    public ShipOrderRequestValidator()
    {
        RuleFor(r => r.Carrier).NotEmpty().MaximumLength(60);
        RuleFor(r => r.TrackingNumber).NotEmpty().MaximumLength(100).Matches("^[A-Za-z0-9-]+$");
    }
}

/// <summary>Order management for administrators. Authorization is enforced at the API layer.</summary>
public sealed class AdminOrderService(
    IAppDbContext db,
    OrderService orders,
    IValidator<UpdateOrderStatusRequest> statusValidator,
    IValidator<ShipOrderRequest> shipValidator,
    TimeProvider clock,
    ILogger<AdminOrderService> logger)
{
    public async Task<PagedResult<AdminOrderSummaryDto>> ListAsync(AdminOrderQuery query, CancellationToken cancellationToken)
    {
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var orderQuery = db.Orders.AsNoTracking();

        if (query.Status is { } status)
        {
            orderQuery = orderQuery.Where(o => o.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLowerInvariant();
            orderQuery = orderQuery.Where(o =>
                o.OrderNumber.ToLower().Contains(term) ||
                o.Contact.Email.ToLower().Contains(term) ||
                o.Contact.FullName.ToLower().Contains(term));
        }

        var total = await orderQuery.CountAsync(cancellationToken);
        var items = await orderQuery
            .OrderByDescending(o => o.PlacedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new AdminOrderSummaryDto(
                o.Id,
                o.OrderNumber,
                o.Status,
                o.PlacedAt,
                o.Contact.FullName,
                o.Contact.Email,
                o.Items.Sum(i => i.Quantity),
                o.Items.Any(i => i.ProductionSpec != null),
                o.Total,
                o.Currency))
            .ToListAsync(cancellationToken);

        return new PagedResult<AdminOrderSummaryDto>(items, page, pageSize, total);
    }

    public async Task<AdminOrderDetailDto> GetAsync(Guid id, CancellationToken cancellationToken) =>
        ToDetail(await orders.LoadOrders().AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, cancellationToken) ?? throw new NotFoundException("Order"));

    private static AdminOrderDetailDto ToDetail(Order order)
    {
        var next = OrderStatusTransitions.NextStatuses(order.Status);
        return new AdminOrderDetailDto(
            order.ToDto(),
            order.UserId,
            next.Where(s => s is not (OrderStatus.Paid or OrderStatus.Shipped or OrderStatus.PaymentPending)).ToList(),
            next.Contains(OrderStatus.Shipped),
            order.Items.Any(i => i.Design is not null));
    }

    public async Task<AdminOrderDetailDto> UpdateStatusAsync(Guid id, UpdateOrderStatusRequest request, CancellationToken cancellationToken)
    {
        await statusValidator.ValidateOrThrowAsync(request, cancellationToken);
        var order = await FindAsync(id, cancellationToken);
        var previous = order.Status;

        if (request.Status == OrderStatus.Cancelled)
        {
            order.Cancel(string.IsNullOrWhiteSpace(request.Note) ? "Cancelled by SublimaLab" : request.Note.Trim(), clock.GetUtcNow().UtcDateTime);
        }
        else
        {
            order.AdvanceTo(request.Status, request.Note?.Trim(), clock.GetUtcNow().UtcDateTime);
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Order {OrderNumber} moved from {Previous} to {Status}", order.OrderNumber, previous, order.Status);
        return ToDetail(order);
    }

    public async Task<AdminOrderDetailDto> ShipAsync(Guid id, ShipOrderRequest request, CancellationToken cancellationToken)
    {
        await shipValidator.ValidateOrThrowAsync(request, cancellationToken);
        var order = await FindAsync(id, cancellationToken);

        order.Ship(request.Carrier.Trim(), request.TrackingNumber.Trim().ToUpperInvariant(), clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Order {OrderNumber} shipped with {Carrier}", order.OrderNumber, request.Carrier);
        return ToDetail(order);
    }

    private async Task<Order> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await orders.LoadOrders().FirstOrDefaultAsync(o => o.Id == id, cancellationToken)
        ?? throw new NotFoundException("Order");
}
