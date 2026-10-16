namespace SublimaLab.Blazor.Models;

// Client view of the admin API contract (/api/admin/*).

public sealed record DailySales(DateOnly Date, int Orders, decimal Revenue);

public sealed record TopProduct(Guid ProductId, string ProductName, int Quantity, decimal Revenue);

public sealed record AdminOrderSummary(
    Guid Id,
    string OrderNumber,
    OrderStatus Status,
    DateTime PlacedAt,
    string CustomerName,
    string CustomerEmail,
    int ItemCount,
    bool HasCustomization,
    decimal Total,
    string Currency);

public sealed record Dashboard(
    decimal TotalRevenue,
    decimal RevenueLast30Days,
    int TotalOrders,
    int PaidOrdersLast30Days,
    int PendingPayment,
    int AwaitingProduction,
    int InProduction,
    int ReadyToShip,
    int Customers,
    int ActiveProducts,
    string Currency,
    IReadOnlyList<DailySales> Sales,
    IReadOnlyList<TopProduct> TopProducts,
    IReadOnlyList<AdminOrderSummary> RecentOrders);

public sealed record AdminOrderDetail(Order Order, Guid CustomerId, IReadOnlyList<OrderStatus> NextStatuses, bool CanShip, bool HasCustomization);

public sealed record UpdateOrderStatusRequest(OrderStatus Status, string? Note);

public sealed record ShipOrderRequest(string Carrier, string TrackingNumber);

public sealed record AdminCategory(Guid Id, string Name, string Slug, bool IsActive, int ProductCount);

public sealed record AdminProductSummary(
    Guid Id,
    string Name,
    string Slug,
    string CategoryName,
    decimal BasePrice,
    bool IsActive,
    bool IsFeatured,
    bool IsCustomizable,
    int VariantCount,
    string? ImageUrl,
    DateTime UpdatedAt);

public sealed record AdminVariant(Guid Id, string Sku, string ColorName, string ColorHex, string? Size, decimal PriceAdjustment, bool IsActive, int SortOrder);

public sealed record AdminImage(Guid Id, string Url, string AltText, int SortOrder, bool IsUploaded);

public sealed record AdminPrintArea(string Key, string Name, decimal WidthMm, decimal HeightMm, string Shape);

public sealed record AdminCustomization(
    bool IsEnabled,
    string Kind,
    string? ModelUrl,
    decimal BaseFee,
    decimal AdditionalPrintAreaFee,
    int MaxElementsPerArea,
    int MinRecommendedDpi,
    IReadOnlyList<AdminPrintArea> PrintAreas);

public sealed record SpecificationItem(string Name, string Value);

public sealed record AdminProduct(
    Guid Id,
    Guid CategoryId,
    string Name,
    string Slug,
    string ShortDescription,
    string Description,
    decimal BasePrice,
    bool IsActive,
    bool IsFeatured,
    int ProductionTimeDays,
    IReadOnlyList<SpecificationItem> Specifications,
    IReadOnlyList<AdminVariant> Variants,
    IReadOnlyList<AdminImage> Images,
    AdminCustomization? Customization,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record SaveProductRequest(
    Guid CategoryId,
    string Name,
    string Slug,
    string? ShortDescription,
    string? Description,
    decimal BasePrice,
    bool IsActive,
    bool IsFeatured,
    int ProductionTimeDays,
    IReadOnlyList<SpecificationItem> Specifications);

public sealed record SetActiveRequest(bool IsActive);

public sealed record SaveVariantItem(Guid? Id, string Sku, string ColorName, string ColorHex, string? Size, decimal PriceAdjustment, bool IsActive);

public sealed record SaveVariantsRequest(IReadOnlyList<SaveVariantItem> Variants);

public sealed record SaveCustomizationRequest(
    bool IsEnabled,
    string Kind,
    string? ModelUrl,
    decimal BaseFee,
    decimal AdditionalPrintAreaFee,
    int MaxElementsPerArea,
    int MinRecommendedDpi,
    IReadOnlyList<AdminPrintArea> PrintAreas);

public sealed record AdminCustomerSummary(
    Guid Id,
    string Email,
    string FullName,
    bool EmailConfirmed,
    bool IsLockedOut,
    bool IsAdmin,
    DateTime CreatedAt,
    int OrderCount,
    decimal TotalSpent);

public sealed record AdminCustomer(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    bool EmailConfirmed,
    bool IsLockedOut,
    IReadOnlyList<string> Roles,
    DateTime CreatedAt,
    int OrderCount,
    decimal TotalSpent,
    IReadOnlyList<OrderSummary> Orders,
    IReadOnlyList<CustomerAddress> Addresses);

public sealed record SetLockRequest(bool Locked);
