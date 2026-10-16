namespace SublimaLab.Domain.Ordering;

/// <summary>Postal address snapshot. Countries are ISO 3166-1 alpha-2 codes.</summary>
public sealed record PostalAddress
{
    public required string Line1 { get; init; }
    public string? Line2 { get; init; }
    public required string City { get; init; }
    public required string Region { get; init; }
    public required string PostalCode { get; init; }
    public required string Country { get; init; }
}

public sealed record ContactInfo
{
    public required string FullName { get; init; }
    public required string Email { get; init; }
    public required string Phone { get; init; }
}

/// <summary>
/// Everything a production service needs to render print-ready files for one order item,
/// frozen at order time so later catalog changes cannot alter what was purchased.
/// </summary>
public sealed record ProductionSpec
{
    public required List<ProductionPrintArea> PrintAreas { get; init; }
    public required List<ProductionArtwork> Artwork { get; init; }
    public int MinRecommendedDpi { get; init; }
}

public sealed record ProductionPrintArea(string Key, string Name, decimal WidthMm, decimal HeightMm, string Shape);

public sealed record ProductionArtwork(Guid UploadId, string ContentType, int WidthPx, int HeightPx, string Sha256);
