using System.Text.Json.Serialization;

namespace SublimaLab.Domain.Designs;

/// <summary>
/// Base type for anything placed on a print area. Position is the element's centre in
/// millimetres from the print area's top-left corner; width/height describe its unrotated
/// box in millimetres; rotation is in degrees, clockwise. List order is layer order
/// (first = bottom). New element types are added as new derived records.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ImageElement), "image")]
[JsonDerivedType(typeof(TextElement), "text")]
public abstract record DesignElement
{
    public required string Id { get; init; }
    public decimal X { get; init; }
    public decimal Y { get; init; }
    public decimal Width { get; init; }
    public decimal Height { get; init; }
    public decimal Rotation { get; init; }

    /// <summary>Axis-aligned bounding box of the rotated element, in millimetres.</summary>
    public (decimal Left, decimal Top, decimal Right, decimal Bottom) GetRotatedBounds()
    {
        var radians = (double)Rotation * Math.PI / 180d;
        var cos = Math.Abs(Math.Cos(radians));
        var sin = Math.Abs(Math.Sin(radians));
        var w = (double)Width;
        var h = (double)Height;
        var halfWidth = (decimal)(((w * cos) + (h * sin)) / 2d);
        var halfHeight = (decimal)(((w * sin) + (h * cos)) / 2d);
        return (X - halfWidth, Y - halfHeight, X + halfWidth, Y + halfHeight);
    }
}

/// <summary>Customer artwork. References a server-side upload by id, never a client-supplied URL.</summary>
public sealed record ImageElement : DesignElement
{
    public Guid UploadId { get; init; }
}

public sealed record TextElement : DesignElement
{
    public required string Text { get; init; }
    public required string FontFamily { get; init; }

    /// <summary>Font size in points.</summary>
    public decimal FontSize { get; init; }

    /// <summary>Hex colour (#RRGGBB).</summary>
    public required string Color { get; init; }
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public TextAlignment Align { get; init; } = TextAlignment.Center;

    /// <summary>Extra spacing between characters, in em units.</summary>
    public decimal LetterSpacing { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<TextAlignment>))]
public enum TextAlignment
{
    Left,
    Center,
    Right,
}
