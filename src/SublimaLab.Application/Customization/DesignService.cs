using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SublimaLab.Application.Abstractions;
using SublimaLab.Application.Common;
using SublimaLab.Application.Uploads;
using SublimaLab.Domain.Catalog;
using SublimaLab.Domain.Designs;
using SublimaLab.Domain.Uploads;

namespace SublimaLab.Application.Customization;

public sealed record SaveDesignRequest(Guid ProductId, string Name, DesignDocument Design, Guid? PreviewUploadId);

public sealed record ValidateDesignRequest(Guid ProductId, DesignDocument Design);

public sealed record DesignDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string ProductSlug,
    string Name,
    DesignDocument Design,
    string? PreviewUrl,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<DesignWarning> Warnings);

public sealed class SaveDesignRequestValidator : AbstractValidator<SaveDesignRequest>
{
    public SaveDesignRequestValidator()
    {
        RuleFor(r => r.ProductId).NotEmpty();
        RuleFor(r => r.Name).NotEmpty().MaximumLength(80);
        RuleFor(r => r.Design).NotNull();
    }
}

/// <summary>Designs saved to a customer account (requires authentication).</summary>
public sealed class DesignService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IClientSession session,
    DesignValidationService designValidation,
    IValidator<SaveDesignRequest> requestValidator)
{
    public async Task<DesignValidationResult> ValidateAsync(ValidateDesignRequest request, CancellationToken cancellationToken)
    {
        var product = await LoadCustomizableProductAsync(request.ProductId, cancellationToken);
        return await designValidation.ValidateAsync(product, request.Design, "design", cancellationToken);
    }

    public async Task<IReadOnlyList<DesignDto>> ListMineAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var rows = await db.Designs
            .AsNoTracking()
            .Where(d => d.UserId == userId)
            .OrderByDescending(d => d.UpdatedAt)
            .Join(db.Products, d => d.ProductId, p => p.Id, (d, p) => new { Design = d, p.Name, p.Slug })
            .ToListAsync(cancellationToken);

        return rows.Select(r => ToDto(r.Design, r.Name, r.Slug, [])).ToList();
    }

    public async Task<DesignDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var design = await FindOwnedAsync(id, cancellationToken);
        var product = await db.Products.AsNoTracking().FirstAsync(p => p.Id == design.ProductId, cancellationToken);
        return ToDto(design, product.Name, product.Slug, []);
    }

    public async Task<DesignDto> CreateAsync(SaveDesignRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        await requestValidator.ValidateOrThrowAsync(request, cancellationToken);

        var product = await LoadCustomizableProductAsync(request.ProductId, cancellationToken);
        var validation = await designValidation.EnsureValidAsync(product, request.Design, "design", cancellationToken);
        await EnsurePreviewAccessibleAsync(request.PreviewUploadId, cancellationToken);

        var design = new Design(userId, product.Id, request.Name.Trim(), request.Design, request.PreviewUploadId);
        db.Designs.Add(design);
        await db.SaveChangesAsync(cancellationToken);

        return ToDto(design, product.Name, product.Slug, validation.Warnings);
    }

    public async Task<DesignDto> UpdateAsync(Guid id, SaveDesignRequest request, CancellationToken cancellationToken)
    {
        await requestValidator.ValidateOrThrowAsync(request, cancellationToken);
        var design = await FindOwnedAsync(id, cancellationToken, track: true);

        if (request.ProductId != design.ProductId)
        {
            throw RequestValidationException.For("productId", "A saved design cannot be moved to another product.");
        }

        var product = await LoadCustomizableProductAsync(design.ProductId, cancellationToken);
        var validation = await designValidation.EnsureValidAsync(product, request.Design, "design", cancellationToken);
        await EnsurePreviewAccessibleAsync(request.PreviewUploadId, cancellationToken);

        design.Update(request.Name.Trim(), request.Design, request.PreviewUploadId);
        await db.SaveChangesAsync(cancellationToken);

        return ToDto(design, product.Name, product.Slug, validation.Warnings);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var design = await FindOwnedAsync(id, cancellationToken, track: true);
        db.Designs.Remove(design);
        await db.SaveChangesAsync(cancellationToken);
    }

    internal async Task<Design> FindOwnedAsync(Guid id, CancellationToken cancellationToken, bool track = false)
    {
        var userId = currentUser.RequireUserId();
        var query = track ? db.Designs : db.Designs.AsNoTracking();

        // Someone else's design is reported as "not found" so ids cannot be probed.
        return await query.FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("Design");
    }

    private async Task<Product> LoadCustomizableProductAsync(Guid productId, CancellationToken cancellationToken)
    {
        var product = await db.Products
            .AsNoTracking()
            .Include(p => p.Customization).ThenInclude(c => c!.PrintAreas)
            .FirstOrDefaultAsync(p => p.Id == productId && p.IsActive, cancellationToken)
            ?? throw new NotFoundException("Product");

        return product.IsCustomizable
            ? product
            : throw new BusinessRuleException("PRODUCT_NOT_CUSTOMIZABLE", "This product cannot be customized.");
    }

    private async Task EnsurePreviewAccessibleAsync(Guid? previewUploadId, CancellationToken cancellationToken)
    {
        if (previewUploadId is not { } id)
        {
            return;
        }

        var upload = await db.Uploads.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id && u.Kind == UploadKind.Preview, cancellationToken);
        if (upload is null || !upload.IsAccessibleBy(currentUser.UserId, session.SessionId))
        {
            throw RequestValidationException.For("previewUploadId", "The preview image does not exist or is not accessible.");
        }
    }

    private static DesignDto ToDto(Design design, string productName, string productSlug, IReadOnlyList<DesignWarning> warnings) => new(
        design.Id,
        design.ProductId,
        productName,
        productSlug,
        design.Name,
        design.Document,
        design.PreviewUploadId is { } previewId ? UploadUrls.Content(previewId) : null,
        design.CreatedAt,
        design.UpdatedAt,
        warnings);
}
