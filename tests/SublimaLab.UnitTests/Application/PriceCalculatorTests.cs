using SublimaLab.Application.Pricing;
using SublimaLab.Tests.Support;

namespace SublimaLab.UnitTests.Application;

public class PriceCalculatorTests
{
    [Fact]
    public void Unit_price_includes_the_variant_adjustment()
    {
        var product = TestData.CustomizableTee(basePrice: 19.99m);
        var xxl = product.Variants.Single(v => v.Size == "XXL");

        Assert.Equal(21.99m, PriceCalculator.UnitPrice(product, xxl));
    }

    [Fact]
    public void Plain_products_have_no_customization_price()
    {
        var product = TestData.CustomizableTee();

        Assert.Equal(0m, PriceCalculator.CustomizationPrice(product.Customization, null));
        Assert.Equal(0m, PriceCalculator.CustomizationPrice(product.Customization, TestData.Design(("front", []))));
    }

    [Fact]
    public void Customization_charges_the_base_fee_plus_each_extra_print_area()
    {
        var product = TestData.CustomizableTee(baseFee: 6m, extraAreaFee: 4m);
        var frontOnly = TestData.Design(("front", [TestData.Text("a"), TestData.Text("b", y: 200)]));
        var frontAndBack = TestData.Design(("front", [TestData.Text("a")]), ("back", [TestData.Text("b")]));

        Assert.Equal(6m, PriceCalculator.CustomizationPrice(product.Customization, frontOnly));
        Assert.Equal(10m, PriceCalculator.CustomizationPrice(product.Customization, frontAndBack));
    }

    [Fact]
    public void Line_total_multiplies_unit_and_customization_by_quantity()
    {
        var product = TestData.CustomizableTee(basePrice: 19.99m, baseFee: 6m);
        var variant = product.Variants.First();

        var line = PriceCalculator.PriceLine(product, variant, TestData.Design(("front", [TestData.Text()])), 3);

        Assert.Equal(new LinePrice(19.99m, 6m, 77.97m), line);
    }

    [Fact]
    public void Totals_apply_tax_to_the_discounted_subtotal_but_not_to_shipping()
    {
        var totals = PriceCalculator.Totals(subtotal: 38.98m, discount: 3.90m, shipping: 4.99m, taxRate: 0.08m);

        Assert.Equal(38.98m, totals.Subtotal);
        Assert.Equal(3.90m, totals.Discount);
        Assert.Equal(2.81m, totals.Tax); // (38.98 - 3.90) * 0.08 = 2.8064
        Assert.Equal(42.88m, totals.Total);
    }

    [Fact]
    public void Discount_can_never_exceed_the_subtotal_or_go_negative()
    {
        Assert.Equal(10m, PriceCalculator.Totals(10m, 25m, 0m, 0.08m).Discount);
        Assert.Equal(0m, PriceCalculator.Totals(10m, -5m, 0m, 0.08m).Discount);
        Assert.Equal(0m, PriceCalculator.Totals(10m, 25m, 0m, 0.08m).Total);
    }
}
