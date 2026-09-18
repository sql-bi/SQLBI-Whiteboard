namespace SQLBI.Whiteboard.Export;

/// <summary>
/// Which connection site of a preset shape a board anchor lands on. A preset
/// carries its own numbered list of sites, and an arrow that names the wrong
/// number is re-routed by PowerPoint to a corner nobody asked for, so the table
/// is the one in the ECMA-376 preset shape definitions and nothing is guessed:
/// an anchor whose point is not a site of that preset has no number here, and an
/// arrow written without one stays exactly where the board drew it.
/// </summary>
public static class PresetConnectionSites
{
    private const double Epsilon = 0.000001;

    /// <summary>
    /// The site this anchor is, or nothing when the preset has none there.
    /// U and V are fractions of the shape's box, as the board records them.
    /// </summary>
    public static int? IndexFor(string preset, double u, double v)
    {
        if (!Tables.TryGetValue(preset, out (double U, double V, int Index)[]? sites))
        {
            return null;
        }

        foreach ((double siteU, double siteV, var index) in sites)
        {
            if (Math.Abs(siteU - u) <= Epsilon && Math.Abs(siteV - v) <= Epsilon)
            {
                return index;
            }
        }

        return null;
    }

    /// <summary>
    /// The four sides of the rectangle family, in the order every preset built
    /// on a box lists them: top, left, bottom, right.
    /// </summary>
    private static readonly (double U, double V, int Index)[] BoxSides =
    [
        (0.5, 0, 0), (0, 0.5, 1), (0.5, 1, 2), (1, 0.5, 3),
    ];

    private static readonly Dictionary<string, (double U, double V, int Index)[]> Tables =
        new(StringComparer.Ordinal)
        {
            ["rect"] = BoxSides,
            ["roundRect"] = BoxSides,

            // A diamond's points are the four side midpoints of its box, and its
            // sites are those four in the same order.
            ["diamond"] = BoxSides,

            // An ellipse has eight: the four it touches the box on, and four at
            // 45 degrees between them, which are on the curve rather than on the
            // box's corners and so are not places the board binds to.
            ["ellipse"] = [(0.5, 0, 0), (0, 0.5, 2), (0.5, 1, 4), (1, 0.5, 6)],

            // The apex, then the middle of the left slope, the two base corners
            // with the middle of the base between them, and the middle of the
            // right slope. The slopes' middles are not box points.
            ["triangle"] = [(0.5, 0, 0), (0, 1, 2), (0.5, 1, 3), (1, 1, 4)],

            // The apex and the middle of the base. The other four sites are the
            // pentagon's remaining corners, which sit inside the box.
            ["pentagon"] = [(0.5, 0, 0), (0.5, 1, 3)],

            // The back of the shaft and the point. The other two sites are where
            // the head meets the shaft, which is six tenths across the box.
            ["rightArrow"] = [(0, 0.5, 1), (1, 0.5, 3)],

            // A parallelogram has six sites and not one of them is a corner or a
            // side midpoint of its box: the slanted sides carry them.
            ["parallelogram"] = [],
        };
}
