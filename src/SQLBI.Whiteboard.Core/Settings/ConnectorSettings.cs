using SQLBI.Whiteboard.Core.Model;

namespace SQLBI.Whiteboard.Core.Settings;

/// <summary>
/// What the next connector is drawn with: the last color, thickness, and kind
/// the property bar or the Insert row was given. Like the pen's color and size
/// it is remembered rather than configured, so it has no row in Preferences.
/// </summary>
public sealed class ConnectorSettings
{
    public uint Argb { get; set; } = InkPalettes.DefaultPen.Argb;

    public double Thickness { get; set; } = ConnectorBoardObject.DefaultThickness;

    /// <summary>
    /// An arrow, because a connector drawn between two shapes is usually
    /// meant to show which way round they go.
    /// </summary>
    public ConnectorKind Kind { get; set; } = ConnectorKind.Arrow;

    /// <summary>
    /// Whether the next connector chooses its own sides. Fixed, because an end
    /// dropped on a particular point was dropped there on purpose; whoever
    /// wants Auto says so once on the property bar and gets it from then on.
    /// </summary>
    public bool AutoRoute { get; set; }

    public static ConnectorSettings Normalize(ConnectorSettings? settings)
    {
        if (settings is null)
        {
            return new ConnectorSettings();
        }

        var fallback = new ConnectorSettings();
        return new ConnectorSettings
        {
            Argb = InkPalettes.Pen.Any(swatch => swatch.Argb == settings.Argb)
                ? settings.Argb
                : fallback.Argb,
            Thickness = InkPalettes.PenThicknesses.Any(thickness => thickness == settings.Thickness)
                ? settings.Thickness
                : fallback.Thickness,
            Kind = Enum.IsDefined(settings.Kind) ? settings.Kind : fallback.Kind,
            AutoRoute = settings.AutoRoute,
        };
    }
}
