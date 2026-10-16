using SublimaLab.Domain.Common;
using SublimaLab.Domain.Designs;

namespace SublimaLab.Domain.Carts;

public sealed class CartItem : Entity
{
    private CartItem() { }

    internal CartItem(Guid cartId, Guid productId, Guid variantId, int quantity, DesignDocument? design, Guid? sourceDesignId, Guid? previewUploadId)
    {
        CartId = cartId;
        ProductId = productId;
        VariantId = variantId;
        Quantity = quantity;
        Design = design;
        SourceDesignId = sourceDesignId;
        PreviewUploadId = previewUploadId;
    }

    public Guid CartId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid VariantId { get; private set; }
    public int Quantity { get; private set; }

    /// <summary>The customization for this line, or null for a plain product.</summary>
    public DesignDocument? Design { get; private set; }

    /// <summary>The saved design this line was created from, if any.</summary>
    public Guid? SourceDesignId { get; private set; }
    public Guid? PreviewUploadId { get; private set; }

    internal void SetQuantity(int quantity) => Quantity = quantity;

    internal void SetCustomization(Guid variantId, DesignDocument design, Guid? previewUploadId)
    {
        VariantId = variantId;
        Design = design;
        PreviewUploadId = previewUploadId;
    }
}
