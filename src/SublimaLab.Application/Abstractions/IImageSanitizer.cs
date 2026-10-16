namespace SublimaLab.Application.Abstractions;

/// <summary>An image rebuilt from its decoded pixels, with its final format and dimensions.</summary>
public sealed record SanitizedImage(byte[] Content, ImageInfo Info);

/// <summary>
/// Fully decodes an untrusted image and re-encodes its pixels into a fresh file. Anything that
/// is not pixel data (EXIF/GPS metadata, ICC profiles, comments, trailing payloads, extra frames)
/// is dropped, so stored files are images the server produced, not bytes the client sent.
/// </summary>
public interface IImageSanitizer
{
    /// <param name="content">The original file, already identified by <see cref="IImageInspector"/>.</param>
    /// <param name="source">What the header inspection found; dimensions must already be validated.</param>
    /// <returns>The re-encoded image, or null when the file cannot be decoded.</returns>
    Task<SanitizedImage?> SanitizeAsync(Stream content, ImageInfo source, CancellationToken cancellationToken);
}
