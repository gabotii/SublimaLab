using System.Diagnostics.Metrics;

namespace SublimaLab.Application.Common;

/// <summary>
/// Business metrics, published through <see cref="System.Diagnostics.Metrics"/> so any exporter
/// (OpenTelemetry, dotnet-counters) can collect them without the application knowing which.
/// </summary>
public static class StoreMetrics
{
    public const string MeterName = "SublimaLab.Store";

    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> OrdersPlaced =
        Meter.CreateCounter<long>("sublimalab.orders.placed", "{order}", "Orders placed at checkout.");

    public static readonly Counter<long> Payments =
        Meter.CreateCounter<long>("sublimalab.payments", "{payment}", "Payment confirmations, tagged by provider and outcome.");

    public static readonly Counter<long> Uploads =
        Meter.CreateCounter<long>("sublimalab.uploads", "{file}", "Image uploads, tagged by kind and outcome.");
}
