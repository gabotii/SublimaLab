namespace SublimaLab.Domain.Designs;

/// <summary>
/// The structured, versioned description of a customer's design. It is stored as JSON on
/// saved designs, cart items and (immutably) order items, so new element types can be added
/// without schema migrations, and the exact configuration ordered is always preserved.
/// </summary>
public sealed record DesignDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public List<DesignSurface> Surfaces { get; init; } = [];

    public IEnumerable<DesignElement> AllElements() => Surfaces.SelectMany(s => s.Elements);

    public bool IsEmpty() => !AllElements().Any();

    public int UsedPrintAreaCount() => Surfaces.Count(s => s.Elements.Count > 0);

    public IReadOnlyCollection<Guid> ReferencedUploadIds() =>
        AllElements().OfType<ImageElement>().Select(e => e.UploadId).Distinct().ToArray();
}

/// <summary>The elements placed on one print area of the product.</summary>
public sealed record DesignSurface
{
    public required string PrintArea { get; init; }
    public List<DesignElement> Elements { get; init; } = [];
}
