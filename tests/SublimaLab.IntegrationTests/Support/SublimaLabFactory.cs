using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SublimaLab.Application.Abstractions;

namespace SublimaLab.IntegrationTests.Support;

/// <summary>
/// Hosts the real API (all middleware, auth, antiforgery, EF Core migrations, seed data) against
/// a throwaway SQLite database. Only the outbound email sender is replaced, to capture links.
/// </summary>
public class SublimaLabFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin@tests.local";
    public const string AdminPassword = "Admin-Tests-123";

    private readonly string _workDirectory = Path.Combine(Path.GetTempPath(), "sublimalab-it-" + Guid.NewGuid().ToString("N"));

    public CapturingEmailSender Emails { get; } = new();

    public SublimaLabFactory()
    {
        ClientOptions.BaseAddress = new Uri("https://localhost");
        ClientOptions.AllowAutoRedirect = false;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_workDirectory);
        builder.UseEnvironment("Testing");

        // Static web assets (the Blazor storefront) are only wired up automatically in Development.
        builder.UseStaticWebAssets();

        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = $"Data Source={Path.Combine(_workDirectory, "tests.db")}",
            ["Database:Provider"] = "Sqlite",
            ["Database:MigrateOnStartup"] = "true",
            ["App:PublicBaseUrl"] = "https://localhost",
            ["Storage:RootPath"] = Path.Combine(_workDirectory, "uploads"),
            ["Email:Provider"] = "Pickup",
            ["Seed:AdminEmail"] = AdminEmail,
            ["Seed:AdminPassword"] = AdminPassword,
            ["RateLimiting:GlobalPermitLimit"] = "100000",
            ["RateLimiting:AuthPermitLimit"] = "100000",
            ["RateLimiting:UploadPermitLimit"] = "100000",
            ["Retention:Enabled"] = "false",
            ["Auth:RequireAdminTwoFactor"] = "false",
            ["Prerendering:ApiBaseUrl"] = "https://localhost",
            ["DataProtection:KeysPath"] = Path.Combine(_workDirectory, "keys"),
            ["Logging:LogLevel:Default"] = "Warning",
            ["Logging:Console:FormatterName"] = "simple",
        };

        ConfigureSettings(settings);
        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IEmailSender>(Emails);

            // There is no real socket in tests: prerender calls go through the in-memory server.
            services.AddHttpClient(SublimaLab.Api.Hosting.StorefrontRendering.PrerenderClientName)
                .ConfigurePrimaryHttpMessageHandler(() => Server.CreateHandler());
        });
    }

    /// <summary>Lets a derived factory change configuration (e.g. to turn a feature on).</summary>
    protected virtual void ConfigureSettings(IDictionary<string, string?> settings)
    {
    }

    public async Task<ApiClient> CreateApiClientAsync()
    {
        var client = new ApiClient(CreateClient());
        await client.RefreshAntiforgeryAsync();
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(_workDirectory, recursive: true);
            }
            catch (IOException)
            {
                // Best effort: temp files are cleaned by the OS eventually.
            }
        }
    }
}

public sealed partial class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _messages = new();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        _messages.Enqueue(message);
        return Task.CompletedTask;
    }

    public EmailMessage Last(string to) =>
        _messages.Reverse().FirstOrDefault(m => string.Equals(m.To, to, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"No email was sent to {to}.");

    /// <summary>Extracts the query string values of the first link in the email.</summary>
    public static Dictionary<string, string> LinkParameters(EmailMessage message)
    {
        var href = HrefPattern().Match(message.HtmlBody).Groups[1].Value;
        var query = new Uri(href).Query.TrimStart('?');
        return query.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
    }

    [GeneratedRegex("href=\"([^\"]+)\"")]
    private static partial Regex HrefPattern();
}
