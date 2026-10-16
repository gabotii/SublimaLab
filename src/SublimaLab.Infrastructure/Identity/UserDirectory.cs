using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SublimaLab.Application.Abstractions;
using SublimaLab.Application.Common;

namespace SublimaLab.Infrastructure.Identity;

internal sealed class UserDirectory(UserManager<ApplicationUser> users, TimeProvider clock) : IUserDirectory
{
    public async Task<(IReadOnlyList<DirectoryUser> Users, int Total)> SearchAsync(string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = users.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToUpperInvariant();
            query = query.Where(u =>
                u.NormalizedEmail!.Contains(term) ||
                u.FirstName.ToUpper().Contains(term) ||
                u.LastName.ToUpper().Contains(term));
        }

        var total = await query.CountAsync(cancellationToken);
        var pageOfUsers = await query
            .OrderByDescending(u => u.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var result = new List<DirectoryUser>(pageOfUsers.Count);
        foreach (var user in pageOfUsers)
        {
            result.Add(await ToDirectoryUserAsync(user));
        }

        return (result, total);
    }

    public async Task<DirectoryUser?> FindAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        return user is null ? null : await ToDirectoryUserAsync(user);
    }

    public async Task<int> CountCustomersAsync(CancellationToken cancellationToken) =>
        (await users.GetUsersInRoleAsync(Roles.Customer)).Count;

    public async Task SetLockedAsync(Guid userId, bool locked, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new NotFoundException("Customer");
        await users.SetLockoutEnabledAsync(user, true);
        await users.SetLockoutEndDateAsync(user, locked ? DateTimeOffset.MaxValue : null);
        if (locked)
        {
            // Invalidates existing authentication cookies at their next validation.
            await users.UpdateSecurityStampAsync(user);
        }
        else
        {
            await users.ResetAccessFailedCountAsync(user);
        }
    }

    private async Task<DirectoryUser> ToDirectoryUserAsync(ApplicationUser user) => new(
        user.Id,
        user.Email ?? string.Empty,
        user.FirstName,
        user.LastName,
        user.PhoneNumber,
        user.EmailConfirmed,
        user.LockoutEnd is { } end && end > clock.GetUtcNow(),
        user.CreatedAt,
        [.. await users.GetRolesAsync(user)]);
}
