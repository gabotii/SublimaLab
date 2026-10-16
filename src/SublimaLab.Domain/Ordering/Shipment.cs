using SublimaLab.Domain.Common;

namespace SublimaLab.Domain.Ordering;

public enum ShipmentStatus
{
    Shipped = 1,
    Delivered = 2,
}

public sealed class Shipment : Entity
{
    private Shipment() { }

    internal Shipment(Guid orderId, string carrier, string trackingNumber, DateTime shippedAt)
    {
        OrderId = orderId;
        Carrier = carrier;
        TrackingNumber = trackingNumber;
        ShippedAt = shippedAt;
        Status = ShipmentStatus.Shipped;
    }

    public Guid OrderId { get; private set; }
    public string Carrier { get; private set; } = string.Empty;
    public string TrackingNumber { get; private set; } = string.Empty;
    public ShipmentStatus Status { get; private set; }
    public DateTime ShippedAt { get; private set; }
    public DateTime? DeliveredAt { get; private set; }

    internal void MarkDelivered(DateTime now)
    {
        Status = ShipmentStatus.Delivered;
        DeliveredAt = now;
    }
}

/// <summary>One entry of the order tracking timeline.</summary>
public sealed class OrderStatusChange : Entity
{
    private OrderStatusChange() { }

    internal OrderStatusChange(OrderStatus status, DateTime changedAt, string? note)
    {
        Status = status;
        ChangedAt = changedAt;
        Note = note;
    }

    public Guid OrderId { get; private set; }
    public OrderStatus Status { get; private set; }
    public DateTime ChangedAt { get; private set; }
    public string? Note { get; private set; }
}
