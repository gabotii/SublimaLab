using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SublimaLab.Application.Common;

namespace SublimaLab.Api.Hosting;

/// <summary>
/// OpenTelemetry traces, metrics and logs, exported over OTLP. Off unless an endpoint is
/// configured with the standard <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> variable (plus optional
/// <c>OTEL_EXPORTER_OTLP_HEADERS</c>, <c>OTEL_SERVICE_NAME</c>, <c>OTEL_RESOURCE_ATTRIBUTES</c>).
/// </summary>
internal static class TelemetrySetup
{
    public const string DefaultServiceName = "sublimalab-api";

    public static WebApplicationBuilder AddTelemetry(this WebApplicationBuilder builder)
    {
        if (string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            return builder;
        }

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(builder.Configuration["OTEL_SERVICE_NAME"] ?? DefaultServiceName, serviceVersion: typeof(TelemetrySetup).Assembly.GetName().Version?.ToString())
                .AddAttributes([new("deployment.environment.name", builder.Environment.EnvironmentName)]))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(options =>
                {
                    // Health probes and static files are noise in traces.
                    options.Filter = context => context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase);
                    options.RecordException = true;
                })
                .AddHttpClientInstrumentation()
                .AddSource("Npgsql")
                .AddOtlpExporter())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(StoreMetrics.MeterName, "Microsoft.EntityFrameworkCore", "Npgsql")
                .AddOtlpExporter());

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeScopes = true;
            logging.IncludeFormattedMessage = true;
            logging.AddOtlpExporter();
        });

        return builder;
    }
}
