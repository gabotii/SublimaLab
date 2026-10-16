using SublimaLab.Domain.Designs;

namespace SublimaLab.Application.Carts;

public sealed record CartVariantDto(Guid Id, string Sku, string ColorName, string ColorHex, string? Size, string Label);

public sealed record CartLineDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string ProductSlug,
    string? ImageUrl,
    CartVariantDto? Variant,
    int Quantity,
    decimal UnitPrice,
    decimal CustomizationPrice,
    decimal LineTotal,
    bool IsCustomized,
    DesignDocument? Design,
    Guid? SourceDesignId,
    string? PreviewUrl,
    bool IsAvailable,
    string? Issue);

public sealed record CartTotalsDto(decimal Subtotal, decimal Discount, decimal Shipping, string? ShippingMethodCode, decimal Tax, decimal Total);

public sealed record CartDto(
    IReadOnlyList<CartLineDto> Items,
    int ItemCount,
    CartTotalsDto Totals,
    string Currency,
    string? DiscountCode,
    string? DiscountMessage,
    bool CanCheckout);

/// <param name="Design">Customization; null for plain products. Ignored when <paramref name="DesignId"/> is used.</param>
/// <param name="DesignId">Adds a design saved to the customer's account.</param>
public sealed record AddCartItemRequest(Guid ProductId, Guid VariantId, int Quantity, DesignDocument? Design, Guid? DesignId, Guid? PreviewUploadId);

/// <param name="Design">When provided, replaces the line's customization (edit design flow).</param>
public sealed record UpdateCartItemRequest(int Quantity, Guid? VariantId, DesignDocument? Design, Guid? PreviewUploadId);

public sealed record ApplyDiscountRequest(string Code);
