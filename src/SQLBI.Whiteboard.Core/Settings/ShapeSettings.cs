using SQLBI.Whiteboard.Core.Model;

namespace SQLBI.Whiteboard.Core.Settings;

/// <summary>
/// What the next shape is drawn with: the last outline color, fill, and
/// thickness the property bar was given. It is not offered in Preferences,
/// because the place to change it is the shape in front of you.
/// </summary>
public sealed class ShapeSettings
{
    /// <summary>
    /// A fill is one of the pen colors at this alpha, so the ink drawn over a
    /// filled shape stays readable through it.
    /// </summary>
    public const uint FillAlpha = 0x40000000;

    public uint OutlineArgb { get; set; } = InkPalettes.DefaultPen.Argb;

    /// <summary>
    /// Null is None, which is the default: a shape drawn over ink should not
    /// hide it.
    /// </summary>
    public uint? FillArgb { get; set; }

    public double Thickness { get; set; } = ShapeBoardObject.DefaultThickness;

    /// <summary>
    /// None, then the six pen colors as tints, in the order the Fill row
    /// offers them.
    /// </summary>
    public static IReadOnlyList<uint?> Fills { get; } =
        [null, .. InkPalettes.Pen.Select(swatch => (uint?)Tint(swatch.Argb))];

    public static uint Tint(uint argb) => (argb & 0x00FFFFFF) | FillAlpha;

    public static ShapeSettings Normalize(ShapeSettings? settings)
    {
        if (settings is null)
        {
            return new ShapeSettings();
        }

        var fallback = new ShapeSettings();
        return new ShapeSettings
        {
            OutlineArgb = InkPalettes.Pen.Any(swatch => swatch.Argb == settings.OutlineArgb)
                ? settings.OutlineArgb
                : fallback.OutlineArgb,
            FillArgb = settings.FillArgb is { } fill && Fills.Contains(fill)
                ? fill
                : null,
            Thickness = InkPalettes.PenThicknesses.Any(thickness => thickness == settings.Thickness)
                ? settings.Thickness
                : fallback.Thickness,
        };
    }
}
