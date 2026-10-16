namespace SublimaLab.Application.Abstractions;

public enum ImageFormat
{
    Png,
    Jpeg,
    WebP,
}

public sealed record ImageInfo(ImageFormat Format, int Width, int Height)
{
    public string ContentType => Format switch
    {
        ImageFormat.Png => "image/png",
        ImageFormat.Jpeg => "image/jpeg",
        ImageFormat.WebP => "image/webp",
        _ => throw new InvalidOperationException($"Unknown format {Format}"),
    };

    public string Extension => Format switch
    {
        ImageFormat.Png => ".png",
        ImageFormat.Jpeg => ".jpg",
        ImageFormat.WebP => ".webp",
        _ => throw new InvalidOperationException($"Unknown format {Format}"),
    };
}

/// <summary>
/// Identifies an image by its binary signature (not by its name or declared MIME type)
/// and reads its pixel dimensions from the header without decoding the image.
/// </summary>
public interface IImageInspector
{
    /// <returns>Image information, or null when the content is not a supported, well-formed image.</returns>
    ImageInfo? Inspect(Stream content);
}
