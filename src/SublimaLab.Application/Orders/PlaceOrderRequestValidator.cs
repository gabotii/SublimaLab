using FluentValidation;

namespace SublimaLab.Application.Orders;

public sealed class PlaceOrderRequestValidator : AbstractValidator<PlaceOrderRequest>
{
    public PlaceOrderRequestValidator()
    {
        RuleFor(r => r.IdempotencyKey).NotEmpty();
        RuleFor(r => r.ShippingMethodCode).NotEmpty().MaximumLength(40);
        RuleFor(r => r.Contact).NotNull().SetValidator(new ContactValidator());
        RuleFor(r => r.ShippingAddress).NotNull().SetValidator(new AddressValidator());
    }
}

public sealed class ContactValidator : AbstractValidator<ContactDto>
{
    public ContactValidator()
    {
        RuleFor(c => c.FullName).NotEmpty().MaximumLength(120);
        RuleFor(c => c.Email).NotEmpty().MaximumLength(256).EmailAddress().WithMessage("A valid email address is required.");
        RuleFor(c => c.Phone).NotEmpty().MaximumLength(30).Matches(@"^\+?[0-9 ()\-.]{6,30}$").WithMessage("A valid phone number is required.");
    }
}

public sealed class AddressValidator : AbstractValidator<AddressDto>
{
    public AddressValidator()
    {
        RuleFor(a => a.Line1).NotEmpty().MaximumLength(200);
        RuleFor(a => a.Line2).MaximumLength(200);
        RuleFor(a => a.City).NotEmpty().MaximumLength(100);
        RuleFor(a => a.Region).NotEmpty().MaximumLength(100);
        RuleFor(a => a.PostalCode).NotEmpty().MaximumLength(20).Matches("^[A-Za-z0-9 -]+$").WithMessage("A valid postal code is required.");
        RuleFor(a => a.Country).NotEmpty().Matches("^[A-Z]{2}$").WithMessage("Country must be a two-letter ISO code (e.g. US).");
    }
}
