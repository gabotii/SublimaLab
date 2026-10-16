using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SublimaLab.Application.Abstractions;
using SublimaLab.Application.Common;
using SublimaLab.Domain.Catalog;
using SublimaLab.Domain.Designs;
using SublimaLab.Domain.Uploads;

namespace SublimaLab.Application.Customization;

public sealed record DesignWarning(string Code, string Message, string? ElementId, int? EffectiveDpi);

public sealed record DesignValidationResult(IReadOnlyDictionary<string, string[]> Errors, IReadOnlyList<DesignWarning> Warnings)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// The product-agnostic customization engine: validates any design document against the
/// product's <see cref="Domain.Customization.CustomizationConfiguration"/> — print areas,
/// placement bounds, element limits, artwork ownership and print resolution.
/// Mugs, keychains and future products reuse it unchanged.
/// </summary>
public sealed class DesignValidationService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IClientSession session,
    IValidator<DesignDocument> documentValidator)
{
    /// <param name="product">Product with Customization and PrintAreas loaded.</param>
    /// <param name="fieldPrefix">JSON path prefix for error keys, e.g. "design".</param>
    public async Task<DesignValidationResult> ValidateAsync(Product product, DesignDocument? document, string fieldPrefix, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, List<string>>();
        var warnings = new List<DesignWarning>();

        void AddError(string path, string message)
        {
            var key = string.IsNullOrEmpty(path) ? fieldPrefix : $"{fieldPrefix}.{path}";
            if (!errors.TryGetValue(key, out var list))
            {
                errors[key] = list = [];
            }

            list.Add(message);
        }

        if (document is null)
        {
            AddError(string.Empty, "A design is required.");
            return Result();
        }

        var configuration = product.Customization;
        if (configuration is not { IsEnabled: true })
        {
            AddError(string.Empty, "This product cannot be customized.");
            return Result();
        }

        var structural = await documentValidator.ValidateAsync(document, cancellationToken);
        if (!structural.IsValid)
        {
            foreach (var (key, messages) in structural.ToCamelCaseDictionary(fieldPrefix))
            {
                errors[key] = [.. messages];
            }

            return Result();
        }

        var uploads = await LoadUploadsAsync(document, cancellationToken);

        for (var s = 0; s < document.Surfaces.Count; s++)
        {
            var surface = document.Surfaces[s];
            var surfacePath = $"surfaces[{s}]";
            var area = configuration.PrintAreas.FirstOrDefault(a => string.Equals(a.Key, surface.PrintArea, StringComparison.OrdinalIgnoreCase));

            if (area is null)
            {
                AddError($"{surfacePath}.printArea", $"'{surface.PrintArea}' is not a print area of this product.");
                continue;
            }

            if (surface.Elements.Count > configuration.MaxElementsPerArea)
            {
                AddError($"{surfacePath}.elements", $"A print area can contain at most {configuration.MaxElementsPerArea} elements.");
            }

            for (var e = 0; e < surface.Elements.Count; e++)
            {
                var element = surface.Elements[e];
                var elementPath = $"{surfacePath}.elements[{e}]";

                var (left, top, right, bottom) = element.GetRotatedBounds();
                var tolerance = DesignRules.BoundsToleranceMm;
                if (left < -tolerance || top < -tolerance || right > area.WidthMm + tolerance || bottom > area.HeightMm + tolerance)
                {
                    AddError(elementPath, $"This element extends outside the printable area ({area.Name}).");
                }

                if (element is not ImageElement image)
                {
                    continue;
                }

                if (!uploads.TryGetValue(image.UploadId, out var upload))
                {
                    AddError($"{elementPath}.uploadId", "The referenced image does not exist or is not accessible.");
                    continue;
                }

                var dpi = EffectiveDpi(upload, image);
                if (dpi < configuration.MinRecommendedDpi)
                {
                    warnings.Add(new DesignWarning("LOW_RESOLUTION", DesignRules.LowResolutionMessage, image.Id, dpi));
                }
            }
        }

        return Result();

        DesignValidationResult Result() => new(errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray()), warnings);
    }

    public async Task<DesignValidationResult> EnsureValidAsync(Product product, DesignDocument? document, string fieldPrefix, CancellationToken cancellationToken)
    {
        var result = await ValidateAsync(product, document, fieldPrefix, cancellationToken);
        return result.IsValid
            ? result
            : throw new RequestValidationException(result.Errors, "The design is not valid for this product.");
    }

    /// <summary>Effective print resolution of an image at its placed physical size.</summary>
    internal static int EffectiveDpi(ArtworkUpload upload, ImageElement element)
    {
        var horizontal = upload.WidthPx / (double)(element.Width / 25.4m);
        var vertical = upload.HeightPx / (double)(element.Height / 25.4m);
        return (int)Math.Floor(Math.Min(horizontal, vertical));
    }

    private async Task<Dictionary<Guid, ArtworkUpload>> LoadUploadsAsync(DesignDocument document, CancellationToken cancellationToken)
    {
        var ids = document.ReferencedUploadIds();
        if (ids.Count == 0)
        {
            return [];
        }

        var userId = currentUser.UserId;
        var sessionId = session.SessionId;

        var uploads = await db.Uploads
            .AsNoTracking()
            .Where(u => ids.Contains(u.Id) && u.Kind == UploadKind.Artwork)
            .ToListAsync(cancellationToken);

        return uploads
            .Where(u => u.IsAccessibleBy(userId, sessionId))
            .ToDictionary(u => u.Id);
    }
}
