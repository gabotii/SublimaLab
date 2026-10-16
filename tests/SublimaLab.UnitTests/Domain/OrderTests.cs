using SublimaLab.Domain.Common;
using SublimaLab.Domain.Ordering;

namespace SublimaLab.UnitTests.Domain;

public class OrderTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void New_order_is_pending_with_a_timeline_entry()
    {
        var order = CreateOrder();

        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Single(order.StatusHistory);
        Assert.Equal(25.98m, order.Items.Single().LineTotal);
    }

    [Fact]
    public void Order_without_items_is_rejected()
    {
        var error = Assert.Throws<DomainException>(() => CreateOrder(items: []));
        Assert.Equal("ORDER_EMPTY", error.Code);
    }

    [Fact]
    public void Successful_payment_marks_the_order_paid()
    {
        var order = CreateOrder();
        var payment = order.StartPayment("test", "pi_1", Now);

        order.ConfirmPayment(payment.Id, Now.AddMinutes(1));

        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.True(order.IsPaid);
    }

    [Fact]
    public void Failed_payment_keeps_the_order_payable_and_a_new_attempt_cancels_stale_ones()
    {
        var order = CreateOrder();
        var first = order.StartPayment("test", "pi_1", Now);
        order.FailPayment(first.Id, "declined", Now);
        var abandoned = order.StartPayment("test", "pi_2", Now);

        var retry = order.StartPayment("test", "pi_3", Now);
        order.ConfirmPayment(retry.Id, Now);

        Assert.Equal(PaymentStatus.Failed, first.Status);
        Assert.Equal(PaymentStatus.Cancelled, abandoned.Status);
        Assert.Equal(OrderStatus.Paid, order.Status);
    }

    [Fact]
    public void A_payment_cannot_be_confirmed_twice()
    {
        var order = CreateOrder();
        var payment = order.StartPayment("test", "pi_1", Now);
        order.ConfirmPayment(payment.Id, Now);

        Assert.Throws<DomainException>(() => order.ConfirmPayment(payment.Id, Now));
    }

    [Fact]
    public void Paid_orders_cannot_start_new_payments()
    {
        var order = PaidOrder();

        var error = Assert.Throws<DomainException>(() => order.StartPayment("test", "pi_2", Now));
        Assert.Equal("ORDER_NOT_PAYABLE", error.Code);
    }

    [Fact]
    public void Fulfilment_follows_the_lifecycle_and_records_the_shipment()
    {
        var order = PaidOrder();

        order.AdvanceTo(OrderStatus.InProduction, "Printing", Now);
        order.AdvanceTo(OrderStatus.ReadyToShip, null, Now);
        order.Ship("UPS", "1Z999", Now);
        order.AdvanceTo(OrderStatus.Delivered, null, Now.AddDays(2));

        Assert.Equal(OrderStatus.Delivered, order.Status);
        Assert.Equal(ShipmentStatus.Delivered, order.Shipment!.Status);
        Assert.Equal(
            [OrderStatus.Pending, OrderStatus.PaymentPending, OrderStatus.Paid, OrderStatus.InProduction, OrderStatus.ReadyToShip, OrderStatus.Shipped, OrderStatus.Delivered],
            order.StatusHistory.Select(h => h.Status));
    }

    [Theory]
    [InlineData(OrderStatus.InProduction)]
    [InlineData(OrderStatus.Delivered)]
    public void Unpaid_orders_cannot_skip_ahead(OrderStatus target)
    {
        var order = CreateOrder();

        var error = Assert.Throws<DomainException>(() => order.AdvanceTo(target, null, Now));
        Assert.Equal("INVALID_STATUS_TRANSITION", error.Code);
    }

    [Fact]
    public void Administrators_cannot_mark_an_order_paid_without_a_payment()
    {
        var order = CreateOrder();
        order.StartPayment("test", "pi_1", Now);

        var error = Assert.Throws<DomainException>(() => order.AdvanceTo(OrderStatus.Paid, null, Now));
        Assert.Equal("PAYMENT_REQUIRED", error.Code);
    }

    [Fact]
    public void Shipping_requires_an_order_ready_to_ship()
    {
        var order = PaidOrder();

        Assert.Throws<DomainException>(() => order.Ship("UPS", "1Z999", Now));
    }

    [Fact]
    public void Cancelled_orders_are_final()
    {
        var order = CreateOrder();
        order.Cancel("Changed my mind", Now);

        Assert.Throws<DomainException>(() => order.StartPayment("test", "pi_1", Now));
        Assert.Empty(OrderStatusTransitions.NextStatuses(OrderStatus.Cancelled));
    }

    [Fact]
    public void Every_state_change_regenerates_the_concurrency_version()
    {
        var order = CreateOrder();
        var before = order.Version;

        order.StartPayment("test", "pi_1", Now);

        Assert.NotEqual(before, order.Version);
    }

    [Fact]
    public void Order_numbers_are_readable_and_unambiguous()
    {
        var number = OrderNumber.Generate(Now);

        Assert.Matches("^SL-261001-[0-9A-HJKMNP-TV-Z]{6}$", number);
    }

    private static Order PaidOrder()
    {
        var order = CreateOrder();
        var payment = order.StartPayment("test", "pi_paid", Now);
        order.ConfirmPayment(payment.Id, Now);
        return order;
    }

    private static Order CreateOrder(IEnumerable<OrderItem>? items = null) => new(
        "SL-261001-ABCDEF",
        Guid.NewGuid(),
        new ContactInfo { FullName = "Ada Lovelace", Email = "ada@example.com", Phone = "+1 555 0100" },
        new PostalAddress { Line1 = "1 Main St", City = "Springfield", Region = "IL", PostalCode = "62701", Country = "US" },
        "standard",
        "Standard Shipping",
        "USD",
        items ?? [new OrderItem(Guid.NewGuid(), Guid.NewGuid(), "Tee", "SKU", "White / M", "#FFFFFF", null, 2, 6.99m, 6m, null, null, null)],
        new OrderTotals(25.98m, 0m, 4.99m, 2.08m, 33.05m),
        null,
        Guid.NewGuid(),
        Now);
}
