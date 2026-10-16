using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SublimaLab.Application.Abstractions;

namespace SublimaLab.Application.Uploads;

public sealed record RetentionResult(int UploadsDeleted, int CartsDeleted);

/// <summary>
/// Housekeeping that bounds storage growth (and abuse): removes guest carts idle for a month and
/// uploads that were never used by a saved design, cart or order. Production artwork attached to
/// an order is retained forever.
/// </summary>
public sealed class RetentionService(IAppDbContext db, IFileStorage storage, TimeProvider clock, ILogger<RetentionService> logger)
{
    public static readonly TimeSpan UploadGracePeriod = TimeSpan.FromDays(30);
    public static readonly TimeSpan GuestCartLifetime = TimeSpan.FromDays(30);

    public async Task<RetentionResult> RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;

        var staleCarts = await db.Carts.Where(c => c.UserId == null && c.UpdatedAt < now - GuestCartLifetime).ToListAsync(cancellationToken);
        db.Carts.RemoveRange(staleCarts);
        await db.SaveChangesAsync(cancellationToken);

        var candidates = await db.Uploads.Where(u => !u.IsRetained && u.CreatedAt < now - UploadGracePeriod).ToListAsync(cancellationToken);
        if (candidates.Count == 0)
        {
            return new RetentionResult(0, staleCarts.Count);
        }

        // Design documents are JSON: collect every referenced upload id in memory.
        var referenced = new HashSet<Guid>();
        foreach (var design in await db.Designs.AsNoTracking().Select(d => new { d.Document, d.PreviewUploadId }).ToListAsync(cancellationToken))
        {
            referenced.UnionWith(design.Document.ReferencedUploadIds());
            if (design.PreviewUploadId is { } preview) referenced.Add(preview);
        }

        foreach (var item in await db.Carts.AsNoTracking().SelectMany(c => c.Items).Select(i => new { i.Design, i.PreviewUploadId }).ToListAsync(cancellationToken))
        {
            if (item.Design is not null) referenced.UnionWith(item.Design.ReferencedUploadIds());
            if (item.PreviewUploadId is { } preview) referenced.Add(preview);
        }

        var unused = candidates.Where(u => !referenced.Contains(u.Id)).ToList();
        db.Uploads.RemoveRange(unused);
        await db.SaveChangesAsync(cancellationToken);

        foreach (var upload in unused)
        {
            await storage.DeleteAsync(upload.StorageKey, cancellationToken);
        }

        if (unused.Count > 0 || staleCarts.Count > 0)
        {
            logger.LogInformation("Retention removed {Uploads} unused uploads and {Carts} stale guest carts", unused.Count, staleCarts.Count);
        }

        return new RetentionResult(unused.Count, staleCarts.Count);
    }
}
