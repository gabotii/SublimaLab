using FluentValidation;

namespace SublimaLab.Application.Catalog;

public sealed class ProductQueryValidator : AbstractValidator<ProductQuery>
{
    public const int MaxPageSize = 48;

    public ProductQueryValidator()
    {
        RuleFor(q => q.Search).MaximumLength(100);
        RuleFor(q => q.Category).MaximumLength(80);
        RuleFor(q => q.MinPrice).GreaterThanOrEqualTo(0).When(q => q.MinPrice.HasValue);
        RuleFor(q => q.MaxPrice).GreaterThanOrEqualTo(0).When(q => q.MaxPrice.HasValue);
        RuleFor(q => q.MaxPrice)
            .GreaterThanOrEqualTo(q => q.MinPrice)
            .When(q => q.MinPrice.HasValue && q.MaxPrice.HasValue)
            .WithMessage("Maximum price must be greater than or equal to the minimum price.");
        RuleFor(q => q.Sort).IsInEnum();
        RuleFor(q => q.Page).InclusiveBetween(1, 10_000);
        RuleFor(q => q.PageSize).InclusiveBetween(1, MaxPageSize);
    }
}
