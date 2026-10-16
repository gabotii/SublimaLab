using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SublimaLab.Application.Uploads;
using SublimaLab.Domain.Carts;
using SublimaLab.Domain.Uploads;
using SublimaLab.Tests.Support;

namespace SublimaLab.UnitTests.Application;

public sealed class RetentionServiceTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private readonly SqliteTestDatabase _database = new();
    private readonly InMemoryFileStorage _storage = new();

    [Fact]
    public async Task Removes_only_old_unused_uploads_and_stale_guest_carts()
    {
        var oldUnused = await AddUploadAsync(Now.AddDays(-40));
        var recent = await AddUploadAsync(Now.AddDays(-2));
        var retained = await AddUploadAsync(Now.AddDays(-400), retain: true);
        var inCart = await AddUploadAsync(Now.AddDays(-40));

        var staleCart = Cart.ForSession("stale-session");
        staleCart.UpdatedAt = Now.AddDays(-45);
        var activeCart = Cart.ForSession("active-session");
        activeCart.UpdatedAt = Now.AddDays(-1);
        activeCart.AddItem(Guid.NewGuid(), Guid.NewGuid(), 1, TestData.Design(("front", [TestData.Image(inCart)])), null, null);
        _database.Context.Carts.AddRange(staleCart, activeCart);
        await _database.Context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
        await _database.Context.SaveChangesAsync();

        var result = await new RetentionService(_database.Context, _storage, new FixedClock(Now), NullLogger<RetentionService>.Instance).RunAsync(CancellationToken.None);

        Assert.Equal(new RetentionResult(1, 1), result);
        var remaining = await _database.Context.Uploads.Select(u => u.Id).ToListAsync();
        Assert.DoesNotContain(oldUnused, remaining);
        Assert.Contains(recent, remaining);
        Assert.Contains(retained, remaining);
        Assert.Contains(inCart, remaining);
        Assert.False(await _database.Context.Carts.AnyAsync(c => c.SessionId == "stale-session"));
        Assert.Equal(3, _storage.Files.Count);
    }

    public void Dispose() => _database.Dispose();

    private async Task<Guid> AddUploadAsync(DateTime createdAt, bool retain = false)
    {
        var key = await _storage.SaveAsync(new MemoryStream([1, 2, 3]), ".png", CancellationToken.None);
        var upload = new ArtworkUpload(null, "guest", UploadKind.Artwork, key, "a.png", "image/png", 3, 100, 100, new string('a', 64), createdAt);
        if (retain)
        {
            upload.Retain();
        }

        _database.Context.Uploads.Add(upload);
        await _database.Context.SaveChangesAsync();
        return upload.Id;
    }

    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }
}
