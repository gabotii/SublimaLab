using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SublimaLab.Application.Abstractions;
using SublimaLab.Application.Common;
using SublimaLab.Domain.Ordering;

namespace SublimaLab.Application.Admin;

/// <summary>
/// Packages everything production needs for an order into a ZIP: per customized item, the exact
/// design document, the frozen production spec (print-area sizes, minimum DPI), the original
/// artwork files and the customer preview. A future rendering service can turn these into
/// print-ready PNG/PDF files without depending on the storefront.
/// </summary>
public sealed class ProductionArtworkService(IAppDbContext db, IFileStorage storage, ILogger<ProductionArtworkService> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<(byte[] Zip, string FileName)> BuildAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await db.Orders.AsNoTracking().Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new NotFoundException("Order");

        var customized = order.Items.Where(i => i.Design is not null).OrderBy(i => i.Id).ToList();
        if (customized.Count == 0)
        {
            throw new BusinessRuleException("NO_CUSTOMIZED_ITEMS", "This order has no customized items.");
        }

        var uploadIds = customized
            .SelectMany(i => i.Design!.ReferencedUploadIds().Concat(i.PreviewUploadId is { } p ? [p] : []))
            .Distinct()
            .ToList();
        var uploads = await db.Uploads.AsNoTracking().Where(u => uploadIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, cancellationToken);

        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            await WriteTextAsync(zip, "README.txt", Readme(order, customized.Count));
            await WriteTextAsync(zip, "order.json", JsonSerializer.Serialize(new
            {
                order.OrderNumber,
                order.PlacedAt,
                order.Status,
                Customer = order.Contact,
                order.ShippingAddress,
                Items = order.Items.Select(i => new { i.Sku, i.ProductName, i.VariantLabel, i.Quantity, Customized = i.Design is not null }),
            }, Json));

            var index = 0;
            foreach (var item in customized)
            {
                var folder = $"item-{++index:00}-{Safe(item.Sku)}";
                await WriteTextAsync(zip, $"{folder}/design.json", JsonSerializer.Serialize(item.Design, Json));
                await WriteTextAsync(zip, $"{folder}/production-spec.json", JsonSerializer.Serialize(new
                {
                    item.ProductName,
                    item.Sku,
                    item.VariantLabel,
                    item.ColorHex,
                    item.Quantity,
                    item.ProductionSpec,
                }, Json));

                foreach (var uploadId in item.Design!.ReferencedUploadIds())
                {
                    if (uploads.TryGetValue(uploadId, out var upload))
                    {
                        await CopyFileAsync(zip, $"{folder}/artwork/{uploadId}{Path.GetExtension(upload.StorageKey)}", upload.StorageKey, cancellationToken);
                    }
                }

                if (item.PreviewUploadId is { } previewId && uploads.TryGetValue(previewId, out var preview))
                {
                    await CopyFileAsync(zip, $"{folder}/preview{Path.GetExtension(preview.StorageKey)}", preview.StorageKey, cancellationToken);
                }
            }
        }

        logger.LogInformation("Built production package for order {OrderNumber} ({Items} customized items)", order.OrderNumber, customized.Count);
        return (buffer.ToArray(), $"{order.OrderNumber}-production.zip");
    }

    private async Task CopyFileAsync(ZipArchive zip, string entryName, string storageKey, CancellationToken cancellationToken)
    {
        await using var source = await storage.OpenReadAsync(storageKey, cancellationToken);
        if (source is null)
        {
            logger.LogWarning("Artwork file {StorageKey} is missing from storage", storageKey);
            return;
        }

        var entry = zip.CreateEntry(entryName, CompressionLevel.NoCompression); // Images are already compressed.
        await using var target = await entry.OpenAsync(cancellationToken);
        await source.CopyToAsync(target, cancellationToken);
    }

    private static async Task WriteTextAsync(ZipArchive zip, string entryName, string content)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var stream = await entry.OpenAsync();
        await stream.WriteAsync(Encoding.UTF8.GetBytes(content));
    }

    private static string Safe(string value) => new(value.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_').ToArray());

    private static string Readme(Order order, int customizedItems) => $"""
        SublimaLab production package — order {order.OrderNumber}
        Placed: {order.PlacedAt:yyyy-MM-dd HH:mm} UTC · Customized items: {customizedItems}

        Each item-XX folder contains:
          design.json            The exact design the customer ordered (schema v1).
                                 Coordinates are millimetres from the print area's top-left
                                 corner; x/y is the element centre; rotation in degrees (clockwise).
          production-spec.json   Print-area sizes (mm) and minimum recommended DPI, frozen at order time.
          artwork/               Original customer uploads referenced by design.json (by uploadId).
          preview.png            The customer's preview image.

        Render at the required DPI: pixels = millimetres / 25.4 * DPI.
        """;
}
