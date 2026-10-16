using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace SublimaLab.Blazor.Services;

public static class StorefrontServices
{
    /// <summary>
    /// Registers the storefront's services. Used by the WebAssembly app and, for prerendering, by
    /// the server, which also provides the <see cref="HttpClient"/> each host calls the API with.
    /// </summary>
    public static IServiceCollection AddStorefront(this IServiceCollection services, bool prerendering = false)
    {
        services.AddScoped(_ => new PrerenderCache(isRecording: prerendering));
        services.AddScoped<ApiClient>();

        services.AddAuthorizationCore();
        services.AddCascadingAuthenticationState();
        services.AddScoped<AuthService>();
        services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<AuthService>());

        services.AddScoped<CartState>();
        services.AddScoped<CatalogCache>();
        services.AddScoped<ToastService>();
        return services;
    }
}
