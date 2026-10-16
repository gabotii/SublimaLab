using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SublimaLab.Application.Abstractions;
using SublimaLab.Application.Common;
using SublimaLab.Application.Orders;
using SublimaLab.Domain.Ordering;

namespace SublimaLab.Application.Admin;

public sealed class AdminDashboardService(IAppDbContext db, IUserDirectory users, AdminOrderService orders, IOptions<CommerceOptions> commerce, TimeProvider clock)
{
    private static readonly OrderStatus[] PaidStatuses =
        [OrderStatus.Paid, OrderStatus.InProduction, OrderStatus.ReadyToShip, OrderStatus.Shipped, OrderStatus.Delivered];

    public async Task<DashboardDto> GetAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var since = today.AddDays(-29).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        // Totals are aggregated in memory from narrow projections: portable across SQLite and
        // PostgreSQL decimal handling, and cheap at this scale. Move to SQL views when volume grows.
        var paid = await db.Orders.AsNoTracking()
            .Where(o => PaidStatuses.Contains(o.Status))
            .Select(o => new { o.Total, o.PlacedAt })
            .ToListAsync(cancellationToken);

        var statusCounts = await db.Orders.AsNoTracking()
            .GroupBy(o => o.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, cancellationToken);

        var recentPaid = paid.Where(o => o.PlacedAt >= since).ToList();
        var sales = Enumerable.Range(0, 30)
            .Select(i => today.AddDays(i - 29))
            .Select(day =>
            {
                var onDay = recentPaid.Where(o => DateOnly.FromDateTime(o.PlacedAt) == day).ToList();
                return new DailySalesDto(day, onDay.Count, onDay.Sum(o => o.Total));
            })
            .ToList();

        var soldItems = await db.Orders.AsNoTracking()
            .Where(o => PaidStatuses.Contains(o.Status))
            .SelectMany(o => o.Items)
            .Select(i => new { i.ProductId, i.ProductName, i.Quantity, i.LineTotal })
            .ToListAsync(cancellationToken);

        var topProducts = soldItems
            .GroupBy(i => i.ProductId)
            .Select(g => new TopProductDto(g.Key, g.First().ProductName, g.Sum(i => i.Quantity), g.Sum(i => i.LineTotal)))
            .OrderByDescending(p => p.Quantity)
            .Take(5)
            .ToList();

        var recent = await orders.ListAsync(new AdminOrderQuery(null, null, 1, 6), cancellationToken);

        int Count(params OrderStatus[] statuses) => statuses.Sum(s => statusCounts.GetValueOrDefault(s));

        return new DashboardDto(
            paid.Sum(o => o.Total),
            recentPaid.Sum(o => o.Total),
            statusCounts.Values.Sum(),
            recentPaid.Count,
            Count(OrderStatus.Pending, OrderStatus.PaymentPending),
            Count(OrderStatus.Paid),
            Count(OrderStatus.InProduction),
            Count(OrderStatus.ReadyToShip),
            await users.CountCustomersAsync(cancellationToken),
            await db.Products.CountAsync(p => p.IsActive, cancellationToken),
            commerce.Value.Currency,
            sales,
            topProducts,
            recent.Items);
    }
}
