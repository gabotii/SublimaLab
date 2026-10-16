using SublimaLab.Domain.Common;

namespace SublimaLab.Domain.Designs;

/// <summary>A design saved to a customer's account so it can be reused or re-ordered.</summary>
public sealed class Design : Entity, IAuditable
{
    private Design() { }

    public Design(Guid userId, Guid productId, string name, DesignDocument document, Guid? previewUploadId)
    {
        UserId = userId;
        ProductId = productId;
        Name = name;
        Document = document;
        PreviewUploadId = previewUploadId;
    }

    public Guid UserId { get; private set; }
    public Guid ProductId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public DesignDocument Document { get; private set; } = new();
    public Guid? PreviewUploadId { get; private set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public void Update(string name, DesignDocument document, Guid? previewUploadId)
    {
        Name = name;
        Document = document;
        PreviewUploadId = previewUploadId;
    }
}
