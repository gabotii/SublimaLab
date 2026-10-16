using SublimaLab.Blazor.Models;

namespace SublimaLab.Blazor.Services;

/// <summary>
/// Caches rarely-changing reference data (categories, shipping methods) for the session,
/// so navigating between pages doesn't refetch it.
/// </summary>
public sealed class CatalogCache(ApiClient api)
{
    private Task<IReadOnlyList<Category>>? _categories;
    private Task<IReadOnlyList<ShippingMethod>>? _shippingMethods;

    public Task<IReadOnlyList<Category>> GetCategoriesAsync() =>
        _categories ??= LoadAsync(() => api.GetAsync<IReadOnlyList<Category>>("api/categories"), () => _categories = null);

    public Task<IReadOnlyList<ShippingMethod>> GetShippingMethodsAsync() =>
        _shippingMethods ??= LoadAsync(() => api.GetAsync<IReadOnlyList<ShippingMethod>>("api/shipping/methods"), () => _shippingMethods = null);

    /// <summary>Free-shipping threshold of the default (standard) method, if any.</summary>
    public async Task<decimal?> GetFreeShippingThresholdAsync()
    {
        var methods = await GetShippingMethodsAsync();
        return methods.FirstOrDefault(m => m.Code == "standard")?.FreeShippingThreshold;
    }

    private static async Task<T> LoadAsync<T>(Func<Task<T>> load, Action reset)
    {
        try
        {
            return await load();
        }
        catch
        {
            reset(); // Don't cache failures.
            throw;
        }
    }
}
