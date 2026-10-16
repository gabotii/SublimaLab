using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SublimaLab.Application.Abstractions;
using SublimaLab.Application.Carts;
using SublimaLab.Application.Common;
using SublimaLab.Application.Customization;
using SublimaLab.Domain.Customers;
using SublimaLab.Domain.Customization;
using SublimaLab.Domain.Designs;
using SublimaLab.Domain.Ordering;

namespace SublimaLab.Application.Orders;

/// <summary>
/// Checkout and customer order history. Placing an order re-validates and re-prices the
/// entire cart server-side, then freezes product data, prices and production artwork data
/// into the order so it is unaffected by later catalog changes.
/// </summary>
public sealed class OrderService(
    IAppDbContext db,
    ICurrentUser currentUser,
    CartService carts,
    CartPricer pricer,
    DesignValidationService designValidation,
    IValidator<PlaceOrderRequest> validator,
    IOptions<CommerceOptions> commerce,
    TimeProvider clock,
    ILogger<OrderService> logger)
{
    public async Task<OrderDto> PlaceOrderAsync(PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        await validator.ValidateOrThrowAsync(request, cancellationToken);

        var existing = await LoadOrders().FirstOrDefaultAsync(o => o.UserId == userId && o.IdempotencyKey == request.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return existing.ToDto();
        }

        var cart = await carts.FindCartAsync(cancellationToken);
        if (cart is null || cart.Items.Count == 0)
        {
            throw new BusinessRuleException("CART_EMPTY", "Your cart is empty.");
        }

        var priced = await pricer.PriceAsync(cart, request.ShippingMethodCode, cancellationToken);
        if (priced.ShippingMethod is null || !string.Equals(priced.ShippingMethod.Code, request.ShippingMethodCode.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw RequestValidationException.For("shippingMethodCode", "The selected shipping method is not available.");
        }

        if (priced.HasUnavailableLines)
        {
            throw new BusinessRuleException("CART_HAS_UNAVAILABLE_ITEMS", "Some items in your cart are no longer available. Please review your cart.");
        }

        if (cart.DiscountCode is not null && priced.Discount is null)
        {
            throw new BusinessRuleException("DISCOUNT_INVALID", priced.DiscountMessage ?? "Your discount code is no longer valid.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var orderItems = new List<OrderItem>();
        var retainedUploadIds = new HashSet<Guid>();

        foreach (var line in priced.Lines)
        {
            var product = line.Product!;
            var variant = line.Variant!;
            var design = line.Item.Design;
            ProductionSpec? productionSpec = null;

            if (design is not null)
            {
                // Uploads or configuration may have changed since the item was added.
                await designValidation.EnsureValidAsync(product, design, "cart", cancellationToken);
                productionSpec = await BuildProductionSpecAsync(product.Customization!, design, cancellationToken);
                retainedUploadIds.UnionWith(design.ReferencedUploadIds());
            }

            if (line.Item.PreviewUploadId is { } previewId)
            {
                retainedUploadIds.Add(previewId);
            }

            orderItems.Add(new OrderItem(
                product.Id,
                variant.Id,
                product.Name,
                variant.Sku,
                variant.Label,
                variant.ColorHex,
                product.Images.OrderBy(i => i.SortOrder).Select(i => i.Url).FirstOrDefault(),
                line.Item.Quantity,
                line.Price.UnitPrice,
                line.Price.CustomizationPrice,
                design,
                productionSpec,
                line.Item.PreviewUploadId));
        }

        var contact = new ContactInfo
        {
            FullName = request.Contact.FullName.Trim(),
            Email = request.Contact.Email.Trim(),
            Phone = request.Contact.Phone.Trim(),
        };
        var address = ToPostalAddress(request.ShippingAddress);

        var order = new Order(
            await GenerateUniqueOrderNumberAsync(now, cancellationToken),
            userId,
            contact,
            address,
            priced.ShippingMethod.Code,
            priced.ShippingMethod.Name,
            commerce.Value.Currency,
            orderItems,
            priced.Totals,
            priced.Discount?.Code,
            request.IdempotencyKey,
            now);

        db.Orders.Add(order);

        if (priced.Discount is not null)
        {
            var discount = await db.DiscountCodes.FirstAsync(d => d.Id == priced.Discount.Id, cancellationToken);
            discount.RegisterRedemption();
        }

        var uploads = await db.Uploads.Where(u => retainedUploadIds.Contains(u.Id)).ToListAsync(cancellationToken);
        uploads.ForEach(u => u.Retain());

        if (request.SaveAddress)
        {
            await SaveToAddressBookAsync(userId, contact, address, cancellationToken);
        }

        cart.Clear();
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Order {OrderNumber} placed by {UserId}: {ItemCount} items, total {Total} {Currency}",
            order.OrderNumber, userId, orderItems.Count, order.Total, order.Currency);
        StoreMetrics.OrdersPlaced.Add(1, new KeyValuePair<string, object?>("currency", order.Currency));

        return order.ToDto();
    }

    public async Task<PagedResult<OrderSummaryDto>> ListMineAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var query = db.Orders.AsNoTracking().Where(o => o.UserId == userId);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(o => o.PlacedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new OrderSummaryDto(
                o.Id,
                o.OrderNumber,
                o.Status,
                o.PlacedAt,
                o.Items.Sum(i => i.Quantity),
                o.Total,
                o.Currency,
                o.Items.OrderBy(i => i.Id).Select(i => i.ImageUrl).FirstOrDefault()))
            .ToListAsync(cancellationToken);

        return new PagedResult<OrderSummaryDto>(items, page, pageSize, total);
    }

    public async Task<OrderDto> GetMineAsync(Guid id, CancellationToken cancellationToken) =>
        (await FindOwnedAsync(id, track: false, cancellationToken)).ToDto();

    public async Task<OrderDto> CancelMineAsync(Guid id, CancellationToken cancellationToken)
    {
        var order = await FindOwnedAsync(id, track: true, cancellationToken);
        if (order.Status is not (OrderStatus.Pending or OrderStatus.PaymentPending))
        {
            throw new BusinessRuleException("ORDER_NOT_CANCELLABLE", "This order is already being processed. Please contact support to cancel it.");
        }

        order.Cancel("Cancelled by customer", clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(cancellationToken);
        return order.ToDto();
    }

    internal IQueryable<Order> LoadOrders() =>
        db.Orders
            .Include(o => o.Items)
            .Include(o => o.Payments)
            .Include(o => o.StatusHistory)
            .Include(o => o.Shipment);

    private async Task<Order> FindOwnedAsync(Guid id, bool track, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var query = track ? LoadOrders() : LoadOrders().AsNoTracking();
        return await query.FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("Order");
    }

    private async Task<ProductionSpec> BuildProductionSpecAsync(CustomizationConfiguration configuration, DesignDocument design, CancellationToken cancellationToken)
    {
        var usedAreas = design.Surfaces.Where(s => s.Elements.Count > 0).Select(s => s.PrintArea).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var uploadIds = design.ReferencedUploadIds();
        var uploads = await db.Uploads.AsNoTracking().Where(u => uploadIds.Contains(u.Id)).ToListAsync(cancellationToken);

        return new ProductionSpec
        {
            PrintAreas = configuration.PrintAreas
                .Where(a => usedAreas.Contains(a.Key))
                .OrderBy(a => a.SortOrder)
                .Select(a => new ProductionPrintArea(a.Key, a.Name, a.WidthMm, a.HeightMm, a.Shape.ToString()))
                .ToList(),
            Artwork = uploads
                .Select(u => new ProductionArtwork(u.Id, u.ContentType, u.WidthPx, u.HeightPx, u.Sha256))
                .ToList(),
            MinRecommendedDpi = configuration.MinRecommendedDpi,
        };
    }

    private async Task<string> GenerateUniqueOrderNumberAsync(DateTime now, CancellationToken cancellationToken)
    {
        // ~10^9 combinations per day: a collision is very unlikely, but cheap to rule out.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var candidate = OrderNumber.Generate(now);
            if (!await db.Orders.AnyAsync(o => o.OrderNumber == candidate, cancellationToken))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Could not generate a unique order number.");
    }

    private async Task SaveToAddressBookAsync(Guid userId, ContactInfo contact, PostalAddress address, CancellationToken cancellationToken)
    {
        var addresses = await db.CustomerAddresses.Where(a => a.UserId == userId).ToListAsync(cancellationToken);
        var duplicate = addresses.Any(a =>
            string.Equals(a.Line1, address.Line1, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.PostalCode, address.PostalCode, StringComparison.OrdinalIgnoreCase) &&
            a.Country == address.Country);

        if (duplicate || addresses.Count >= Accounts.AccountService.MaxAddresses)
        {
            return;
        }

        db.CustomerAddresses.Add(new CustomerAddress
        {
            UserId = userId,
            Label = "Shipping",
            RecipientName = contact.FullName,
            Phone = contact.Phone,
            Line1 = address.Line1,
            Line2 = address.Line2,
            City = address.City,
            Region = address.Region,
            PostalCode = address.PostalCode,
            Country = address.Country,
            IsDefault = addresses.Count == 0,
        });
    }

    private static PostalAddress ToPostalAddress(AddressDto dto) => new()
    {
        Line1 = dto.Line1.Trim(),
        Line2 = string.IsNullOrWhiteSpace(dto.Line2) ? null : dto.Line2.Trim(),
        City = dto.City.Trim(),
        Region = dto.Region.Trim(),
        PostalCode = dto.PostalCode.Trim().ToUpperInvariant(),
        Country = dto.Country,
    };
}
