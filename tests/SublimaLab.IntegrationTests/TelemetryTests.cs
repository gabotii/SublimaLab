using System.Net;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using SublimaLab.IntegrationTests.Support;

namespace SublimaLab.IntegrationTests;

/// <summary>Telemetry switched on, pointing at a collector that isn't running (exports fail quietly).</summary>
public sealed class TelemetryFactory : SublimaLabFactory
{
    protected override void ConfigureSettings(IDictionary<string, string?> settings) =>
        settings["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://127.0.0.1:4317";
}

public sealed class TelemetryTests(TelemetryFactory factory) : IClassFixture<TelemetryFactory>
{
    [Fact]
    public async Task OpenTelemetry_is_enabled_by_the_otlp_endpoint_setting()
    {
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
        Assert.NotNull(factory.Services.GetService<TracerProvider>());
        Assert.NotNull(factory.Services.GetService<MeterProvider>());
    }
}

public sealed class TelemetryOffTests(SublimaLabFactory factory) : IClassFixture<SublimaLabFactory>
{
    [Fact]
    public void OpenTelemetry_is_off_without_an_endpoint() =>
        Assert.Null(factory.Services.GetService<TracerProvider>());
}
