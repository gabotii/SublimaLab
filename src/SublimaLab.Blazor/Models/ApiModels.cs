using System.Text.Json;

namespace SublimaLab.Blazor.Models;

// Client-side view of the public HTTP contract (see docs/api.md). The storefront is a separate
// deployable that runs in the browser, so it deliberately does not reference server assemblies.

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, int TotalPages, bool HasNextPage);

public sealed record ApiError(string Code, string Message, Dictionary<string, string[]>? Errors, string? TraceId);

// ---------- Catalog ----------

public sealed record Category(Guid Id, string Name, string Slug, string Description, string? ImageUrl, int ProductCount);

public sealed record ColorSwatch(string Name, string Hex);

public sealed record ProductSummary(
    Guid Id,
    string Name,
    string Slug,
    string ShortDescription,
    string CategorySlug,
    string CategoryName,
    decimal Price,
    string Currency,
    string? ImageUrl,
    string? ImageAlt,
    bool IsCustomizable,
    bool IsFeatured,
    IReadOnlyList<ColorSwatch> Colors);

public sealed record ProductImage(string Url, string AltText);

public sealed record Variant(Guid Id, string Sku, string ColorName, string ColorHex, string? Size, decimal Price);

public sealed record Specification(string Name, string Value);

public sealed record PrintArea(string Key, string Name, decimal WidthMm, decimal HeightMm, string Shape);

public sealed record Customization(
    string Kind,
    string? ModelUrl,
    decimal BaseFee,
    decimal AdditionalPrintAreaFee,
    int MaxElementsPerArea,
    int MinRecommendedDpi,
    IReadOnlyList<PrintArea> PrintAreas,
    IReadOnlyList<string> Fonts);

public sealed record ProductDetail(
    Guid Id,
    string Name,
    string Slug,
    string ShortDescription,
    string Description,
    string CategorySlug,
    string CategoryName,
    decimal Price,
    string Currency,
    bool IsCustomizable,
    int ProductionTimeDays,
    IReadOnlyList<ProductImage> Images,
    IReadOnlyList<Variant> Variants,
    IReadOnlyList<Specification> Specifications,
    Customization? Customization);

public sealed record ShippingMethod(string Code, string Name, string Description, decimal Cost, decimal? FreeShippingThreshold, int MinDeliveryDays, int MaxDeliveryDays, string Currency);

// ---------- Cart ----------

public sealed record CartVariant(Guid Id, string Sku, string ColorName, string ColorHex, string? Size, string Label);

public sealed record CartLine(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string ProductSlug,
    string? ImageUrl,
    CartVariant? Variant,
    int Quantity,
    decimal UnitPrice,
    decimal CustomizationPrice,
    decimal LineTotal,
    bool IsCustomized,
    JsonElement? Design,
    Guid? SourceDesignId,
    string? PreviewUrl,
    bool IsAvailable,
    string? Issue);

public sealed record CartTotals(decimal Subtotal, decimal Discount, decimal Shipping, string? ShippingMethodCode, decimal Tax, decimal Total);

public sealed record Cart(
    IReadOnlyList<CartLine> Items,
    int ItemCount,
    CartTotals Totals,
    string Currency,
    string? DiscountCode,
    string? DiscountMessage,
    bool CanCheckout)
{
    public static readonly Cart Empty = new([], 0, new CartTotals(0, 0, 0, null, 0, 0), "USD", null, null, false);
}

public sealed record AddCartItemRequest(Guid ProductId, Guid VariantId, int Quantity);

public sealed record UpdateCartItemRequest(int Quantity);

// ---------- Accounts ----------

public sealed record User(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    bool EmailConfirmed,
    IReadOnlyList<string> Roles,
    bool TwoFactorEnabled = false,
    bool MustEnableTwoFactor = false)
{
    public string FullName => $"{FirstName} {LastName}".Trim();
    public bool IsAdmin => Roles.Contains("Admin");
}

public sealed record AntiforgeryToken(string Token, string HeaderName);

/// <summary><c>User</c> is null while <c>RequiresTwoFactor</c> is true (password accepted, code needed).</summary>
public sealed record AuthResponse(User? User, AntiforgeryToken Antiforgery, bool RequiresTwoFactor = false);

public sealed record TwoFactorStatus(bool Enabled, int RecoveryCodesLeft, bool Required);

public sealed record TwoFactorSetup(string SharedKey, string AuthenticatorUri, string QrCodeSvg);

public sealed record RecoveryCodeList(IReadOnlyList<string> RecoveryCodes);

public sealed record SessionResponse(User? User);

public sealed record RegisterResponse(string Email, bool RequiresEmailConfirmation, string Message);

public sealed record CustomerAddress(
    Guid Id,
    string Label,
    string RecipientName,
    string Phone,
    string Line1,
    string? Line2,
    string City,
    string Region,
    string PostalCode,
    string Country,
    bool IsDefault);

// ---------- Orders ----------

public enum OrderStatus
{
    Pending,
    PaymentPending,
    Paid,
    InProduction,
    ReadyToShip,
    Shipped,
    Delivered,
    Cancelled,
}

public enum PaymentStatus
{
    Pending,
    Succeeded,
    Failed,
    Cancelled,
    Refunded,
}

public sealed record Contact(string FullName, string Email, string Phone);

public sealed record Address(string Line1, string? Line2, string City, string Region, string PostalCode, string Country);

public sealed record PlaceOrderRequest(Guid IdempotencyKey, Contact Contact, Address ShippingAddress, string ShippingMethodCode, bool SaveAddress);

public sealed record OrderItem(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string Sku,
    string VariantLabel,
    string ColorHex,
    string? ImageUrl,
    int Quantity,
    decimal UnitPrice,
    decimal CustomizationPrice,
    decimal LineTotal,
    bool IsCustomized,
    JsonElement? Design,
    string? PreviewUrl);

public sealed record Payment(Guid Id, Guid OrderId, string Provider, PaymentStatus Status, decimal Amount, string Currency, string? FailureReason, DateTime CreatedAt, string? ClientSecret);

public sealed record PaymentOutcome(Payment Payment, OrderStatus OrderStatus, string OrderNumber);

public sealed record Shipment(string Carrier, string TrackingNumber, string Status, DateTime ShippedAt, DateTime? DeliveredAt);

public sealed record TimelineEntry(OrderStatus Status, DateTime ChangedAt, string? Note);

public sealed record Order(
    Guid Id,
    string OrderNumber,
    OrderStatus Status,
    DateTime PlacedAt,
    Contact Contact,
    Address ShippingAddress,
    string ShippingMethodCode,
    string ShippingMethodName,
    string Currency,
    decimal Subtotal,
    decimal Discount,
    string? DiscountCode,
    decimal Shipping,
    decimal Tax,
    decimal Total,
    IReadOnlyList<OrderItem> Items,
    IReadOnlyList<Payment> Payments,
    Shipment? Shipment,
    IReadOnlyList<TimelineEntry> Timeline,
    bool CanPay,
    bool CanCancel);

public sealed record SavedDesign(Guid Id, Guid ProductId, string ProductName, string ProductSlug, string Name, JsonElement Design, string? PreviewUrl, DateTime CreatedAt, DateTime UpdatedAt);

public sealed record OrderSummary(Guid Id, string OrderNumber, OrderStatus Status, DateTime PlacedAt, int ItemCount, decimal Total, string Currency, string? FirstItemImageUrl);
