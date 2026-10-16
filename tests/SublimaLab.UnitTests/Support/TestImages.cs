using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace SublimaLab.Tests.Support;

/// <summary>Builds small, structurally valid image files for tests.</summary>
public static class TestImages
{
    /// <summary>A real, decodable RGB PNG (all black).</summary>
    public static byte[] Png(int width, int height)
    {
        using var output = new MemoryStream();
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // bit depth
        header[9] = 2; // colour type: RGB
        WriteChunk(output, "IHDR", header);

        using (var raw = new MemoryStream())
        {
            using (var zlib = new ZLibStream(raw, CompressionLevel.Fastest, leaveOpen: true))
            {
                var row = new byte[1 + (width * 3)]; // filter byte + pixels
                for (var y = 0; y < height; y++)
                {
                    zlib.Write(row);
                }
            }

            WriteChunk(output, "IDAT", raw.ToArray());
        }

        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    /// <summary>JPEG header with an APP0 segment followed by a baseline SOF0 frame.</summary>
    public static byte[] Jpeg(int width, int height)
    {
        var bytes = new List<byte> { 0xFF, 0xD8 };
        bytes.AddRange([0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00, 0x01, 0x01, 0x00, 0x00, 0x48, 0x00, 0x48, 0x00, 0x00]);
        bytes.AddRange([0xFF, 0xC0, 0x00, 0x11, 0x08, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 0x03, 0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01]);
        bytes.AddRange([0xFF, 0xD9]);
        return [.. bytes];
    }

    public static byte[] WebPLossy(int width, int height)
    {
        var payload = new byte[10];
        payload[3] = 0x9D;
        payload[4] = 0x01;
        payload[5] = 0x2A;
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(6), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(8), (ushort)height);
        return Riff("VP8 ", payload);
    }

    public static byte[] WebPLossless(int width, int height)
    {
        var payload = new byte[5];
        payload[0] = 0x2F;
        var bits = (uint)(width - 1) | ((uint)(height - 1) << 14);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(1), bits);
        return Riff("VP8L", payload);
    }

    public static byte[] WebPExtended(int width, int height, bool animated = false)
    {
        var payload = new byte[10];
        payload[0] = animated ? (byte)0x02 : (byte)0x00;
        WriteUInt24(payload.AsSpan(4), width - 1);
        WriteUInt24(payload.AsSpan(7), height - 1);
        return Riff("VP8X", payload);
    }

    private static byte[] Riff(string chunk, byte[] payload)
    {
        using var output = new MemoryStream();
        output.Write("RIFF"u8);
        Span<byte> size = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(size, 4 + 8 + payload.Length);
        output.Write(size);
        output.Write("WEBP"u8);
        output.Write(Encoding.ASCII.GetBytes(chunk));
        BinaryPrimitives.WriteInt32LittleEndian(size, payload.Length);
        output.Write(size);
        output.Write(payload);
        return output.ToArray();
    }

    private static void WriteUInt24(Span<byte> target, int value)
    {
        target[0] = (byte)value;
        target[1] = (byte)(value >> 8);
        target[2] = (byte)(value >> 16);
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);

        var typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);

        var crc = Crc32([.. typeBytes, .. data]);
        BinaryPrimitives.WriteUInt32BigEndian(length, crc);
        output.Write(length);
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return crc ^ 0xFFFFFFFFu;
    }
}
