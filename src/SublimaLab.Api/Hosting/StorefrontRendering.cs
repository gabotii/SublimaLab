using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using SublimaLab.Blazor.Services;

namespace SublimaLab.Api.Hosting;

/// <summary>
/// Server-side prerendering of the storefront. While prerendering, the storefront's own
/// <see cref="ApiClient"/> runs on the server and calls this app over loopback with the
/// visitor's cookies, so pages render exactly as they would in the browser (signed-in header,
/// cart badge) without a second, server-only code path.
/// </summary>
internal static class StorefrontRendering
{
    public const string PrerenderClientName = "SublimaLab.Prerender";

    public static readonly IComponentRenderMode Prerendered = new InteractiveWebAssemblyRenderMode(prerender: true);
    public static readonly IComponentRenderMode ClientOnly = new InteractiveWebAssemblyRenderMode(prerender: false);

    /// <summary>Public pages that are the same for everyone and worth indexing.</summary>
    private static readonly string[] PrerenderedPrefixes = ["/shop", "/products/", "/terms", "/privacy", "/shipping-returns", "/contact", "/not-found"];

    /// <summary>The not-found page's catch-all route; matching it means "no such page".</summary>
    private const string NotFoundRoute = "/{*path:nonfile}";

    public static bool ShouldPrerender(HttpContext context)
    {
        // Unknown URLs are prerendered too, so the 404 status and page come from the server.
        if (context.GetEndpoint() is RouteEndpoint { RoutePattern.RawText: NotFoundRoute })
        {
            return true;
        }

        var value = context.Request.Path.Value ?? "/";
        return value is "/" or "" || PrerenderedPrefixes.Any(prefix => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    public static IServiceCollection AddStorefrontRendering(this IServiceCollection services)
    {
        services.AddRazorComponents().AddInteractiveWebAssemblyComponents();
        services.AddStorefront(prerendering: true);

        services.AddSingleton<InternalRequestKey>();
        services.AddSingleton<LoopbackAddress>();
        services.AddTransient<PrerenderForwardingHandler>();
        services.AddHttpClient(PrerenderClientName)
            .AddHttpMessageHandler<PrerenderForwardingHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The visitor's Cookie header is forwarded as-is; no cookie container or redirects.
                UseCookies = false,
                AllowAutoRedirect = false,
            });

        services.AddScoped(sp =>
        {
            var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient(PrerenderClientName);
            client.BaseAddress = sp.GetRequiredService<LoopbackAddress>().BaseAddress;
            client.Timeout = TimeSpan.FromSeconds(10);
            return client;
        });

        return services;
    }
}

/// <summary>
/// A per-process secret that marks prerender calls as coming from this app, so they don't use up
/// the visitor's (or the loopback address's) rate-limit budget.
/// </summary>
internal sealed class InternalRequestKey
{
    public const string HeaderName = "X-SublimaLab-Internal";

    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    public string Value => Convert.ToBase64String(_key);

    public bool IsInternal(HttpContext context) =>
        context.Request.Headers.TryGetValue(HeaderName, out var header) &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(header.ToString()), Encoding.ASCII.GetBytes(Value));
}

/// <summary>
/// Where the app can reach itself: <c>Prerendering:ApiBaseUrl</c> when set, otherwise one of
/// Kestrel's own addresses (HTTPS preferred, wildcard hosts mapped to localhost).
/// </summary>
internal sealed class LoopbackAddress(IServer server, IConfiguration configuration)
{
    private Uri? _baseAddress;

    public Uri BaseAddress => _baseAddress ??= Resolve();

    private Uri Resolve()
    {
        if (configuration["Prerendering:ApiBaseUrl"] is { Length: > 0 } configured)
        {
            return new Uri(configured.TrimEnd('/') + "/");
        }

        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses ?? [];
        var address = addresses.FirstOrDefault(a => a.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            ?? addresses.FirstOrDefault()
            ?? throw new InvalidOperationException("The server has no listening address to prerender against; set Prerendering:ApiBaseUrl.");

        var builder = new UriBuilder(address.Replace("://+", "://localhost", StringComparison.Ordinal).Replace("://*", "://localhost", StringComparison.Ordinal));
        if (builder.Host is "0.0.0.0" or "[::]" or "::")
        {
            builder.Host = "localhost";
        }

        builder.Path = "/";
        return builder.Uri;
    }
}

/// <summary>Adds the visitor's cookies and the internal marker to prerender API calls.</summary>
internal sealed class PrerenderForwardingHandler(IHttpContextAccessor httpContextAccessor, InternalRequestKey key) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (httpContextAccessor.HttpContext?.Request.Headers.Cookie is { Count: > 0 } cookies)
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookies.ToString());
        }

        request.Headers.Add(InternalRequestKey.HeaderName, key.Value);
        return base.SendAsync(request, cancellationToken);
    }
}
