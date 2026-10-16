using System.Buffers.Binary;
using SublimaLab.Application.Abstractions;

namespace SublimaLab.Infrastructure.Imaging;

/// <summary>
/// Dependency-free image sniffer for PNG, JPEG and WebP. It validates the binary signature and
/// reads dimensions from the header, without decoding pixel data, so it is fast and safe to run
/// on untrusted input. Animated WebP is rejected (not printable).
/// </summary>
public sealed class ImageHeaderInspector : IImageInspector
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public ImageInfo? Inspect(Stream content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!content.CanSeek)
        {
            throw new ArgumentException("A seekable stream is required.", nameof(content));
        }

        var start = content.Position;
        try
        {
            Span<byte> header = stackalloc byte[32];
            var read = content.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
            header = header[..read];

            if (header.StartsWith(PngSignature))
            {
                return ReadPng(header);
            }

            if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            {
                content.Position = start + 2;
                return ReadJpeg(content);
            }

            if (header.Length >= 16 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
            {
                return ReadWebP(header);
            }

            return null;
        }
        catch (EndOfStreamException)
        {
            return null;
        }
        finally
        {
            content.Position = start;
        }
    }

    private static ImageInfo? ReadPng(ReadOnlySpan<byte> header)
    {
        // Signature (8) + IHDR length (4) + "IHDR" (4) + width (4) + height (4)
        if (header.Length < 24 || !header[12..16].SequenceEqual("IHDR"u8))
        {
            return null;
        }

        var width = BinaryPrimitives.ReadInt32BigEndian(header[16..20]);
        var height = BinaryPrimitives.ReadInt32BigEndian(header[20..24]);
        return Create(ImageFormat.Png, width, height);
    }

    private static ImageInfo? ReadJpeg(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[7];

        // Walk segment markers until a Start Of Frame marker reveals the dimensions.
        while (true)
        {
            var marker = ReadMarker(stream);
            if (marker is null or 0xD9 or 0xDA)
            {
                return null; // End of image or start of scan before any frame header.
            }

            if (marker is 0x01 or (>= 0xD0 and <= 0xD7))
            {
                continue; // Standalone markers without a length field.
            }

            stream.ReadExactly(buffer[..2]);
            var length = BinaryPrimitives.ReadUInt16BigEndian(buffer[..2]);
            if (length < 2)
            {
                return null;
            }

            var isStartOfFrame = marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC);
            if (isStartOfFrame)
            {
                // Precision (1), height (2), width (2)
                stream.ReadExactly(buffer[..5]);
                var height = BinaryPrimitives.ReadUInt16BigEndian(buffer[1..3]);
                var width = BinaryPrimitives.ReadUInt16BigEndian(buffer[3..5]);
                return Create(ImageFormat.Jpeg, width, height);
            }

            var next = stream.Position + length - 2;
            if (next > stream.Length)
            {
                return null;
            }

            stream.Position = next;
        }
    }

    private static int? ReadMarker(Stream stream)
    {
        var value = stream.ReadByte();
        if (value != 0xFF)
        {
            return null;
        }

        // Fill bytes (0xFF) may precede a marker.
        do
        {
            value = stream.ReadByte();
        }
        while (value == 0xFF);

        return value < 0 ? null : value;
    }

    private static ImageInfo? ReadWebP(ReadOnlySpan<byte> header)
    {
        var chunk = header[12..16];

        if (chunk.SequenceEqual("VP8 "u8))
        {
            // Frame tag (3) + start code 9D 01 2A (3) + 14-bit width/height.
            if (header.Length < 30 || header[23] != 0x9D || header[24] != 0x01 || header[25] != 0x2A)
            {
                return null;
            }

            var width = BinaryPrimitives.ReadUInt16LittleEndian(header[26..28]) & 0x3FFF;
            var height = BinaryPrimitives.ReadUInt16LittleEndian(header[28..30]) & 0x3FFF;
            return Create(ImageFormat.WebP, width, height);
        }

        if (chunk.SequenceEqual("VP8L"u8))
        {
            if (header.Length < 25 || header[20] != 0x2F)
            {
                return null;
            }

            var bits = BinaryPrimitives.ReadUInt32LittleEndian(header[21..25]);
            var width = (int)(bits & 0x3FFF) + 1;
            var height = (int)((bits >> 14) & 0x3FFF) + 1;
            return Create(ImageFormat.WebP, width, height);
        }

        if (chunk.SequenceEqual("VP8X"u8))
        {
            const byte animationFlag = 0x02;
            if (header.Length < 30 || (header[20] & animationFlag) != 0)
            {
                return null;
            }

            var width = (header[24] | (header[25] << 8) | (header[26] << 16)) + 1;
            var height = (header[27] | (header[28] << 8) | (header[29] << 16)) + 1;
            return Create(ImageFormat.WebP, width, height);
        }

        return null;
    }

    private static ImageInfo? Create(ImageFormat format, int width, int height) =>
        width > 0 && height > 0 ? new ImageInfo(format, width, height) : null;
}
