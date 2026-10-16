using System.Globalization;
using SublimaLab.Blazor.Models;

namespace SublimaLab.Blazor.Services;

public static class Format
{
    // Invariant culture: English month names and US-style numbers, without shipping ICU data to the browser.
    private static readonly CultureInfo Display = CultureInfo.InvariantCulture;

    public static string Money(decimal amount, string currency = "USD")
    {
        var culture = (CultureInfo)Display.Clone();
        culture.NumberFormat.CurrencySymbol = currency switch
        {
            "USD" => "$",
            "EUR" => "€",
            "GBP" => "£",
            _ => currency + " ",
        };
        return amount.ToString("C", culture);
    }

    public static string Date(DateTime utc) => utc.ToLocalTime().ToString("MMM d, yyyy", Display);

    public static string DateAndTime(DateTime utc) => utc.ToLocalTime().ToString("MMM d, yyyy · h:mm tt", Display);

    public static string Status(OrderStatus status) => status switch
    {
        OrderStatus.Pending => "Order placed",
        OrderStatus.PaymentPending => "Awaiting payment",
        OrderStatus.Paid => "Paid",
        OrderStatus.InProduction => "In production",
        OrderStatus.ReadyToShip => "Ready to ship",
        OrderStatus.Shipped => "Shipped",
        OrderStatus.Delivered => "Delivered",
        OrderStatus.Cancelled => "Cancelled",
        _ => status.ToString(),
    };

    public static string StatusTone(OrderStatus status) => status switch
    {
        OrderStatus.Delivered or OrderStatus.Shipped => "success",
        OrderStatus.Cancelled => "danger",
        OrderStatus.Pending or OrderStatus.PaymentPending => "warning",
        _ => "info",
    };

    /// <summary>Picks a readable text colour (dark or white) for a background hex colour.</summary>
    public static bool IsLight(string hex)
    {
        if (hex.Length != 7 || hex[0] != '#')
        {
            return true;
        }

        var r = Convert.ToInt32(hex[1..3], 16);
        var g = Convert.ToInt32(hex[3..5], 16);
        var b = Convert.ToInt32(hex[5..7], 16);
        return ((0.299 * r) + (0.587 * g) + (0.114 * b)) > 160;
    }
}
