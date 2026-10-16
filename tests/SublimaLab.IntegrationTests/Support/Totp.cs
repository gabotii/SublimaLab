using System.Buffers.Binary;
using System.Security.Cryptography;

namespace SublimaLab.IntegrationTests.Support;

/// <summary>RFC 6238 time-based codes, as an authenticator app computes them (HMAC-SHA1, 30 s, 6 digits).</summary>
public static class Totp
{
    /// <param name="sharedKey">The base32 key from the setup endpoint (spaces and case don't matter).</param>
    /// <param name="stepOffset">Codes for neighbouring 30-second steps; the server accepts a small window.</param>
    public static string Code(string sharedKey, int stepOffset = 0)
    {
        var step = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30) + stepOffset;
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);

        var hash = HMACSHA1.HashData(Base32Decode(sharedKey), counter);
        var offset = hash[^1] & 0x0F;
        var value = (BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset)) & 0x7FFFFFFF) % 1_000_000;
        return value.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static byte[] Base32Decode(string text)
    {
        const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in text.ToUpperInvariant().Where(c => c is not (' ' or '=')))
        {
            buffer = (buffer << 5) | Alphabet.IndexOf(c);
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                output.Add((byte)(buffer >> bits));
            }
        }

        return [.. output];
    }
}
