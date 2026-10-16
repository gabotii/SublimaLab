using FluentValidation;
using SublimaLab.Domain.Designs;

namespace SublimaLab.Application.Customization;

/// <summary>
/// Structural validation of a design document, independent of any product.
/// Product-specific rules (print areas, bounds, uploads) live in <see cref="DesignValidationService"/>.
/// </summary>
public sealed class DesignDocumentValidator : AbstractValidator<DesignDocument>
{
    public DesignDocumentValidator()
    {
        RuleFor(d => d.SchemaVersion)
            .Equal(DesignDocument.CurrentSchemaVersion)
            .WithMessage($"Unsupported design schema version. Expected {DesignDocument.CurrentSchemaVersion}.");

        RuleFor(d => d.Surfaces)
            .NotNull()
            .Must(s => s.Count <= DesignRules.MaxSurfaces).WithMessage($"A design can have at most {DesignRules.MaxSurfaces} surfaces.")
            .Must(s => s.Select(x => x.PrintArea).Distinct(StringComparer.OrdinalIgnoreCase).Count() == s.Count)
            .WithMessage("Each print area can appear only once.");

        RuleFor(d => d)
            .Must(d => d.AllElements().Select(e => e.Id).Distinct(StringComparer.Ordinal).Count() == d.AllElements().Count())
            .WithName("surfaces")
            .WithMessage("Element ids must be unique within a design.")
            .When(d => d.Surfaces is not null);

        RuleForEach(d => d.Surfaces).SetValidator(new SurfaceValidator());
    }

    private sealed class SurfaceValidator : AbstractValidator<DesignSurface>
    {
        public SurfaceValidator()
        {
            RuleFor(s => s.PrintArea).NotEmpty().MaximumLength(40).Matches("^[a-z0-9-]+$");
            RuleFor(s => s.Elements).NotNull();
            RuleForEach(s => s.Elements)
                .NotNull()
                .SetInheritanceValidator(v =>
                {
                    v.Add(new ImageElementValidator());
                    v.Add(new TextElementValidator());
                });
        }
    }

    private abstract class ElementValidator<T> : AbstractValidator<T>
        where T : DesignElement
    {
        protected ElementValidator()
        {
            RuleFor(e => e.Id).NotEmpty().MaximumLength(64).Matches("^[A-Za-z0-9_-]+$");
            RuleFor(e => e.Width).InclusiveBetween(DesignRules.MinElementSizeMm, DesignRules.MaxElementSizeMm);
            RuleFor(e => e.Height).InclusiveBetween(DesignRules.MinElementSizeMm, DesignRules.MaxElementSizeMm);
            RuleFor(e => e.X).InclusiveBetween(-DesignRules.MaxElementSizeMm, DesignRules.MaxElementSizeMm);
            RuleFor(e => e.Y).InclusiveBetween(-DesignRules.MaxElementSizeMm, DesignRules.MaxElementSizeMm);
            RuleFor(e => e.Rotation).InclusiveBetween(-360, 360);
        }
    }

    private sealed class ImageElementValidator : ElementValidator<ImageElement>
    {
        public ImageElementValidator()
        {
            RuleFor(e => e.UploadId).NotEmpty();
        }
    }

    private sealed class TextElementValidator : ElementValidator<TextElement>
    {
        public TextElementValidator()
        {
            RuleFor(e => e.Text)
                .NotEmpty()
                .MaximumLength(DesignRules.MaxTextLength)
                .Must(t => t is null || !t.Any(c => char.IsControl(c) && c != '\n'))
                .WithMessage("Text contains unsupported characters.");
            RuleFor(e => e.FontFamily)
                .Must(f => DesignRules.AllowedFonts.Contains(f))
                .WithMessage("Font family is not supported.");
            RuleFor(e => e.FontSize).InclusiveBetween(DesignRules.MinFontSize, DesignRules.MaxFontSize);
            RuleFor(e => e.Color).NotEmpty().Matches("^#[0-9A-Fa-f]{6}$").WithMessage("Color must be a hex value like #1A2B3C.");
            RuleFor(e => e.LetterSpacing).InclusiveBetween(DesignRules.MinLetterSpacing, DesignRules.MaxLetterSpacing);
            RuleFor(e => e.Align).IsInEnum();
        }
    }
}
