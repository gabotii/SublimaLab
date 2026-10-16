using SublimaLab.Application.Orders;
using SublimaLab.Domain.Ordering;

namespace SublimaLab.Application.Admin;

// ---------- Dashboard ----------

public sealed record DailySalesDto(DateOnly Date, int Orders, decimal Revenue);

public sealed record TopProductDto(Guid ProductId, string ProductName, int Quantity, decimal Revenue);

public sealed record DashboardDto(
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
    IReadOnlyList<DailySalesDto> Sales,
    IReadOnlyList<TopProductDto> TopProducts,
    IReadOnlyList<AdminOrderSummaryDto> RecentOrders);

// ---------- Catalog ----------

public sealed record AdminCategoryDto(Guid Id, string Name, string Slug, bool IsActive, int ProductCount);

public sealed record AdminProductSummaryDto(
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

public sealed record AdminVariantDto(Guid Id, string Sku, string ColorName, string ColorHex, string? Size, decimal PriceAdjustment, bool IsActive, int SortOrder);

public sealed record AdminImageDto(Guid Id, string Url, string AltText, int SortOrder, bool IsUploaded);

public sealed record AdminPrintAreaDto(string Key, string Name, decimal WidthMm, decimal HeightMm, string Shape);

public sealed record AdminCustomizationDto(
    bool IsEnabled,
    string Kind,
    string? ModelUrl,
    decimal BaseFee,
    decimal AdditionalPrintAreaFee,
    int MaxElementsPerArea,
    int MinRecommendedDpi,
    IReadOnlyList<AdminPrintAreaDto> PrintAreas);

public sealed record SpecificationItem(string Name, string Value);

public sealed record AdminProductDto(
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
    IReadOnlyList<AdminVariantDto> Variants,
    IReadOnlyList<AdminImageDto> Images,
    AdminCustomizationDto? Customization,
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
    IReadOnlyList<SpecificationItem>? Specifications);

public sealed record SetActiveRequest(bool IsActive);

/// <param name="Id">Existing variant to update; null to create a new one.</param>
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
    IReadOnlyList<AdminPrintAreaDto> PrintAreas);

// ---------- Customers ----------

public sealed record AdminCustomerSummaryDto(
    Guid Id,
    string Email,
    string FullName,
    bool EmailConfirmed,
    bool IsLockedOut,
    bool IsAdmin,
    DateTime CreatedAt,
    int OrderCount,
    decimal TotalSpent);

public sealed record AdminCustomerDto(
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
    IReadOnlyList<OrderSummaryDto> Orders,
    IReadOnlyList<Accounts.CustomerAddressDto> Addresses);

public sealed record SetLockRequest(bool Locked);

public sealed record AdminOrderNextStepsDto(OrderStatus Status, IReadOnlyList<OrderStatus> AllowedTransitions);
