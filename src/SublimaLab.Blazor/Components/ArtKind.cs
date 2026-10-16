namespace SublimaLab.Blazor.Components;

/// <summary>Maps catalog identifiers to the matching illustration in the SVG sprite.</summary>
public static class ArtKind
{
    public const string TShirt = "tshirt";
    public const string Mug = "mug";
    public const string KeychainRect = "keychain-rect";
    public const string KeychainSquare = "keychain-square";
    public const string KeychainRound = "keychain-round";

    public static string FromProductSlug(string? slug) => slug switch
    {
        null or "" => TShirt,
        _ when slug.Contains("round-keychain", StringComparison.Ordinal) => KeychainRound,
        _ when slug.Contains("square-keychain", StringComparison.Ordinal) => KeychainSquare,
        _ when slug.Contains("keychain", StringComparison.Ordinal) => KeychainRect,
        _ when slug.Contains("mug", StringComparison.Ordinal) => Mug,
        _ => TShirt,
    };

    /// <summary>Order items carry the SKU rather than the slug (e.g. "KEY-RK-WHI").</summary>
    public static string FromSku(string? sku) => sku switch
    {
        null or "" => TShirt,
        _ when sku.StartsWith("KEY-RK", StringComparison.Ordinal) => KeychainRound,
        _ when sku.StartsWith("KEY-SK", StringComparison.Ordinal) => KeychainSquare,
        _ when sku.StartsWith("KEY", StringComparison.Ordinal) => KeychainRect,
        _ when sku.StartsWith("MUG", StringComparison.Ordinal) => Mug,
        _ => TShirt,
    };

    public static string FromCategory(string? categorySlug) => categorySlug switch
    {
        "mugs" => Mug,
        "keychains" => KeychainRound,
        _ => TShirt,
    };
}
