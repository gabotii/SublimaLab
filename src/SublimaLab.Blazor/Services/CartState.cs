using SublimaLab.Blazor.Models;

namespace SublimaLab.Blazor.Services;

/// <summary>
/// The current cart, shared across components (header badge, cart page, checkout). Every
/// mutation returns the server-priced cart, which simply replaces the local copy.
/// </summary>
public sealed class CartState(ApiClient api)
{
    private Task? _initialLoad;

    public Cart Cart { get; private set; } = Cart.Empty;

    public bool IsLoaded { get; private set; }

    public event Action? Changed;

    public Task EnsureLoadedAsync() => _initialLoad ??= RefreshAsync();

    public async Task RefreshAsync(string? shippingMethod = null)
    {
        var url = shippingMethod is null ? "api/cart" : $"api/cart?shippingMethod={Uri.EscapeDataString(shippingMethod)}";
        Set(await api.GetAsync<Cart>(url));
    }

    public async Task AddAsync(Guid productId, Guid variantId, int quantity) =>
        Set(await api.PostAsync<Cart>("api/cart/items", new AddCartItemRequest(productId, variantId, quantity)));

    public async Task UpdateQuantityAsync(Guid itemId, int quantity) =>
        Set(await api.PutAsync<Cart>($"api/cart/items/{itemId}", new UpdateCartItemRequest(quantity)));

    public async Task RemoveAsync(Guid itemId) =>
        Set(await api.DeleteAsync<Cart>($"api/cart/items/{itemId}"));

    public async Task ApplyDiscountAsync(string code) =>
        Set(await api.PostAsync<Cart>("api/cart/discount", new ApplyDiscountRequest(code)));

    public async Task RemoveDiscountAsync() =>
        Set(await api.DeleteAsync<Cart>("api/cart/discount"));

    /// <summary>Called after sign-in/out, when the server-side cart changes owner.</summary>
    public Task ReloadAsync()
    {
        _initialLoad = RefreshAsync();
        return _initialLoad;
    }

    private void Set(Cart cart)
    {
        Cart = cart;
        IsLoaded = true;
        Changed?.Invoke();
    }
}
