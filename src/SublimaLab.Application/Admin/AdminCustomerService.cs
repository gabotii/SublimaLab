using Microsoft.EntityFrameworkCore;
using SublimaLab.Application.Abstractions;
using SublimaLab.Application.Accounts;
using SublimaLab.Application.Common;
using SublimaLab.Application.Orders;
using SublimaLab.Domain.Ordering;

namespace SublimaLab.Application.Admin;

public sealed class AdminCustomerService(IAppDbContext db, IUserDirectory users, ICurrentUser currentUser)
{
    private static readonly OrderStatus[] PaidStatuses =
        [OrderStatus.Paid, OrderStatus.InProduction, OrderStatus.ReadyToShip, OrderStatus.Shipped, OrderStatus.Delivered];

    public async Task<PagedResult<AdminCustomerSummaryDto>> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var (found, total) = await users.SearchAsync(search, page, pageSize, cancellationToken);
        var ids = found.Select(u => u.Id).ToList();

        var stats = (await db.Orders.AsNoTracking()
                .Where(o => ids.Contains(o.UserId))
                .Select(o => new { o.UserId, o.Total, o.Status })
                .ToListAsync(cancellationToken))
            .GroupBy(o => o.UserId)
            .ToDictionary(g => g.Key, g => (Count: g.Count(), Spent: g.Where(o => PaidStatuses.Contains(o.Status)).Sum(o => o.Total)));

        var items = found.Select(u =>
        {
            var s = stats.GetValueOrDefault(u.Id);
            return new AdminCustomerSummaryDto(u.Id, u.Email, $"{u.FirstName} {u.LastName}".Trim(), u.EmailConfirmed, u.IsLockedOut, u.Roles.Contains(Roles.Admin), u.CreatedAt, s.Count, s.Spent);
        }).ToList();

        return new PagedResult<AdminCustomerSummaryDto>(items, page, pageSize, total);
    }

    public async Task<AdminCustomerDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await users.FindAsync(id, cancellationToken) ?? throw new NotFoundException("Customer");

        var orders = await db.Orders.AsNoTracking()
            .Where(o => o.UserId == id)
            .OrderByDescending(o => o.PlacedAt)
            .Select(o => new OrderSummaryDto(o.Id, o.OrderNumber, o.Status, o.PlacedAt, o.Items.Sum(i => i.Quantity), o.Total, o.Currency, o.Items.OrderBy(i => i.Id).Select(i => i.ImageUrl).FirstOrDefault()))
            .ToListAsync(cancellationToken);

        var addresses = await db.CustomerAddresses.AsNoTracking()
            .Where(a => a.UserId == id)
            .Select(a => new CustomerAddressDto(a.Id, a.Label, a.RecipientName, a.Phone, a.Line1, a.Line2, a.City, a.Region, a.PostalCode, a.Country, a.IsDefault))
            .ToListAsync(cancellationToken);

        return new AdminCustomerDto(
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            user.PhoneNumber,
            user.EmailConfirmed,
            user.IsLockedOut,
            user.Roles,
            user.CreatedAt,
            orders.Count,
            orders.Where(o => PaidStatuses.Contains(o.Status)).Sum(o => o.Total),
            orders,
            addresses);
    }

    public async Task<AdminCustomerDto> SetLockedAsync(Guid id, bool locked, CancellationToken cancellationToken)
    {
        if (locked && id == currentUser.UserId)
        {
            throw new BusinessRuleException("CANNOT_LOCK_SELF", "You can't lock your own account.");
        }

        var user = await users.FindAsync(id, cancellationToken) ?? throw new NotFoundException("Customer");
        if (locked && user.Roles.Contains(Roles.Admin))
        {
            throw new BusinessRuleException("CANNOT_LOCK_ADMIN", "Administrator accounts can't be locked from the dashboard.");
        }

        await users.SetLockedAsync(id, locked, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }
}
