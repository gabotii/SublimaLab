using SublimaLab.Domain.Common;

namespace SublimaLab.Domain.Shipping;

public sealed class ShippingMethod : Entity
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Cost { get; set; }

    /// <summary>Order subtotal (after discounts) from which shipping is free. Null = never free.</summary>
    public decimal? FreeShippingThreshold { get; set; }

    public int MinDeliveryDays { get; set; }
    public int MaxDeliveryDays { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    public decimal CostFor(decimal discountedSubtotal) =>
        FreeShippingThreshold is { } threshold && discountedSubtotal >= threshold ? 0m : Cost;
}
