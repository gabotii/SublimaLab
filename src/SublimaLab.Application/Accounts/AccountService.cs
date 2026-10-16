using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SublimaLab.Application.Abstractions;
using SublimaLab.Application.Common;
using SublimaLab.Domain.Customers;

namespace SublimaLab.Application.Accounts;

public sealed record SaveAddressRequest(
    string? Label,
    string RecipientName,
    string Phone,
    string Line1,
    string? Line2,
    string City,
    string Region,
    string PostalCode,
    string Country,
    bool IsDefault);

public sealed record CustomerAddressDto(
    Guid Id,
    string Label,
    string RecipientName,
    string Phone,
    string Line1,
    string? Line2,
    string City,
    string Region,
    string PostalCode,
    string Country,
    bool IsDefault);

public sealed class SaveAddressRequestValidator : AbstractValidator<SaveAddressRequest>
{
    public SaveAddressRequestValidator()
    {
        RuleFor(a => a.Label).MaximumLength(40);
        RuleFor(a => a.RecipientName).NotEmpty().MaximumLength(120);
        RuleFor(a => a.Phone).NotEmpty().MaximumLength(30).Matches(@"^\+?[0-9 ()\-.]{6,30}$").WithMessage("A valid phone number is required.");
        RuleFor(a => a.Line1).NotEmpty().MaximumLength(200);
        RuleFor(a => a.Line2).MaximumLength(200);
        RuleFor(a => a.City).NotEmpty().MaximumLength(100);
        RuleFor(a => a.Region).NotEmpty().MaximumLength(100);
        RuleFor(a => a.PostalCode).NotEmpty().MaximumLength(20).Matches("^[A-Za-z0-9 -]+$");
        RuleFor(a => a.Country).NotEmpty().Matches("^[A-Z]{2}$").WithMessage("Country must be a two-letter ISO code (e.g. US).");
    }
}

/// <summary>The signed-in customer's address book.</summary>
public sealed class AccountService(IAppDbContext db, ICurrentUser currentUser, IValidator<SaveAddressRequest> validator)
{
    public const int MaxAddresses = 10;

    public async Task<IReadOnlyList<CustomerAddressDto>> GetAddressesAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var addresses = await db.CustomerAddresses
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.IsDefault)
            .ThenBy(a => a.CreatedAt)
            .ToListAsync(cancellationToken);
        return addresses.Select(ToDto).ToList();
    }

    public async Task<CustomerAddressDto> CreateAddressAsync(SaveAddressRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        await validator.ValidateOrThrowAsync(request, cancellationToken);

        var existing = await db.CustomerAddresses.Where(a => a.UserId == userId).ToListAsync(cancellationToken);
        if (existing.Count >= MaxAddresses)
        {
            throw new BusinessRuleException("ADDRESS_LIMIT_REACHED", $"You can save up to {MaxAddresses} addresses.");
        }

        var address = new CustomerAddress
        {
            UserId = userId,
            RecipientName = request.RecipientName,
            Phone = request.Phone,
            Line1 = request.Line1,
            City = request.City,
            Region = request.Region,
            PostalCode = request.PostalCode,
            Country = request.Country,
        };
        Apply(address, request);
        SetDefault(existing, address, request.IsDefault || existing.Count == 0);

        db.CustomerAddresses.Add(address);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(address);
    }

    public async Task<CustomerAddressDto> UpdateAddressAsync(Guid id, SaveAddressRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        await validator.ValidateOrThrowAsync(request, cancellationToken);

        var addresses = await db.CustomerAddresses.Where(a => a.UserId == userId).ToListAsync(cancellationToken);
        var address = addresses.FirstOrDefault(a => a.Id == id) ?? throw new NotFoundException("Address");

        Apply(address, request);
        if (request.IsDefault)
        {
            SetDefault(addresses.Where(a => a.Id != id).ToList(), address, true);
        }

        await db.SaveChangesAsync(cancellationToken);
        return ToDto(address);
    }

    public async Task DeleteAddressAsync(Guid id, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var addresses = await db.CustomerAddresses.Where(a => a.UserId == userId).ToListAsync(cancellationToken);
        var address = addresses.FirstOrDefault(a => a.Id == id) ?? throw new NotFoundException("Address");

        db.CustomerAddresses.Remove(address);
        if (address.IsDefault && addresses.FirstOrDefault(a => a.Id != id) is { } next)
        {
            next.IsDefault = true;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static void Apply(CustomerAddress address, SaveAddressRequest request)
    {
        address.Label = string.IsNullOrWhiteSpace(request.Label) ? "Home" : request.Label.Trim();
        address.RecipientName = request.RecipientName.Trim();
        address.Phone = request.Phone.Trim();
        address.Line1 = request.Line1.Trim();
        address.Line2 = string.IsNullOrWhiteSpace(request.Line2) ? null : request.Line2.Trim();
        address.City = request.City.Trim();
        address.Region = request.Region.Trim();
        address.PostalCode = request.PostalCode.Trim().ToUpperInvariant();
        address.Country = request.Country;
    }

    private static void SetDefault(IEnumerable<CustomerAddress> others, CustomerAddress address, bool isDefault)
    {
        if (!isDefault)
        {
            return;
        }

        foreach (var other in others)
        {
            other.IsDefault = false;
        }

        address.IsDefault = true;
    }

    private static CustomerAddressDto ToDto(CustomerAddress a) =>
        new(a.Id, a.Label, a.RecipientName, a.Phone, a.Line1, a.Line2, a.City, a.Region, a.PostalCode, a.Country, a.IsDefault);
}
