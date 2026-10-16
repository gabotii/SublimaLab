using SublimaLab.Domain.Common;

namespace SublimaLab.Domain.Customization;

/// <summary>
/// A printable region of a product. Design coordinates are expressed in millimetres relative
/// to the top-left corner of the area, which keeps production output resolution-independent:
/// pixels = mm / 25.4 * dpi.
/// </summary>
public sealed class PrintArea : Entity
{
    public Guid CustomizationConfigurationId { get; set; }

    /// <summary>Stable identifier referenced by designs, e.g. "front", "back", "wrap".</summary>
    public required string Key { get; set; }
    public required string Name { get; set; }

    public decimal WidthMm { get; set; }
    public decimal HeightMm { get; set; }
    public PrintAreaShape Shape { get; set; } = PrintAreaShape.Rectangle;

    /// <summary>Where the area's texture maps onto the 3D model, in normalised UV space (0..1).</summary>
    public decimal UvX { get; set; }
    public decimal UvY { get; set; }
    public decimal UvWidth { get; set; } = 1;
    public decimal UvHeight { get; set; } = 1;

    public int SortOrder { get; set; }
}

/// <summary>
/// Visual shape of a print area. Placement is validated against the bounding rectangle;
/// for ellipses, content outside the curve is clipped as print bleed.
/// </summary>
public enum PrintAreaShape
{
    Rectangle = 1,
    Ellipse = 2,
}
