namespace SublimaLab.Domain.Ordering;

public enum OrderStatus
{
    Pending = 1,
    PaymentPending = 2,
    Paid = 3,
    InProduction = 4,
    ReadyToShip = 5,
    Shipped = 6,
    Delivered = 7,
    Cancelled = 8,
}

/// <summary>The order lifecycle as an explicit state machine.</summary>
public static class OrderStatusTransitions
{
    private static readonly Dictionary<OrderStatus, OrderStatus[]> Allowed = new()
    {
        [OrderStatus.Pending] = [OrderStatus.PaymentPending, OrderStatus.Cancelled],
        [OrderStatus.PaymentPending] = [OrderStatus.Paid, OrderStatus.Cancelled],
        [OrderStatus.Paid] = [OrderStatus.InProduction, OrderStatus.Cancelled],
        [OrderStatus.InProduction] = [OrderStatus.ReadyToShip],
        [OrderStatus.ReadyToShip] = [OrderStatus.Shipped],
        [OrderStatus.Shipped] = [OrderStatus.Delivered],
        [OrderStatus.Delivered] = [],
        [OrderStatus.Cancelled] = [],
    };

    public static bool CanTransition(OrderStatus from, OrderStatus to) =>
        Allowed.TryGetValue(from, out var targets) && targets.Contains(to);

    public static IReadOnlyCollection<OrderStatus> NextStatuses(OrderStatus from) =>
        Allowed.TryGetValue(from, out var targets) ? targets : [];
}
