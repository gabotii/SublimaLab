using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SublimaLab.Application.Abstractions;
using SublimaLab.Application.Common;

namespace SublimaLab.Application.Shipping;

public sealed record ShippingMethodDto(
    string Code,
    string Name,
    string Description,
    decimal Cost,
    decimal? FreeShippingThreshold,
    int MinDeliveryDays,
    int MaxDeliveryDays,
    string Currency);

public sealed class ShippingService(IAppDbContext db, IOptions<CommerceOptions> commerce)
{
    public async Task<IReadOnlyList<ShippingMethodDto>> GetMethodsAsync(CancellationToken cancellationToken)
    {
        var currency = commerce.Value.Currency;
        return await db.ShippingMethods
            .AsNoTracking()
            .Where(m => m.IsActive)
            .OrderBy(m => m.SortOrder)
            .Select(m => new ShippingMethodDto(m.Code, m.Name, m.Description, m.Cost, m.FreeShippingThreshold, m.MinDeliveryDays, m.MaxDeliveryDays, currency))
            .ToListAsync(cancellationToken);
    }
}
