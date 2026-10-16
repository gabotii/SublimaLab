using System.Security.Cryptography;

namespace SublimaLab.Domain.Ordering;

/// <summary>Human-friendly, non-sequential order numbers such as <c>SL-261001-7KQ2MX</c>.</summary>
public static class OrderNumber
{
    // Crockford-style alphabet: no I, L, O or U to avoid confusion when read aloud.
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string Generate(DateTime utcNow)
    {
        Span<char> suffix = stackalloc char[6];
        for (var i = 0; i < suffix.Length; i++)
        {
            suffix[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return $"SL-{utcNow:yyMMdd}-{suffix}";
    }
}
