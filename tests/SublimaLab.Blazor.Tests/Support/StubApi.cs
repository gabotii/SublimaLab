using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using SublimaLab.Blazor.Models;
using SublimaLab.Blazor.Services;

namespace SublimaLab.Blazor.Tests.Support;

/// <summary>An in-memory API: routes "METHOD path" to canned JSON responses and records requests.</summary>
public sealed class StubApi : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _routes = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Requests { get; } = [];

    public StubApi()
    {
        Json("GET api/antiforgery/token", new AntiforgeryToken("test-token", "X-XSRF-TOKEN"));
        Json("GET api/auth/session", new SessionResponse(null));
        Json("GET api/cart", Cart.Empty);
        Json<IReadOnlyList<Category>>("GET api/categories", []);
        Json<IReadOnlyList<ShippingMethod>>("GET api/shipping/methods", [new ShippingMethod("standard", "Standard Shipping", "Tracked", 4.99m, 50m, 3, 6, "USD")]);
    }

    public StubApi Json<T>(string route, T body, HttpStatusCode status = HttpStatusCode.OK)
    {
        _routes[route] = _ => new HttpResponseMessage(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, ApiClient.Json), Encoding.UTF8, "application/json"),
        };
        return this;
    }

    public StubApi Error(string route, HttpStatusCode status, string code, string message) =>
        Json(route, new ApiError(code, message, null, "trace"), status);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath.TrimStart('/');
        var key = $"{request.Method} {path}";
        Requests.Add($"{key}{request.RequestUri.Query}");

        return Task.FromResult(_routes.TryGetValue(key, out var handler)
            ? handler(request)
            : new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("""{"code":"NOT_FOUND","message":"Not stubbed"}""", Encoding.UTF8, "application/json"),
            });
    }

    /// <summary>Registers the storefront services backed by this stub.</summary>
    public void Register(IServiceCollection services)
    {
        services.AddSingleton(new HttpClient(this) { BaseAddress = new Uri("http://localhost/") });
        services.AddSingleton<ApiClient>();
        services.AddSingleton<AuthService>();
        services.AddSingleton<AuthenticationStateProvider>(sp => sp.GetRequiredService<AuthService>());
        services.AddSingleton<CartState>();
        services.AddSingleton<CatalogCache>();
        services.AddSingleton<ToastService>();
        services.AddAuthorizationCore();
        services.AddCascadingAuthenticationState();
    }
}

public static class TestModels
{
    public static ProductSummary Product(string slug = "classic-white-t-shirt", bool customizable = true, int colors = 1) => new(
        Guid.NewGuid(),
        "Classic White T-Shirt",
        slug,
        "Everyday tee",
        "t-shirts",
        "T-Shirts",
        19.99m,
        "USD",
        $"/images/products/{slug}.svg",
        "Classic White T-Shirt",
        customizable,
        false,
        Enumerable.Range(0, colors).Select(i => new ColorSwatch($"Color {i}", i == 0 ? "#FFFFFF" : "#A9D4F5")).ToList());

    public static Cart CartWith(params CartLine[] lines) => new(
        lines,
        lines.Sum(l => l.Quantity),
        new CartTotals(lines.Sum(l => l.LineTotal), 0, 4.99m, "standard", 2.08m, lines.Sum(l => l.LineTotal) + 4.99m + 2.08m),
        "USD",
        null,
        null,
        lines.Length > 0);

    public static CartLine Line(string name = "Classic White T-Shirt", int quantity = 1, decimal unit = 19.99m, decimal customization = 0m) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        name,
        "classic-white-t-shirt",
        null,
        new CartVariant(Guid.NewGuid(), "TSH-CW-WHI-M", "White", "#FFFFFF", "M", "White / M"),
        quantity,
        unit,
        customization,
        (unit + customization) * quantity,
        customization > 0,
        null,
        null,
        null,
        true,
        null);
}
