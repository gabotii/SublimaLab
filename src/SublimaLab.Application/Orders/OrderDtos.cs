using SublimaLab.Application.Uploads;
using SublimaLab.Domain.Designs;
using SublimaLab.Domain.Ordering;

namespace SublimaLab.Application.Orders;

public sealed record ContactDto(string FullName, string Email, string Phone);

public sealed record AddressDto(string Line1, string? Line2, string City, string Region, string PostalCode, string Country);

/// <param name="IdempotencyKey">Generated once per checkout attempt by the client; resubmitting returns the same order.</param>
public sealed record PlaceOrderRequest(Guid IdempotencyKey, ContactDto Contact, AddressDto ShippingAddress, string ShippingMethodCode, bool SaveAddress);

public sealed record OrderItemDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string Sku,
    string VariantLabel,
    string ColorHex,
    string? ImageUrl,
    int Quantity,
    decimal UnitPrice,
    decimal CustomizationPrice,
    decimal LineTotal,
    bool IsCustomized,
    DesignDocument? Design,
    string? PreviewUrl);

public sealed record PaymentDto(Guid Id, Guid OrderId, string Provider, PaymentStatus Status, decimal Amount, string Currency, string? FailureReason, DateTime CreatedAt, string? ClientSecret = null);

public sealed record ShipmentDto(string Carrier, string TrackingNumber, ShipmentStatus Status, DateTime ShippedAt, DateTime? DeliveredAt);

public sealed record OrderTimelineEntryDto(OrderStatus Status, DateTime ChangedAt, string? Note);

public sealed record OrderDto(
    Guid Id,
    string OrderNumber,
    OrderStatus Status,
    DateTime PlacedAt,
    ContactDto Contact,
    AddressDto ShippingAddress,
    string ShippingMethodCode,
    string ShippingMethodName,
    string Currency,
    decimal Subtotal,
    decimal Discount,
    string? DiscountCode,
    decimal Shipping,
    decimal Tax,
    decimal Total,
    IReadOnlyList<OrderItemDto> Items,
    IReadOnlyList<PaymentDto> Payments,
    ShipmentDto? Shipment,
    IReadOnlyList<OrderTimelineEntryDto> Timeline,
    bool CanPay,
    bool CanCancel);

public sealed record OrderSummaryDto(
    Guid Id,
    string OrderNumber,
    OrderStatus Status,
    DateTime PlacedAt,
    int ItemCount,
    decimal Total,
    string Currency,
    string? FirstItemImageUrl);

internal static class OrderMapping
{
    public static OrderDto ToDto(this Order order) => new(
        order.Id,
        order.OrderNumber,
        order.Status,
        order.PlacedAt,
        new ContactDto(order.Contact.FullName, order.Contact.Email, order.Contact.Phone),
        new AddressDto(order.ShippingAddress.Line1, order.ShippingAddress.Line2, order.ShippingAddress.City, order.ShippingAddress.Region, order.ShippingAddress.PostalCode, order.ShippingAddress.Country),
        order.ShippingMethodCode,
        order.ShippingMethodName,
        order.Currency,
        order.Subtotal,
        order.DiscountTotal,
        order.DiscountCode,
        order.ShippingTotal,
        order.TaxTotal,
        order.Total,
        order.Items.OrderBy(i => i.Id).Select(i => new OrderItemDto(
            i.Id,
            i.ProductId,
            i.ProductName,
            i.Sku,
            i.VariantLabel,
            i.ColorHex,
            i.ImageUrl,
            i.Quantity,
            i.UnitPrice,
            i.CustomizationPrice,
            i.LineTotal,
            i.Design is not null,
            i.Design,
            i.PreviewUploadId is { } previewId ? UploadUrls.Content(previewId) : null)).ToList(),
        order.Payments.OrderBy(p => p.CreatedAt).Select(p => p.ToDto()).ToList(),
        order.Shipment is { } s ? new ShipmentDto(s.Carrier, s.TrackingNumber, s.Status, s.ShippedAt, s.DeliveredAt) : null,
        order.StatusHistory.OrderBy(h => h.ChangedAt).ThenBy(h => h.Id).Select(h => new OrderTimelineEntryDto(h.Status, h.ChangedAt, h.Note)).ToList(),
        order.Status is OrderStatus.Pending or OrderStatus.PaymentPending,
        order.Status is OrderStatus.Pending or OrderStatus.PaymentPending);

    public static PaymentDto ToDto(this Payment payment, string? clientSecret = null) =>
        new(payment.Id, payment.OrderId, payment.Provider, payment.Status, payment.Amount, payment.Currency, payment.FailureReason, payment.CreatedAt, clientSecret);
}
