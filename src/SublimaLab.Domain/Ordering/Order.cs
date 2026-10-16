using SublimaLab.Domain.Common;

namespace SublimaLab.Domain.Ordering;

/// <summary>
/// An order is an immutable record of what was bought and at what price; only its
/// lifecycle (status, payments, shipment) changes after placement.
/// </summary>
public sealed class Order : Entity
{
    private readonly List<OrderItem> _items = [];
    private readonly List<Payment> _payments = [];
    private readonly List<OrderStatusChange> _statusHistory = [];

    private Order() { }

    public Order(
        string orderNumber,
        Guid userId,
        ContactInfo contact,
        PostalAddress shippingAddress,
        string shippingMethodCode,
        string shippingMethodName,
        string currency,
        IEnumerable<OrderItem> items,
        OrderTotals totals,
        string? discountCode,
        Guid idempotencyKey,
        DateTime placedAt)
    {
        _items.AddRange(items);
        if (_items.Count == 0)
        {
            throw new DomainException("ORDER_EMPTY", "An order must contain at least one item.");
        }

        OrderNumber = orderNumber;
        UserId = userId;
        Contact = contact;
        ShippingAddress = shippingAddress;
        ShippingMethodCode = shippingMethodCode;
        ShippingMethodName = shippingMethodName;
        Currency = currency;
        Subtotal = totals.Subtotal;
        DiscountTotal = totals.Discount;
        ShippingTotal = totals.Shipping;
        TaxTotal = totals.Tax;
        Total = totals.Total;
        DiscountCode = discountCode;
        IdempotencyKey = idempotencyKey;
        PlacedAt = placedAt;
        UpdatedAt = placedAt;
        Status = OrderStatus.Pending;
        _statusHistory.Add(new OrderStatusChange(OrderStatus.Pending, placedAt, "Order placed"));
    }

    public string OrderNumber { get; private set; } = string.Empty;
    public Guid UserId { get; private set; }
    public OrderStatus Status { get; private set; }

    public ContactInfo Contact { get; private set; } = null!;
    public PostalAddress ShippingAddress { get; private set; } = null!;
    public string ShippingMethodCode { get; private set; } = string.Empty;
    public string ShippingMethodName { get; private set; } = string.Empty;

    public string Currency { get; private set; } = string.Empty;
    public decimal Subtotal { get; private set; }
    public decimal DiscountTotal { get; private set; }
    public decimal ShippingTotal { get; private set; }
    public decimal TaxTotal { get; private set; }
    public decimal Total { get; private set; }
    public string? DiscountCode { get; private set; }

    /// <summary>Client-generated key that makes checkout submission idempotent per user.</summary>
    public Guid IdempotencyKey { get; private set; }

    public DateTime PlacedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token, regenerated on every state change.</summary>
    public Guid Version { get; private set; } = Guid.NewGuid();

    public IReadOnlyCollection<OrderItem> Items => _items;
    public IReadOnlyCollection<Payment> Payments => _payments;
    public IReadOnlyCollection<OrderStatusChange> StatusHistory => _statusHistory;
    public Shipment? Shipment { get; private set; }

    public bool IsPaid => Status is not (OrderStatus.Pending or OrderStatus.PaymentPending or OrderStatus.Cancelled);

    public Payment StartPayment(string provider, string providerPaymentId, DateTime now)
    {
        if (Status is not (OrderStatus.Pending or OrderStatus.PaymentPending))
        {
            throw new DomainException("ORDER_NOT_PAYABLE", $"An order in status '{Status}' cannot be paid.");
        }

        foreach (var pending in _payments.Where(p => p.Status == PaymentStatus.Pending))
        {
            pending.MarkCancelled(now);
        }

        var payment = new Payment(Id, provider, providerPaymentId, Total, Currency, now);
        _payments.Add(payment);

        if (Status == OrderStatus.Pending)
        {
            ChangeStatus(OrderStatus.PaymentPending, "Payment started", now);
        }

        return payment;
    }

    public void ConfirmPayment(Guid paymentId, DateTime now)
    {
        var payment = GetPayment(paymentId);
        payment.MarkSucceeded(now);
        ChangeStatus(OrderStatus.Paid, "Payment received", now);
    }

    public void FailPayment(Guid paymentId, string reason, DateTime now)
    {
        GetPayment(paymentId).MarkFailed(reason, now);
        Touch(now);
    }

    public void Cancel(string reason, DateTime now) => ChangeStatus(OrderStatus.Cancelled, reason, now);

    /// <summary>Moves the order forward in its fulfilment lifecycle (administrators).</summary>
    public void AdvanceTo(OrderStatus target, string? note, DateTime now)
    {
        if (target == OrderStatus.Paid)
        {
            throw new DomainException("PAYMENT_REQUIRED", "Orders become paid only through a confirmed payment.");
        }

        if (target == OrderStatus.Shipped)
        {
            throw new DomainException("SHIPMENT_REQUIRED", "Use the ship operation to provide carrier and tracking details.");
        }

        if (target == OrderStatus.Delivered && Shipment is not null)
        {
            Shipment.MarkDelivered(now);
        }

        ChangeStatus(target, note, now);
    }

    public void Ship(string carrier, string trackingNumber, DateTime now)
    {
        if (!OrderStatusTransitions.CanTransition(Status, OrderStatus.Shipped))
        {
            throw InvalidTransition(OrderStatus.Shipped);
        }

        Shipment = new Shipment(Id, carrier, trackingNumber, now);
        ChangeStatus(OrderStatus.Shipped, $"Shipped with {carrier} ({trackingNumber})", now);
    }

    private void ChangeStatus(OrderStatus target, string? note, DateTime now)
    {
        if (!OrderStatusTransitions.CanTransition(Status, target))
        {
            throw InvalidTransition(target);
        }

        Status = target;
        _statusHistory.Add(new OrderStatusChange(target, now, note));
        Touch(now);
    }

    private void Touch(DateTime now)
    {
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }

    private Payment GetPayment(Guid paymentId) =>
        _payments.FirstOrDefault(p => p.Id == paymentId)
        ?? throw new DomainException("PAYMENT_NOT_FOUND", "The payment does not belong to this order.");

    private DomainException InvalidTransition(OrderStatus target) =>
        new("INVALID_STATUS_TRANSITION", $"An order cannot move from '{Status}' to '{target}'.");
}

public sealed record OrderTotals(decimal Subtotal, decimal Discount, decimal Shipping, decimal Tax, decimal Total);
