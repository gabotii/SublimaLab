using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SublimaLab.Application.Abstractions;
using SublimaLab.Domain.Carts;

namespace SublimaLab.Application.Accounts;

/// <summary>
/// After sign-in, moves everything a guest created in this browser (cart lines, uploaded
/// artwork) into their account so no work is lost when they log in at checkout.
/// </summary>
public sealed class GuestSessionMerger(IAppDbContext db, ILogger<GuestSessionMerger> logger)
{
    public async Task MergeAsync(Guid userId, string? sessionId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return;
        }

        var uploads = await db.Uploads.Where(u => u.SessionId == sessionId && u.OwnerUserId == null).ToListAsync(cancellationToken);
        uploads.ForEach(u => u.ClaimFor(userId));

        var guestCart = await db.Carts.Include(c => c.Items).FirstOrDefaultAsync(c => c.SessionId == sessionId, cancellationToken);
        if (guestCart is { Items.Count: > 0 })
        {
            var userCart = await db.Carts.Include(c => c.Items).FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);
            if (userCart is null)
            {
                userCart = Cart.ForUser(userId);
                db.Carts.Add(userCart);
            }

            userCart.MergeFrom(guestCart);
        }

        if (guestCart is not null)
        {
            db.Carts.Remove(guestCart);
        }

        await db.SaveChangesAsync(cancellationToken);

        if (uploads.Count > 0 || guestCart is not null)
        {
            logger.LogInformation("Merged guest session into user {UserId}: {UploadCount} uploads claimed", userId, uploads.Count);
        }
    }
}
