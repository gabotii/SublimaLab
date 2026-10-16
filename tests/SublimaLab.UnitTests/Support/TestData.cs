using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SublimaLab.Application.Abstractions;
using SublimaLab.Domain.Catalog;
using SublimaLab.Domain.Customization;
using SublimaLab.Domain.Designs;
using SublimaLab.Infrastructure.Persistence;

namespace SublimaLab.Tests.Support;

public static class TestData
{
    public static Product CustomizableTee(decimal basePrice = 20m, decimal baseFee = 6m, decimal extraAreaFee = 4m)
    {
        var category = new ProductCategory { Name = "T-Shirts", Slug = "t-shirts" };
        var product = new Product { Name = "Test Tee", Slug = $"test-tee-{Guid.NewGuid():N}", BasePrice = basePrice, Category = category };
        product.Variants.Add(new ProductVariant { Sku = $"SKU-{Guid.NewGuid():N}", ColorName = "White", ColorHex = "#FFFFFF", Size = "M" });
        product.Variants.Add(new ProductVariant { Sku = $"SKU-{Guid.NewGuid():N}", ColorName = "White", ColorHex = "#FFFFFF", Size = "XXL", PriceAdjustment = 2m });
        product.Customization = new CustomizationConfiguration
        {
            Kind = CustomizerKind.Apparel,
            BaseFee = baseFee,
            AdditionalPrintAreaFee = extraAreaFee,
            MaxElementsPerArea = 3,
            MinRecommendedDpi = 150,
        };
        product.Customization.PrintAreas.Add(new PrintArea { Key = "front", Name = "Front", WidthMm = 300, HeightMm = 400 });
        product.Customization.PrintAreas.Add(new PrintArea { Key = "back", Name = "Back", WidthMm = 300, HeightMm = 400, SortOrder = 1 });
        return product;
    }

    public static TextElement Text(string id = "t1", decimal x = 150, decimal y = 100, decimal width = 100, decimal height = 30, decimal rotation = 0) => new()
    {
        Id = id,
        Text = "SublimaLab",
        FontFamily = "Inter",
        FontSize = 32,
        Color = "#111111",
        X = x,
        Y = y,
        Width = width,
        Height = height,
        Rotation = rotation,
    };

    public static ImageElement Image(Guid uploadId, string id = "i1", decimal x = 150, decimal y = 200, decimal width = 200, decimal height = 250) => new()
    {
        Id = id,
        UploadId = uploadId,
        X = x,
        Y = y,
        Width = width,
        Height = height,
    };

    public static DesignDocument Design(params (string Area, DesignElement[] Elements)[] surfaces) => new()
    {
        Surfaces = surfaces.Select(s => new DesignSurface { PrintArea = s.Area, Elements = [.. s.Elements] }).ToList(),
    };
}

/// <summary>A real SQLite in-memory database with the production model, for service tests.</summary>
public sealed class SqliteTestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteTestDatabase()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        Context = CreateContext();
        Context.Database.EnsureCreated();
    }

    public SqliteAppDbContext Context { get; }

    public SqliteAppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(_connection).Options);

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}

public sealed class FakeCurrentUser(Guid? userId = null, bool isAdmin = false) : ICurrentUser
{
    public Guid? UserId { get; set; } = userId;
    public bool IsAuthenticated => UserId is not null;
    public bool IsAdmin { get; set; } = isAdmin;
}

public sealed class FakeClientSession(string? sessionId = null) : IClientSession
{
    public string? SessionId { get; private set; } = sessionId;

    public string GetOrCreateSessionId() => SessionId ??= Guid.NewGuid().ToString("N");
}

public sealed class InMemoryFileStorage : IFileStorage
{
    public Dictionary<string, byte[]> Files { get; } = [];

    public async Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        var key = $"2026/10/{Guid.NewGuid():N}{extension}";
        Files[key] = buffer.ToArray();
        return key;
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken) =>
        Task.FromResult<Stream?>(Files.TryGetValue(key, out var bytes) ? new MemoryStream(bytes) : null);

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        Files.Remove(key);
        return Task.CompletedTask;
    }
}
