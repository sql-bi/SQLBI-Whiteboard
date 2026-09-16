using SQLBI.Whiteboard.Core.Geometry;

namespace SQLBI.Whiteboard.Core.Viewport;

/// <summary>
/// Where the background grid falls. It lives here rather than in the renderer so
/// that the spacing steps are one pure function anyone can test, and so that the
/// surface only turns the positions it is given into screen coordinates.
/// </summary>
public static class GridGeometry
{
    /// <summary>
    /// The spacing at zoom 1, in board pixels.
    /// </summary>
    public const double BaseSpacing = 40;

    /// <summary>
    /// How close two lines may come on screen before the spacing steps up. Below
    /// this the grid reads as a texture rather than as a measure.
    /// </summary>
    public const double MinimumScreenSpacing = 12;

    /// <summary>
    /// The spacing is multiplied rather than scaled continuously, so zooming out
    /// shows the lines spreading and then snapping coarser. That step is the cue:
    /// it is what makes a zoom level visible on an otherwise blank board.
    /// </summary>
    private const double CoarseningFactor = 4;

    /// <summary>
    /// A visible rectangle comes from the camera, so the count is already bounded
    /// by the viewport. The cap is for a caller that asks for something else, and
    /// keeps a bad rectangle from becoming a frozen window.
    /// </summary>
    private const int MaximumLines = 4096;

    public static double SpacingFor(double zoom)
    {
        if (!double.IsFinite(zoom) || zoom <= 0)
        {
            return BaseSpacing;
        }

        var spacing = BaseSpacing;
        while (spacing * zoom < MinimumScreenSpacing)
        {
            spacing *= CoarseningFactor;
        }

        return spacing;
    }

    /// <summary>
    /// The world X of every vertical line that crosses <paramref name="visible"/>.
    /// </summary>
    public static IEnumerable<double> VerticalLines(RectD visible, double spacing) =>
        Positions(visible.Left, visible.Right, spacing);

    /// <summary>
    /// The world Y of every horizontal line that crosses <paramref name="visible"/>.
    /// </summary>
    public static IEnumerable<double> HorizontalLines(RectD visible, double spacing) =>
        Positions(visible.Top, visible.Bottom, spacing);

    private static IEnumerable<double> Positions(double start, double end, double spacing)
    {
        if (!double.IsFinite(start) ||
            !double.IsFinite(end) ||
            !double.IsFinite(spacing) ||
            spacing <= 0 ||
            end < start)
        {
            yield break;
        }

        var first = Math.Ceiling(start / spacing) * spacing;
        var count = Math.Min(MaximumLines, Math.Floor((end - first) / spacing) + 1);
        for (var index = 0; index < count; index++)
        {
            yield return first + (index * spacing);
        }
    }
}
