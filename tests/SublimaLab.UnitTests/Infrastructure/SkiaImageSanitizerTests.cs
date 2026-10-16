using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using SublimaLab.Application.Abstractions;
using SublimaLab.Infrastructure.Imaging;
using SublimaLab.Tests.Support;

namespace SublimaLab.UnitTests.Infrastructure;

public sealed class SkiaImageSanitizerTests : IDisposable
{
    private readonly SkiaImageSanitizer _sanitizer = new(NullLogger<SkiaImageSanitizer>.Instance);
    private readonly ImageHeaderInspector _inspector = new();

    [Fact]
    public async Task Png_text_chunks_and_trailing_bytes_are_removed()
    {
        var secret = Encoding.ASCII.GetBytes("GPS 40.4168N 3.7038W");
        var png = TestImages.Png(120, 80);
        byte[] dirty = [.. png[..^12], .. TextChunk(secret), .. png[^12..], .. secret];

        var result = await SanitizeAsync(dirty);

        Assert.NotNull(result);
        Assert.Equal(-1, result.Content.AsSpan().IndexOf(secret));
        AssertDecodes(result, ImageFormat.Png, 120, 80);
    }

    [Fact]
    public async Task Jpeg_exif_is_dropped_and_its_orientation_applied()
    {
        // Orientation 6 means "rotate 90° clockwise to display": a 300×200 file shows as 200×300.
        var jpeg = WithExifOrientation(Encode(Solid(300, 200, SKColors.OrangeRed), SKEncodedImageFormat.Jpeg), orientation: 6);

        var result = await SanitizeAsync(jpeg);

        Assert.NotNull(result);
        Assert.Equal(-1, result.Content.AsSpan().IndexOf("Exif"u8));
        AssertDecodes(result, ImageFormat.Jpeg, 200, 300);
    }

    [Fact]
    public async Task Rotation_moves_pixels_to_the_upright_position()
    {
        // Mark the top-left corner; after a 90° clockwise turn it must sit at the top-right.
        using var bitmap = Solid(40, 20, SKColors.White);
        for (var y = 0; y < 5; y++)
        {
            for (var x = 0; x < 5; x++)
            {
                bitmap.SetPixel(x, y, SKColors.Black);
            }
        }

        var result = await SanitizeAsync(WithExifOrientation(Encode(bitmap, SKEncodedImageFormat.Jpeg), orientation: 6));

        using var upright = SKBitmap.Decode(result!.Content);
        Assert.True(upright.GetPixel(upright.Width - 2, 1).Red < 60);
        Assert.True(upright.GetPixel(1, 1).Red > 200);
    }

    [Fact]
    public async Task Transparency_survives_re_encoding()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(64, 64, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        bitmap.Erase(new SKColor(139, 44, 245, 128));

        var result = await SanitizeAsync(Encode(bitmap, SKEncodedImageFormat.Png));

        using var decoded = SKBitmap.Decode(result!.Content);
        var pixel = decoded.GetPixel(10, 10);
        Assert.InRange(pixel.Alpha, 126, 130);
        Assert.InRange(pixel.Blue, 240, 250);
    }

    [Fact]
    public async Task WebP_is_re_encoded_as_webp() =>
        AssertDecodes((await SanitizeAsync(Encode(Solid(90, 60, SKColors.Teal), SKEncodedImageFormat.Webp)))!, ImageFormat.WebP, 90, 60);

    [Fact]
    public async Task Corrupt_or_header_only_files_are_rejected()
    {
        var png = TestImages.Png(200, 200);
        png[^20] ^= 0xFF;

        Assert.Null(await SanitizeAsync(png));
        Assert.Null(await SanitizeAsync(TestImages.Jpeg(400, 300)));
        Assert.Null(await SanitizeAsync(TestImages.Png(200, 200)[..60]));
    }

    public void Dispose() => _sanitizer.Dispose();

    private async Task<SanitizedImage?> SanitizeAsync(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var info = _inspector.Inspect(stream);
        return info is null ? null : await _sanitizer.SanitizeAsync(stream, info, CancellationToken.None);
    }

    private void AssertDecodes(SanitizedImage result, ImageFormat format, int width, int height)
    {
        Assert.Equal(new ImageInfo(format, width, height), result.Info);
        Assert.Equal(result.Info, _inspector.Inspect(new MemoryStream(result.Content)));
        using var decoded = SKBitmap.Decode(result.Content);
        Assert.NotNull(decoded);
        Assert.Equal((width, height), (decoded.Width, decoded.Height));
    }

    private static SKBitmap Solid(int width, int height, SKColor color)
    {
        var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color);
        return bitmap;
    }

    private static byte[] Encode(SKBitmap bitmap, SKEncodedImageFormat format)
    {
        using var data = bitmap.Encode(format, 90);
        return data.ToArray();
    }

    private static byte[] TextChunk(byte[] text)
    {
        byte[] body = [.. "Comment"u8, 0, .. text];
        var length = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, body.Length);
        byte[] typeAndBody = [.. "tEXt"u8, .. body];
        var crc = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typeAndBody));
        return [.. length, .. typeAndBody, .. crc];
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }

    /// <summary>Inserts a minimal big-endian EXIF APP1 segment holding only an Orientation tag.</summary>
    private static byte[] WithExifOrientation(byte[] jpeg, byte orientation)
    {
        byte[] exif =
        [
            .. "Exif"u8, 0, 0,
            .. "MM"u8, 0, 42, 0, 0, 0, 8, // TIFF header, first IFD at offset 8
            0, 1, // one entry
            0x01, 0x12, 0, 3, 0, 0, 0, 1, 0, orientation, 0, 0, // Orientation, SHORT, count 1
            0, 0, 0, 0, // no next IFD
        ];
        var length = exif.Length + 2;
        return [.. jpeg[..2], 0xFF, 0xE1, (byte)(length >> 8), (byte)length, .. exif, .. jpeg[2..]];
    }
}
