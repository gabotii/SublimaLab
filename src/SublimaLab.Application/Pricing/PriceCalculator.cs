using SublimaLab.Domain.Catalog;
using SublimaLab.Domain.Common;
using SublimaLab.Domain.Customization;
using SublimaLab.Domain.Designs;
using SublimaLab.Domain.Ordering;

namespace SublimaLab.Application.Pricing;

public sealed record LinePrice(decimal UnitPrice, decimal CustomizationPrice, decimal LineTotal);

/// <summary>
/// The single source of truth for prices. Pure functions over catalog data so the same rules
/// are used for cart display, checkout and tests. Client-supplied prices are never read.
/// </summary>
public static class PriceCalculator
{
    public static decimal UnitPrice(Product product, ProductVariant variant) =>
        Money.Round(Math.Max(product.BasePrice + variant.PriceAdjustment, 0m));

    /// <summary>
    /// Customization is charged per unit: a base fee as soon as the design has any element,
    /// plus a fee for every additional print area used (e.g. front + back).
    /// </summary>
    public static decimal CustomizationPrice(CustomizationConfiguration? configuration, DesignDocument? design)
    {
        if (configuration is null || design is null || design.IsEmpty())
        {
            return 0m;
        }

        var extraAreas = Math.Max(design.UsedPrintAreaCount() - 1, 0);
        return Money.Round(configuration.BaseFee + (configuration.AdditionalPrintAreaFee * extraAreas));
    }

    public static LinePrice PriceLine(Product product, ProductVariant variant, DesignDocument? design, int quantity)
    {
        var unit = UnitPrice(product, variant);
        var customization = CustomizationPrice(product.Customization, design);
        return new LinePrice(unit, customization, Money.Round((unit + customization) * quantity));
    }

    /// <summary>Tax applies to the discounted merchandise subtotal; shipping is not taxed.</summary>
    public static OrderTotals Totals(decimal subtotal, decimal discount, decimal shipping, decimal taxRate)
    {
        subtotal = Money.Round(subtotal);
        discount = Money.Round(Math.Clamp(discount, 0m, subtotal));
        shipping = Money.Round(Math.Max(shipping, 0m));
        var tax = Money.Round((subtotal - discount) * taxRate);
        return new OrderTotals(subtotal, discount, shipping, tax, subtotal - discount + shipping + tax);
    }
}
