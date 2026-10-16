namespace SublimaLab.Application.Customization;

/// <summary>Global limits for design documents, shared with clients through the product API.</summary>
public static class DesignRules
{
    /// <summary>Fonts the production pipeline has licensed and installed.</summary>
    public static readonly IReadOnlyList<string> AllowedFonts =
    [
        "Inter",
        "Poppins",
        "Montserrat",
        "Bebas Neue",
        "Playfair Display",
        "Pacifico",
        "Permanent Marker",
        "Roboto Mono",
    ];

    public const int MaxSurfaces = 10;
    public const int MaxTextLength = 120;
    public const decimal MinFontSize = 4;
    public const decimal MaxFontSize = 400;
    public const decimal MinLetterSpacing = -0.5m;
    public const decimal MaxLetterSpacing = 2m;
    public const decimal MinElementSizeMm = 1;
    public const decimal MaxElementSizeMm = 2000;

    /// <summary>Tolerance for placement checks, absorbing client-side floating point rounding.</summary>
    public const decimal BoundsToleranceMm = 0.5m;

    public const string LowResolutionMessage =
        "Your image may appear blurry when printed. For best results, use a high-resolution image.";
}
