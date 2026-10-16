using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SublimaLab.Application.Abstractions;
using SublimaLab.Application.Common;
using SublimaLab.Application.Pricing;
using SublimaLab.Domain.Carts;
using SublimaLab.Domain.Catalog;
using SublimaLab.Domain.Discounts;
using SublimaLab.Domain.Ordering;
using SublimaLab.Domain.Shipping;

namespace SublimaLab.Application.Carts;

public sealed record PricedLine(CartItem Item, Product? Product, ProductVariant? Variant, LinePrice Price, string? Issue)
{
    public bool IsAvailable => Issue is null;
}

public sealed record PricedCart(
    IReadOnlyList<PricedLine> Lines,
    OrderTotals Totals,
    ShippingMethod? ShippingMethod,
    DiscountCode? Discount,
    string? DiscountMessage)
{
    public bool HasUnavailableLines => Lines.Any(l => !l.IsAvailable);
}

/// <summary>
/// Prices a cart against the live catalog. Shared by the cart view and checkout so the
/// customer always sees exactly what they will be charged.
/// </summary>
public sealed class CartPricer(IAppDbContext db, IOptions<CommerceOptions> commerce, TimeProvider clock)
{
    public async Task<PricedCart> PriceAsync(Cart cart, string? shippingMethodCode, CancellationToken cancellationToken)
    {
        var productIds = cart.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await db.Products
            .AsNoTracking()
            .Include(p => p.Variants)
            .Include(p => p.Images)
            .Include(p => p.Customization).ThenInclude(c => c!.PrintAreas)
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var lines = cart.Items
            .OrderBy(i => i.Id)
            .Select(item => PriceLine(item, products.GetValueOrDefault(item.ProductId)))
            .ToList();

        var subtotal = lines.Where(l => l.IsAvailable).Sum(l => l.Price.LineTotal);

        var (discount, discountAmount, discountMessage) = await EvaluateDiscountAsync(cart.DiscountCode, subtotal, cancellationToken);

        var shippingMethod = await ResolveShippingMethodAsync(shippingMethodCode, cancellationToken);
        var shipping = lines.Any(l => l.IsAvailable) && shippingMethod is not null
            ? shippingMethod.CostFor(subtotal - discountAmount)
            : 0m;

        var totals = PriceCalculator.Totals(subtotal, discountAmount, shipping, commerce.Value.TaxRate);
        return new PricedCart(lines, totals, shippingMethod, discount, discountMessage);
    }

    public async Task<ShippingMethod?> ResolveShippingMethodAsync(string? code, CancellationToken cancellationToken)
    {
        code = string.IsNullOrWhiteSpace(code) ? commerce.Value.DefaultShippingMethodCode : code.Trim().ToLowerInvariant();
        return await db.ShippingMethods.AsNoTracking().FirstOrDefaultAsync(m => m.Code == code && m.IsActive, cancellationToken);
    }

    private static PricedLine PriceLine(CartItem item, Product? product)
    {
        var variant = product?.Variants.FirstOrDefault(v => v.Id == item.VariantId);
        var zero = new LinePrice(0m, 0m, 0m);

        if (product is not { IsActive: true })
        {
            return new PricedLine(item, product, variant, zero, "This product is no longer available.");
        }

        if (variant is not { IsActive: true })
        {
            return new PricedLine(item, product, variant, zero, "The selected option is no longer available.");
        }

        if (item.Design is not null && !product.IsCustomizable)
        {
            return new PricedLine(item, product, variant, zero, "This product can no longer be customized.");
        }

        return new PricedLine(item, product, variant, PriceCalculator.PriceLine(product, variant, item.Design, item.Quantity), null);
    }

    private async Task<(DiscountCode? Discount, decimal Amount, string? Message)> EvaluateDiscountAsync(string? code, decimal subtotal, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(code))
        {
            return (null, 0m, null);
        }

        var discount = await db.DiscountCodes.AsNoTracking().FirstOrDefaultAsync(d => d.Code == code, cancellationToken);
        if (discount is null)
        {
            return (null, 0m, "This discount code is not valid.");
        }

        var reason = discount.GetIneligibilityReason(subtotal, clock.GetUtcNow().UtcDateTime);
        return reason is null
            ? (discount, discount.CalculateAmount(subtotal), null)
            : (null, 0m, reason);
    }
}
