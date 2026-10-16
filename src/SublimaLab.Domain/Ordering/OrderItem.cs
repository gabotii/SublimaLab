using SublimaLab.Domain.Common;
using SublimaLab.Domain.Designs;

namespace SublimaLab.Domain.Ordering;

/// <summary>
/// A purchased line. Product data, prices and the full design are snapshotted so the order
/// stays accurate even if the catalog changes or the product is later deleted.
/// </summary>
public sealed class OrderItem : Entity
{
    private OrderItem() { }

    public OrderItem(
        Guid productId,
        Guid variantId,
        string productName,
        string sku,
        string variantLabel,
        string colorHex,
        string? imageUrl,
        int quantity,
        decimal unitPrice,
        decimal customizationPrice,
        DesignDocument? design,
        ProductionSpec? productionSpec,
        Guid? previewUploadId)
    {
        ProductId = productId;
        VariantId = variantId;
        ProductName = productName;
        Sku = sku;
        VariantLabel = variantLabel;
        ColorHex = colorHex;
        ImageUrl = imageUrl;
        Quantity = quantity;
        UnitPrice = unitPrice;
        CustomizationPrice = customizationPrice;
        LineTotal = Money.Round((unitPrice + customizationPrice) * quantity);
        Design = design;
        ProductionSpec = productionSpec;
        PreviewUploadId = previewUploadId;
    }

    public Guid OrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid VariantId { get; private set; }
    public string ProductName { get; private set; } = string.Empty;
    public string Sku { get; private set; } = string.Empty;
    public string VariantLabel { get; private set; } = string.Empty;
    public string ColorHex { get; private set; } = string.Empty;
    public string? ImageUrl { get; private set; }
    public int Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal CustomizationPrice { get; private set; }
    public decimal LineTotal { get; private set; }
    public DesignDocument? Design { get; private set; }
    public ProductionSpec? ProductionSpec { get; private set; }
    public Guid? PreviewUploadId { get; private set; }
}
