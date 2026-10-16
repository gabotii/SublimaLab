using SublimaLab.Api.Hosting;
using SublimaLab.Application;
using SublimaLab.Infrastructure;
using SublimaLab.Infrastructure.Persistence.Seed;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.AddServerHeader = false;

    // JSON requests are small; upload endpoints raise this limit explicitly.
    kestrel.Limits.MaxRequestBodySize = 1024 * 1024;
});

builder.AddTelemetry();

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddApi(builder.Configuration);

var app = builder.Build();

var database = app.Services.GetDatabaseOptions();
if (database.MigrateOnStartup || database.SeedOnStartup)
{
    await DatabaseInitializer.InitializeAsync(app.Services, applyMigrations: database.MigrateOnStartup);
}

app.UseApi();

await app.RunAsync();

/// <summary>Entry point marker used by integration tests (WebApplicationFactory).</summary>
public partial class Program;
