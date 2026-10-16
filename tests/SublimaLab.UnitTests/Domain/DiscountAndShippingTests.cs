using SublimaLab.Domain.Discounts;
using SublimaLab.Domain.Shipping;

namespace SublimaLab.UnitTests.Domain;

public class DiscountAndShippingTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(DiscountType.Percentage, 10, 80, 8)]
    [InlineData(DiscountType.Percentage, 15, 33.33, 5)]
    [InlineData(DiscountType.FixedAmount, 5, 80, 5)]
    [InlineData(DiscountType.FixedAmount, 50, 30, 30)]
    [InlineData(DiscountType.Percentage, 150, 20, 20)]
    public void Discount_amount_is_rounded_and_never_exceeds_the_subtotal(DiscountType type, decimal value, decimal subtotal, decimal expected)
    {
        var discount = new DiscountCode { Code = "X", Type = type, Value = value };

        Assert.Equal(expected, discount.CalculateAmount(subtotal));
    }

    [Fact]
    public void Expired_inactive_exhausted_or_below_minimum_codes_are_ineligible()
    {
        Assert.NotNull(new DiscountCode { Code = "A", ExpiresAt = Now.AddDays(-1) }.GetIneligibilityReason(100, Now));
        Assert.NotNull(new DiscountCode { Code = "B", IsActive = false }.GetIneligibilityReason(100, Now));
        Assert.NotNull(new DiscountCode { Code = "C", MaxRedemptions = 1, RedemptionCount = 1 }.GetIneligibilityReason(100, Now));
        Assert.NotNull(new DiscountCode { Code = "D", MinimumSubtotal = 50 }.GetIneligibilityReason(49.99m, Now));
        Assert.NotNull(new DiscountCode { Code = "E", StartsAt = Now.AddDays(1) }.GetIneligibilityReason(100, Now));
        Assert.Null(new DiscountCode { Code = "F", MinimumSubtotal = 50, ExpiresAt = Now.AddDays(1) }.GetIneligibilityReason(50, Now));
    }

    [Theory]
    [InlineData(49.99, 4.99)]
    [InlineData(50, 0)]
    [InlineData(120, 0)]
    public void Shipping_is_free_from_the_threshold(decimal subtotal, decimal expected)
    {
        var method = new ShippingMethod { Code = "standard", Name = "Standard", Cost = 4.99m, FreeShippingThreshold = 50m };

        Assert.Equal(expected, method.CostFor(subtotal));
    }

    [Fact]
    public void Shipping_without_threshold_is_never_free()
    {
        var method = new ShippingMethod { Code = "express", Name = "Express", Cost = 12.99m };

        Assert.Equal(12.99m, method.CostFor(10_000m));
    }
}
