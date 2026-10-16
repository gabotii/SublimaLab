using SublimaLab.Domain.Common;

namespace SublimaLab.Domain.Discounts;

public enum DiscountType
{
    Percentage = 1,
    FixedAmount = 2,
}

public sealed class DiscountCode : Entity
{
    public required string Code { get; set; }
    public string Description { get; set; } = string.Empty;
    public DiscountType Type { get; set; }

    /// <summary>Percentage (0-100) or a fixed amount in the store currency.</summary>
    public decimal Value { get; set; }

    public decimal? MinimumSubtotal { get; set; }
    public DateTime? StartsAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public int? MaxRedemptions { get; set; }
    public int RedemptionCount { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Returns null when the code applies, otherwise a customer-facing reason.</summary>
    public string? GetIneligibilityReason(decimal subtotal, DateTime now)
    {
        if (!IsActive || (StartsAt is { } start && now < start))
        {
            return "This discount code is not valid.";
        }

        if (ExpiresAt is { } end && now >= end)
        {
            return "This discount code has expired.";
        }

        if (MaxRedemptions is { } max && RedemptionCount >= max)
        {
            return "This discount code is no longer available.";
        }

        if (MinimumSubtotal is { } minimum && subtotal < minimum)
        {
            return $"This discount code requires a minimum order of {minimum:0.00}.";
        }

        return null;
    }

    /// <summary>Discount amount for a subtotal; never exceeds the subtotal itself.</summary>
    public decimal CalculateAmount(decimal subtotal)
    {
        var amount = Type switch
        {
            DiscountType.Percentage => subtotal * Math.Clamp(Value, 0m, 100m) / 100m,
            DiscountType.FixedAmount => Math.Max(Value, 0m),
            _ => 0m,
        };

        return Money.Round(Math.Min(amount, subtotal));
    }

    public void RegisterRedemption() => RedemptionCount++;
}
