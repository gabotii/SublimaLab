using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SublimaLab.Application.Abstractions;
using SublimaLab.Application.Common;
using SublimaLab.Application.Customization;
using SublimaLab.Application.Uploads;
using SublimaLab.Domain.Carts;
using SublimaLab.Domain.Catalog;
using SublimaLab.Domain.Designs;
using SublimaLab.Domain.Uploads;

namespace SublimaLab.Application.Carts;

public sealed class AddCartItemRequestValidator : AbstractValidator<AddCartItemRequest>
{
    public AddCartItemRequestValidator()
    {
        RuleFor(r => r.ProductId).NotEmpty();
        RuleFor(r => r.VariantId).NotEmpty();
        RuleFor(r => r.Quantity).InclusiveBetween(1, Cart.MaxQuantityPerLine);
    }
}

public sealed class UpdateCartItemRequestValidator : AbstractValidator<UpdateCartItemRequest>
{
    public UpdateCartItemRequestValidator()
    {
        RuleFor(r => r.Quantity).InclusiveBetween(1, Cart.MaxQuantityPerLine);
    }
}

public sealed class ApplyDiscountRequestValidator : AbstractValidator<ApplyDiscountRequest>
{
    public ApplyDiscountRequestValidator()
    {
        RuleFor(r => r.Code).NotEmpty().MaximumLength(40).Matches("^[A-Za-z0-9_-]+$");
    }
}

/// <summary>
/// Cart operations for both guests (session cookie) and signed-in customers. Requests carry
/// only identifiers and quantities; every price in the response is computed server-side.
/// </summary>
public sealed class CartService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IClientSession session,
    CartPricer pricer,
    DesignValidationService designValidation,
    DesignService designs,
    IValidator<AddCartItemRequest> addValidator,
    IValidator<UpdateCartItemRequest> updateValidator,
    IValidator<ApplyDiscountRequest> discountValidator,
    IOptions<CommerceOptions> commerce)
{
    public async Task<CartDto> GetAsync(string? shippingMethodCode, CancellationToken cancellationToken)
    {
        var cart = await FindCartAsync(cancellationToken);
        return cart is null ? EmptyCart() : await ToDtoAsync(cart, shippingMethodCode, cancellationToken);
    }

    public async Task<CartDto> AddItemAsync(AddCartItemRequest request, CancellationToken cancellationToken)
    {
        await addValidator.ValidateOrThrowAsync(request, cancellationToken);

        var product = await LoadPurchasableProductAsync(request.ProductId, cancellationToken);
        var variant = GetActiveVariant(product, request.VariantId);

        var design = request.Design;
        var sourceDesignId = request.DesignId;
        if (sourceDesignId is { } savedDesignId)
        {
            var saved = await designs.FindOwnedAsync(savedDesignId, cancellationToken);
            if (saved.ProductId != product.Id)
            {
                throw RequestValidationException.For("designId", "The saved design belongs to a different product.");
            }

            design = saved.Document;
        }

        design = await NormalizeDesignAsync(product, design, request.PreviewUploadId, cancellationToken);

        var cart = await FindCartAsync(cancellationToken) ?? CreateCart();
        cart.AddItem(product.Id, variant.Id, request.Quantity, design, design is null ? null : sourceDesignId, design is null ? null : request.PreviewUploadId);

        await db.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(cart, null, cancellationToken);
    }

    public async Task<CartDto> UpdateItemAsync(Guid itemId, UpdateCartItemRequest request, CancellationToken cancellationToken)
    {
        await updateValidator.ValidateOrThrowAsync(request, cancellationToken);
        var cart = await FindCartAsync(cancellationToken) ?? throw new NotFoundException("Cart item");
        var item = cart.Items.FirstOrDefault(i => i.Id == itemId) ?? throw new NotFoundException("Cart item");

        if (request.Design is not null)
        {
            var product = await LoadPurchasableProductAsync(item.ProductId, cancellationToken);
            var variant = GetActiveVariant(product, request.VariantId ?? item.VariantId);
            var design = await NormalizeDesignAsync(product, request.Design, request.PreviewUploadId, cancellationToken)
                ?? throw RequestValidationException.For("design", "A customized item needs at least one design element.");

            cart.UpdateCustomization(itemId, variant.Id, design, request.PreviewUploadId);
        }

        cart.UpdateQuantity(itemId, request.Quantity);
        await db.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(cart, null, cancellationToken);
    }

    public async Task<CartDto> RemoveItemAsync(Guid itemId, CancellationToken cancellationToken)
    {
        var cart = await FindCartAsync(cancellationToken);
        if (cart is null || cart.Items.All(i => i.Id != itemId))
        {
            throw new NotFoundException("Cart item");
        }

        cart.RemoveItem(itemId);
        await db.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(cart, null, cancellationToken);
    }

    public async Task<CartDto> ApplyDiscountAsync(ApplyDiscountRequest request, CancellationToken cancellationToken)
    {
        await discountValidator.ValidateOrThrowAsync(request, cancellationToken);
        var cart = await FindCartAsync(cancellationToken);
        if (cart is null || cart.Items.Count == 0)
        {
            throw new BusinessRuleException("CART_EMPTY", "Add something to your cart before applying a discount code.");
        }

        cart.ApplyDiscountCode(request.Code);
        var priced = await pricer.PriceAsync(cart, null, cancellationToken);
        if (priced.Discount is null)
        {
            throw new BusinessRuleException("DISCOUNT_INVALID", priced.DiscountMessage ?? "This discount code is not valid.");
        }

        await db.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(cart, null, cancellationToken);
    }

    public async Task<CartDto> RemoveDiscountAsync(CancellationToken cancellationToken)
    {
        var cart = await FindCartAsync(cancellationToken);
        if (cart is null)
        {
            return EmptyCart();
        }

        cart.RemoveDiscountCode();
        await db.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(cart, null, cancellationToken);
    }

    internal async Task<Cart?> FindCartAsync(CancellationToken cancellationToken)
    {
        var carts = db.Carts.Include(c => c.Items);
        if (currentUser.UserId is { } userId)
        {
            return await carts.FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);
        }

        var sessionId = session.SessionId;
        return sessionId is null ? null : await carts.FirstOrDefaultAsync(c => c.SessionId == sessionId, cancellationToken);
    }

    private Cart CreateCart()
    {
        var cart = currentUser.UserId is { } userId ? Cart.ForUser(userId) : Cart.ForSession(session.GetOrCreateSessionId());
        db.Carts.Add(cart);
        return cart;
    }

    /// <summary>Validates a customization; an empty design means "not customized".</summary>
    private async Task<DesignDocument?> NormalizeDesignAsync(Product product, DesignDocument? design, Guid? previewUploadId, CancellationToken cancellationToken)
    {
        if (design is null || design.IsEmpty())
        {
            return null;
        }

        await designValidation.EnsureValidAsync(product, design, "design", cancellationToken);

        if (previewUploadId is { } previewId)
        {
            var preview = await db.Uploads.AsNoTracking().FirstOrDefaultAsync(u => u.Id == previewId && u.Kind == UploadKind.Preview, cancellationToken);
            if (preview is null || !preview.IsAccessibleBy(currentUser.UserId, session.SessionId))
            {
                throw RequestValidationException.For("previewUploadId", "The preview image does not exist or is not accessible.");
            }
        }

        return design;
    }

    private async Task<Product> LoadPurchasableProductAsync(Guid productId, CancellationToken cancellationToken) =>
        await db.Products
            .AsNoTracking()
            .Include(p => p.Variants)
            .Include(p => p.Customization).ThenInclude(c => c!.PrintAreas)
            .FirstOrDefaultAsync(p => p.Id == productId && p.IsActive, cancellationToken)
        ?? throw new NotFoundException("Product");

    private static ProductVariant GetActiveVariant(Product product, Guid variantId) =>
        product.Variants.FirstOrDefault(v => v.Id == variantId && v.IsActive)
        ?? throw RequestValidationException.For("variantId", "The selected option is not available for this product.");

    private async Task<CartDto> ToDtoAsync(Cart cart, string? shippingMethodCode, CancellationToken cancellationToken)
    {
        var priced = await pricer.PriceAsync(cart, shippingMethodCode, cancellationToken);

        var lines = priced.Lines.Select(line => new CartLineDto(
                line.Item.Id,
                line.Item.ProductId,
                line.Product?.Name ?? "Unavailable product",
                line.Product?.Slug ?? string.Empty,
                line.Product?.Images.OrderBy(i => i.SortOrder).Select(i => i.Url).FirstOrDefault(),
                line.Variant is { } v ? new CartVariantDto(v.Id, v.Sku, v.ColorName, v.ColorHex, v.Size, v.Label) : null,
                line.Item.Quantity,
                line.Price.UnitPrice,
                line.Price.CustomizationPrice,
                line.Price.LineTotal,
                line.Item.Design is not null,
                line.Item.Design,
                line.Item.SourceDesignId,
                line.Item.PreviewUploadId is { } previewId ? UploadUrls.Content(previewId) : null,
                line.IsAvailable,
                line.Issue))
            .ToList();

        var totals = priced.Totals;
        return new CartDto(
            lines,
            lines.Where(l => l.IsAvailable).Sum(l => l.Quantity),
            new CartTotalsDto(totals.Subtotal, totals.Discount, totals.Shipping, priced.ShippingMethod?.Code, totals.Tax, totals.Total),
            commerce.Value.Currency,
            cart.DiscountCode,
            priced.DiscountMessage,
            lines.Count > 0 && !priced.HasUnavailableLines);
    }

    private CartDto EmptyCart() =>
        new([], 0, new CartTotalsDto(0, 0, 0, null, 0, 0), commerce.Value.Currency, null, null, false);
}
