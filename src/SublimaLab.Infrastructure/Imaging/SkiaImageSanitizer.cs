using Microsoft.Extensions.Logging;
using SkiaSharp;
using SublimaLab.Application.Abstractions;

namespace SublimaLab.Infrastructure.Imaging;

/// <summary>
/// Re-encodes uploads with Skia. Pixels are converted to sRGB (so dropping the ICC profile does
/// not shift colours) and the EXIF orientation is applied (so stored images are upright without
/// metadata). Output keeps the source format: JPEG at quality 95, lossless PNG, WebP at quality 95.
/// </summary>
public sealed partial class SkiaImageSanitizer(ILogger<SkiaImageSanitizer> logger) : IImageSanitizer, IDisposable
{
    private const int JpegQuality = 95;
    private const int WebPQuality = 95;

    // Decoding is CPU and memory heavy (up to 4 bytes per pixel), so limit parallel work.
    private readonly SemaphoreSlim _gate = new(Math.Max(1, Environment.ProcessorCount / 2));

    public async Task<SanitizedImage?> SanitizeAsync(Stream content, ImageInfo source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(source);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            return Sanitize(content, source);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private SanitizedImage? Sanitize(Stream content, ImageInfo source)
    {
        using var data = SKData.Create(content);
        using var codec = data is null ? null : SKCodec.Create(data);
        if (codec is null || codec.Info.Width != source.Width || codec.Info.Height != source.Height)
        {
            LogUndecodable(logger, source.Format, source.Width, source.Height);
            return null;
        }

        // Unpremultiplied pixels keep semi-transparent edges exact; rotation needs a canvas,
        // which only draws premultiplied.
        var origin = codec.EncodedOrigin;
        var alphaType = codec.Info.AlphaType == SKAlphaType.Opaque ? SKAlphaType.Opaque
            : origin == SKEncodedOrigin.TopLeft ? SKAlphaType.Unpremul : SKAlphaType.Premul;
        var decodeInfo = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, alphaType, SKColorSpace.CreateSrgb());

        using var decoded = new SKBitmap(decodeInfo);

        // Truncated files decode partially (IncompleteInput); refusing them avoids storing half-grey artwork.
        if (codec.GetPixels(decodeInfo, decoded.GetPixels()) != SKCodecResult.Success)
        {
            LogUndecodable(logger, source.Format, source.Width, source.Height);
            return null;
        }

        using var rotated = origin == SKEncodedOrigin.TopLeft ? null : Rotate(decoded, origin);
        var upright = rotated ?? decoded;
        using var pixmap = upright.PeekPixels();
        using var encoded = source.Format switch
        {
            ImageFormat.Jpeg => pixmap.Encode(new SKJpegEncoderOptions(JpegQuality, SKJpegEncoderDownsample.Downsample444, SKJpegEncoderAlphaOption.Ignore)),
            ImageFormat.Png => pixmap.Encode(new SKPngEncoderOptions(SKPngEncoderFilterFlags.AllFilters, zLibLevel: 6)),
            ImageFormat.WebP => pixmap.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossy, WebPQuality)),
            _ => null,
        };

        if (encoded is null)
        {
            LogUndecodable(logger, source.Format, source.Width, source.Height);
            return null;
        }

        return new SanitizedImage(encoded.ToArray(), source with { Width = upright.Width, Height = upright.Height });
    }

    /// <summary>Returns an upright copy of a bitmap stored with a rotated or mirrored EXIF origin.</summary>
    private static SKBitmap Rotate(SKBitmap bitmap, SKEncodedOrigin origin)
    {
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var width = swap ? bitmap.Height : bitmap.Width;
        var height = swap ? bitmap.Width : bitmap.Height;
        var upright = new SKBitmap(bitmap.Info.WithSize(width, height));

        using var canvas = new SKCanvas(upright);
        canvas.SetMatrix(OriginMatrix(origin, bitmap.Width, bitmap.Height));
        using var image = SKImage.FromBitmap(bitmap);
        canvas.DrawImage(image, 0, 0, SKSamplingOptions.Default);
        return upright;
    }

    // Maps source pixels onto the upright canvas for each EXIF orientation (1-8).
    private static SKMatrix OriginMatrix(SKEncodedOrigin origin, int w, int h) => origin switch
    {
        SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
        SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
        SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
        SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
        SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
        _ => SKMatrix.Identity,
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not decode {Format} image ({Width}x{Height}); it was rejected")]
    private static partial void LogUndecodable(ILogger logger, ImageFormat format, int width, int height);
}
