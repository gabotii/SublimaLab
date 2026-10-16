using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SublimaLab.Application.Abstractions;
using SublimaLab.Application.Common;
using SublimaLab.Domain.Catalog;
using SublimaLab.Domain.Customization;

namespace SublimaLab.Application.Admin;

public sealed class SaveProductRequestValidator : AbstractValidator<SaveProductRequest>
{
    public SaveProductRequestValidator()
    {
        RuleFor(r => r.CategoryId).NotEmpty();
        RuleFor(r => r.Name).NotEmpty().MaximumLength(120);
        RuleFor(r => r.Slug).NotEmpty().MaximumLength(120).Matches("^[a-z0-9]+(-[a-z0-9]+)*$").WithMessage("Use lowercase letters, numbers and single hyphens (e.g. classic-white-t-shirt).");
        RuleFor(r => r.ShortDescription).MaximumLength(300);
        RuleFor(r => r.Description).MaximumLength(4000);
        RuleFor(r => r.BasePrice).InclusiveBetween(0, 100_000);
        RuleFor(r => r.ProductionTimeDays).InclusiveBetween(0, 60);
        RuleForEach(r => r.Specifications).ChildRules(s =>
        {
            s.RuleFor(x => x.Name).NotEmpty().MaximumLength(60);
            s.RuleFor(x => x.Value).NotEmpty().MaximumLength(200);
        });
    }
}

public sealed class SaveVariantsRequestValidator : AbstractValidator<SaveVariantsRequest>
{
    public SaveVariantsRequestValidator()
    {
        RuleFor(r => r.Variants).NotEmpty().WithMessage("A product needs at least one variant.")
            .Must(v => v.Select(x => x.Sku.Trim().ToUpperInvariant()).Distinct().Count() == v.Count).WithMessage("SKUs must be unique.");
        RuleForEach(r => r.Variants).ChildRules(v =>
        {
            v.RuleFor(x => x.Sku).NotEmpty().MaximumLength(64).Matches("^[A-Za-z0-9-]+$");
            v.RuleFor(x => x.ColorName).NotEmpty().MaximumLength(60);
            v.RuleFor(x => x.ColorHex).NotEmpty().Matches("^#[0-9A-Fa-f]{6}$").WithMessage("Use a hex color like #FFFFFF.");
            v.RuleFor(x => x.Size).MaximumLength(30);
            v.RuleFor(x => x.PriceAdjustment).InclusiveBetween(-10_000, 10_000);
        });
    }
}

public sealed class SaveCustomizationRequestValidator : AbstractValidator<SaveCustomizationRequest>
{
    public SaveCustomizationRequestValidator()
    {
        RuleFor(r => r.Kind).Must(k => Enum.TryParse<CustomizerKind>(k, true, out _)).WithMessage("Unknown product kind.");
        RuleFor(r => r.ModelUrl).MaximumLength(500).Matches("^/models/[a-z0-9-]+\\.glb$").When(r => !string.IsNullOrEmpty(r.ModelUrl))
            .WithMessage("Choose one of the 3D models in /models.");
        RuleFor(r => r.BaseFee).InclusiveBetween(0, 1000);
        RuleFor(r => r.AdditionalPrintAreaFee).InclusiveBetween(0, 1000);
        RuleFor(r => r.MaxElementsPerArea).InclusiveBetween(1, 50);
        RuleFor(r => r.MinRecommendedDpi).InclusiveBetween(50, 1200);
        RuleFor(r => r.PrintAreas).NotEmpty().When(r => r.IsEnabled).WithMessage("Customizable products need at least one print area.")
            .Must(a => a.Select(x => x.Key.ToLowerInvariant()).Distinct().Count() == a.Count).WithMessage("Print area keys must be unique.");
        RuleForEach(r => r.PrintAreas).ChildRules(a =>
        {
            a.RuleFor(x => x.Key).NotEmpty().MaximumLength(40).Matches("^[a-z0-9-]+$");
            a.RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
            a.RuleFor(x => x.WidthMm).InclusiveBetween(5, 2000);
            a.RuleFor(x => x.HeightMm).InclusiveBetween(5, 2000);
            a.RuleFor(x => x.Shape).Must(s => Enum.TryParse<PrintAreaShape>(s, true, out _)).WithMessage("Shape must be Rectangle or Ellipse.");
        });
    }
}

/// <summary>Product management for administrators. Products are deactivated rather than deleted to keep history intact.</summary>
public sealed class AdminCatalogService(
    IAppDbContext db,
    IFileStorage storage,
    IImageInspector imageInspector,
    IImageSanitizer imageSanitizer,
    IValidator<SaveProductRequest> productValidator,
    IValidator<SaveVariantsRequest> variantsValidator,
    IValidator<SaveCustomizationRequest> customizationValidator,
    TimeProvider clock,
    ILogger<AdminCatalogService> logger)
{
    private const long MaxImageBytes = 8 * 1024 * 1024;

    // Bounds decode memory for product photos (4 bytes per pixel).
    private const int MaxImageSidePx = 6000;

    public async Task<IReadOnlyList<AdminCategoryDto>> GetCategoriesAsync(CancellationToken cancellationToken) =>
        await db.Categories.AsNoTracking()
            .OrderBy(c => c.SortOrder)
            .Select(c => new AdminCategoryDto(c.Id, c.Name, c.Slug, c.IsActive, c.Products.Count))
            .ToListAsync(cancellationToken);

    public async Task<PagedResult<AdminProductSummaryDto>> ListAsync(string? search, Guid? categoryId, bool? active, int page, int pageSize, CancellationToken cancellationToken)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.Products.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(p => p.Name.ToLower().Contains(term) || p.Slug.Contains(term) || p.Variants.Any(v => v.Sku.ToLower().Contains(term)));
        }

        if (categoryId is { } category)
        {
            query = query.Where(p => p.CategoryId == category);
        }

        if (active is { } isActive)
        {
            query = query.Where(p => p.IsActive == isActive);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(p => p.Category!.SortOrder).ThenBy(p => p.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new AdminProductSummaryDto(
                p.Id,
                p.Name,
                p.Slug,
                p.Category!.Name,
                p.BasePrice,
                p.IsActive,
                p.IsFeatured,
                p.Customization != null && p.Customization.IsEnabled,
                p.Variants.Count(v => v.IsActive),
                p.Images.OrderBy(i => i.SortOrder).Select(i => i.Url).FirstOrDefault(),
                p.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<AdminProductSummaryDto>(items, page, pageSize, total);
    }

    public async Task<AdminProductDto> GetAsync(Guid id, CancellationToken cancellationToken) =>
        ToDto(await LoadAsync(id, track: false, cancellationToken));

    public async Task<AdminProductDto> CreateAsync(SaveProductRequest request, CancellationToken cancellationToken)
    {
        await productValidator.ValidateOrThrowAsync(request, cancellationToken);
        await EnsureCategoryAsync(request.CategoryId, cancellationToken);
        await EnsureSlugAvailableAsync(request.Slug, null, cancellationToken);

        var product = new Product { Name = request.Name.Trim(), Slug = request.Slug };
        Apply(product, request);
        db.Products.Add(product);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Product {Slug} created", product.Slug);
        return await GetAsync(product.Id, cancellationToken);
    }

    public async Task<AdminProductDto> UpdateAsync(Guid id, SaveProductRequest request, CancellationToken cancellationToken)
    {
        await productValidator.ValidateOrThrowAsync(request, cancellationToken);
        await EnsureCategoryAsync(request.CategoryId, cancellationToken);
        await EnsureSlugAvailableAsync(request.Slug, id, cancellationToken);

        var product = await LoadAsync(id, track: true, cancellationToken);
        Apply(product, request);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(product);
    }

    public async Task<AdminProductDto> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken)
    {
        var product = await LoadAsync(id, track: true, cancellationToken);
        if (isActive && !product.Variants.Any(v => v.IsActive))
        {
            throw new BusinessRuleException("PRODUCT_HAS_NO_VARIANTS", "Add at least one active variant before activating this product.");
        }

        product.IsActive = isActive;
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Product {Slug} {State}", product.Slug, isActive ? "activated" : "deactivated");
        return ToDto(product);
    }

    /// <summary>Creates or updates variants; variants left out are deactivated (carts and orders may reference them).</summary>
    public async Task<AdminProductDto> SaveVariantsAsync(Guid id, SaveVariantsRequest request, CancellationToken cancellationToken)
    {
        await variantsValidator.ValidateOrThrowAsync(request, cancellationToken);
        var product = await LoadAsync(id, track: true, cancellationToken);

        var skus = request.Variants.Select(v => v.Sku.Trim().ToUpperInvariant()).ToList();
        var ownIds = product.Variants.Select(v => v.Id).ToList();
        var clash = await db.ProductVariants.AsNoTracking().Where(v => skus.Contains(v.Sku) && !ownIds.Contains(v.Id)).Select(v => v.Sku).FirstOrDefaultAsync(cancellationToken);
        if (clash is not null)
        {
            throw RequestValidationException.For("variants", $"SKU {clash} is already used by another product.");
        }

        var sort = 0;
        var kept = new HashSet<Guid>();
        foreach (var item in request.Variants)
        {
            var variant = item.Id is { } variantId ? product.Variants.FirstOrDefault(v => v.Id == variantId) : null;
            if (variant is null)
            {
                variant = new ProductVariant { Sku = item.Sku, ColorName = item.ColorName, ColorHex = item.ColorHex, ProductId = product.Id };
                product.Variants.Add(variant);
            }

            variant.Sku = item.Sku.Trim().ToUpperInvariant();
            variant.ColorName = item.ColorName.Trim();
            variant.ColorHex = item.ColorHex.ToUpperInvariant();
            variant.Size = string.IsNullOrWhiteSpace(item.Size) ? null : item.Size.Trim();
            variant.PriceAdjustment = Domain.Common.Money.Round(item.PriceAdjustment);
            variant.IsActive = item.IsActive;
            variant.SortOrder = sort++;
            kept.Add(variant.Id);
        }

        foreach (var removed in product.Variants.Where(v => !kept.Contains(v.Id)))
        {
            removed.IsActive = false;
        }

        if (product.IsActive && !product.Variants.Any(v => v.IsActive))
        {
            throw new BusinessRuleException("PRODUCT_HAS_NO_VARIANTS", "An active product needs at least one active variant. Deactivate the product first.");
        }

        product.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(product);
    }

    public async Task<AdminProductDto> SaveCustomizationAsync(Guid id, SaveCustomizationRequest request, CancellationToken cancellationToken)
    {
        await customizationValidator.ValidateOrThrowAsync(request, cancellationToken);
        var product = await LoadAsync(id, track: true, cancellationToken);

        var configuration = product.Customization;
        if (configuration is null)
        {
            configuration = new CustomizationConfiguration { ProductId = product.Id };
            product.Customization = configuration;
        }

        configuration.IsEnabled = request.IsEnabled;
        configuration.Kind = Enum.Parse<CustomizerKind>(request.Kind, true);
        configuration.ModelUrl = string.IsNullOrWhiteSpace(request.ModelUrl) ? null : request.ModelUrl;
        configuration.BaseFee = Domain.Common.Money.Round(request.BaseFee);
        configuration.AdditionalPrintAreaFee = Domain.Common.Money.Round(request.AdditionalPrintAreaFee);
        configuration.MaxElementsPerArea = request.MaxElementsPerArea;
        configuration.MinRecommendedDpi = request.MinRecommendedDpi;

        var sort = 0;
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in request.PrintAreas)
        {
            var key = item.Key.ToLowerInvariant();
            keys.Add(key);
            var area = configuration.PrintAreas.FirstOrDefault(a => a.Key == key);
            if (area is null)
            {
                area = new PrintArea { Key = key, Name = item.Name, CustomizationConfigurationId = configuration.Id };
                configuration.PrintAreas.Add(area);
            }

            area.Name = item.Name.Trim();
            area.WidthMm = item.WidthMm;
            area.HeightMm = item.HeightMm;
            area.Shape = Enum.Parse<PrintAreaShape>(item.Shape, true);
            area.SortOrder = sort++;
        }

        foreach (var removed in configuration.PrintAreas.Where(a => !keys.Contains(a.Key)).ToList())
        {
            configuration.PrintAreas.Remove(removed);
        }

        product.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Customization of {Slug} updated ({AreaCount} print areas)", product.Slug, request.PrintAreas.Count);
        return ToDto(product);
    }

    public async Task<AdminProductDto> AddImageAsync(Guid id, Stream content, string fileName, long length, string? altText, CancellationToken cancellationToken)
    {
        var product = await LoadAsync(id, track: true, cancellationToken);
        if (length <= 0 || length > MaxImageBytes)
        {
            throw RequestValidationException.For("file", "Product photos must be PNG, JPG or WebP files up to 8 MB.");
        }

        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length > MaxImageBytes)
        {
            throw RequestValidationException.For("file", "Product photos must be PNG, JPG or WebP files up to 8 MB.");
        }

        buffer.Position = 0;
        var image = imageInspector.Inspect(buffer) ?? throw RequestValidationException.For("file", "The file is not a valid PNG, JPG or WebP image.");
        if (image.Width > MaxImageSidePx || image.Height > MaxImageSidePx)
        {
            throw RequestValidationException.For("file", $"Product photos can be at most {MaxImageSidePx} pixels per side.");
        }

        buffer.Position = 0;
        var sanitized = await imageSanitizer.SanitizeAsync(buffer, image, cancellationToken)
            ?? throw RequestValidationException.For("file", "The image appears to be damaged and could not be read.");
        image = sanitized.Info;
        await using var clean = new MemoryStream(sanitized.Content, writable: false);
        var key = await storage.SaveAsync(clean, image.Extension, cancellationToken);

        var productImage = new ProductImage
        {
            ProductId = product.Id,
            Url = string.Empty,
            AltText = string.IsNullOrWhiteSpace(altText) ? product.Name : altText.Trim()[..Math.Min(altText.Trim().Length, 200)],
            SortOrder = product.Images.Select(i => i.SortOrder).DefaultIfEmpty(-1).Max() + 1,
            StorageKey = key,
            ContentType = image.ContentType,
        };
        productImage.Url = ProductImage.UploadedUrl(productImage.Id);
        product.Images.Add(productImage);
        product.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Image {ImageId} added to {Slug} ({FileName})", productImage.Id, product.Slug, Path.GetFileName(fileName));
        return ToDto(product);
    }

    public async Task<AdminProductDto> RemoveImageAsync(Guid id, Guid imageId, CancellationToken cancellationToken)
    {
        var product = await LoadAsync(id, track: true, cancellationToken);
        var image = product.Images.FirstOrDefault(i => i.Id == imageId) ?? throw new NotFoundException("Image");
        product.Images.Remove(image);
        product.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
        if (image.StorageKey is not null)
        {
            await storage.DeleteAsync(image.StorageKey, cancellationToken);
        }

        return ToDto(product);
    }

    /// <summary>Public read of an uploaded product photo.</summary>
    public async Task<(Stream Content, string ContentType)> OpenImageAsync(Guid imageId, CancellationToken cancellationToken)
    {
        var image = await db.Products.AsNoTracking().SelectMany(p => p.Images).FirstOrDefaultAsync(i => i.Id == imageId && i.StorageKey != null, cancellationToken)
            ?? throw new NotFoundException("Image");
        var stream = await storage.OpenReadAsync(image.StorageKey!, cancellationToken) ?? throw new NotFoundException("Image");
        return (stream, image.ContentType ?? "application/octet-stream");
    }

    private async Task<Product> LoadAsync(Guid id, bool track, CancellationToken cancellationToken)
    {
        var query = db.Products
            .Include(p => p.Variants)
            .Include(p => p.Images)
            .Include(p => p.Customization).ThenInclude(c => c!.PrintAreas)
            .AsQueryable();
        if (!track)
        {
            query = query.AsNoTracking();
        }

        return await query.FirstOrDefaultAsync(p => p.Id == id, cancellationToken) ?? throw new NotFoundException("Product");
    }

    private async Task EnsureCategoryAsync(Guid categoryId, CancellationToken cancellationToken)
    {
        if (!await db.Categories.AnyAsync(c => c.Id == categoryId, cancellationToken))
        {
            throw RequestValidationException.For("categoryId", "Choose an existing category.");
        }
    }

    private async Task EnsureSlugAvailableAsync(string slug, Guid? productId, CancellationToken cancellationToken)
    {
        if (await db.Products.AnyAsync(p => p.Slug == slug && p.Id != productId, cancellationToken))
        {
            throw RequestValidationException.For("slug", "Another product already uses this URL slug.");
        }
    }

    private static void Apply(Product product, SaveProductRequest request)
    {
        product.CategoryId = request.CategoryId;
        product.Name = request.Name.Trim();
        product.Slug = request.Slug;
        product.ShortDescription = request.ShortDescription?.Trim() ?? string.Empty;
        product.Description = request.Description?.Trim() ?? string.Empty;
        product.BasePrice = Domain.Common.Money.Round(request.BasePrice);
        product.IsActive = request.IsActive;
        product.IsFeatured = request.IsFeatured;
        product.ProductionTimeDays = request.ProductionTimeDays;
        product.Specifications = (request.Specifications ?? []).Select(s => new ProductSpecification(s.Name.Trim(), s.Value.Trim())).ToList();
    }

    private static AdminProductDto ToDto(Product p) => new(
        p.Id,
        p.CategoryId,
        p.Name,
        p.Slug,
        p.ShortDescription,
        p.Description,
        p.BasePrice,
        p.IsActive,
        p.IsFeatured,
        p.ProductionTimeDays,
        p.Specifications.Select(s => new SpecificationItem(s.Name, s.Value)).ToList(),
        p.Variants.OrderBy(v => v.SortOrder).Select(v => new AdminVariantDto(v.Id, v.Sku, v.ColorName, v.ColorHex, v.Size, v.PriceAdjustment, v.IsActive, v.SortOrder)).ToList(),
        p.Images.OrderBy(i => i.SortOrder).Select(i => new AdminImageDto(i.Id, i.Url, i.AltText, i.SortOrder, i.StorageKey is not null)).ToList(),
        p.Customization is { } c
            ? new AdminCustomizationDto(c.IsEnabled, c.Kind.ToString(), c.ModelUrl, c.BaseFee, c.AdditionalPrintAreaFee, c.MaxElementsPerArea, c.MinRecommendedDpi,
                c.PrintAreas.OrderBy(a => a.SortOrder).Select(a => new AdminPrintAreaDto(a.Key, a.Name, a.WidthMm, a.HeightMm, a.Shape.ToString())).ToList())
            : null,
        p.CreatedAt,
        p.UpdatedAt);
}
