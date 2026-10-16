using SublimaLab.Domain.Common;
using SublimaLab.Domain.Designs;

namespace SublimaLab.Domain.Carts;

/// <summary>
/// A shopping cart owned by a user or by an anonymous session. The cart stores only what the
/// customer chose (product, variant, quantity, design), never prices: those are always
/// recalculated server-side from the current catalog.
/// </summary>
public sealed class Cart : Entity, IAuditable
{
    public const int MaxLines = 50;
    public const int MaxQuantityPerLine = 100;

    private readonly List<CartItem> _items = [];

    private Cart() { }

    public static Cart ForUser(Guid userId) => new() { UserId = userId };

    public static Cart ForSession(string sessionId) => new() { SessionId = sessionId };

    public Guid? UserId { get; private set; }
    public string? SessionId { get; private set; }
    public string? DiscountCode { get; private set; }
    public IReadOnlyCollection<CartItem> Items => _items;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public CartItem AddItem(Guid productId, Guid variantId, int quantity, DesignDocument? design, Guid? sourceDesignId, Guid? previewUploadId)
    {
        EnsureValidQuantity(quantity);

        // Plain lines of the same variant are merged; customized lines are always distinct.
        if (design is null)
        {
            var existing = _items.FirstOrDefault(i => i.Design is null && i.ProductId == productId && i.VariantId == variantId);
            if (existing is not null)
            {
                existing.SetQuantity(Math.Min(existing.Quantity + quantity, MaxQuantityPerLine));
                return existing;
            }
        }

        if (_items.Count >= MaxLines)
        {
            throw new DomainException("CART_FULL", $"A cart can contain at most {MaxLines} different items.");
        }

        var item = new CartItem(Id, productId, variantId, quantity, design, sourceDesignId, previewUploadId);
        _items.Add(item);
        return item;
    }

    public CartItem UpdateQuantity(Guid itemId, int quantity)
    {
        EnsureValidQuantity(quantity);
        var item = GetItem(itemId);
        item.SetQuantity(quantity);
        return item;
    }

    public CartItem UpdateCustomization(Guid itemId, Guid variantId, DesignDocument design, Guid? previewUploadId)
    {
        var item = GetItem(itemId);
        if (item.Design is null)
        {
            throw new DomainException("CART_ITEM_NOT_CUSTOMIZED", "Only customized items can have their design edited.");
        }

        item.SetCustomization(variantId, design, previewUploadId);
        return item;
    }

    public void RemoveItem(Guid itemId) => _items.Remove(GetItem(itemId));

    public void Clear()
    {
        _items.Clear();
        DiscountCode = null;
    }

    public void ApplyDiscountCode(string code) => DiscountCode = code.Trim().ToUpperInvariant();

    public void RemoveDiscountCode() => DiscountCode = null;

    /// <summary>Moves the lines of a guest cart into this cart after the guest signs in.</summary>
    public void MergeFrom(Cart guestCart)
    {
        foreach (var item in guestCart.Items)
        {
            var mergesIntoExistingLine = item.Design is null &&
                _items.Any(i => i.Design is null && i.ProductId == item.ProductId && i.VariantId == item.VariantId);

            if (_items.Count >= MaxLines && !mergesIntoExistingLine)
            {
                break;
            }

            AddItem(item.ProductId, item.VariantId, item.Quantity, item.Design, item.SourceDesignId, item.PreviewUploadId);
        }

        DiscountCode ??= guestCart.DiscountCode;
        guestCart.Clear();
    }

    private CartItem GetItem(Guid itemId) =>
        _items.FirstOrDefault(i => i.Id == itemId)
        ?? throw new DomainException("CART_ITEM_NOT_FOUND", "The cart item does not exist.");

    private static void EnsureValidQuantity(int quantity)
    {
        if (quantity is < 1 or > MaxQuantityPerLine)
        {
            throw new DomainException("INVALID_QUANTITY", $"Quantity must be between 1 and {MaxQuantityPerLine}.");
        }
    }
}
