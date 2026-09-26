using SQLBI.Whiteboard.Core.Geometry;

namespace SQLBI.Whiteboard.Core.Settings;

/// <summary>
/// Where the Insert palette sits before anyone has moved it, and how far it is
/// allowed to go once they have. Both are arithmetic on a window, a toolbar, and
/// a palette, so the rule for each <see cref="ToolbarPlacement"/> can be read
/// without a window on screen.
/// </summary>
public static class InsertPalettePlacement
{
    /// <summary>
    /// The gap between the main tool palette and this one, in device-independent
    /// pixels. Narrower than the toolbar's own inset from the window edge, so
    /// the two read as a pair rather than as two unrelated panels.
    /// </summary>
    public const double Gap = 12;

    /// <summary>
    /// The palette starts against the side the toolbar is on, clear of it: below
    /// a toolbar at the top and above one at the bottom, because "below" at the
    /// bottom of the window is off the board.
    /// </summary>
    public static PointD Default(
        ToolbarPlacement placement,
        double windowWidth,
        double windowHeight,
        RectD toolbar,
        double paletteWidth,
        double paletteHeight)
    {
        var x = placement switch
        {
            ToolbarPlacement.TopLeft or ToolbarPlacement.BottomLeft => toolbar.Left,
            ToolbarPlacement.BottomCenter => toolbar.Center.X - (paletteWidth / 2),
            _ => toolbar.Right - paletteWidth,
        };
        var y = placement switch
        {
            ToolbarPlacement.BottomLeft or ToolbarPlacement.BottomRight or ToolbarPlacement.BottomCenter
                => toolbar.Top - Gap - paletteHeight,
            _ => toolbar.Bottom + Gap,
        };

        return Clamp(
            new PointD(x, y),
            windowWidth,
            windowHeight,
            paletteWidth,
            paletteHeight);
    }

    /// <summary>
    /// Keeps the whole palette inside the window, which is stricter than keeping
    /// its grip reachable and says the same thing about a palette smaller than
    /// the window. One larger than the window comes to rest at the top left,
    /// where the grip still is.
    /// </summary>
    public static PointD Clamp(
        PointD position,
        double windowWidth,
        double windowHeight,
        double paletteWidth,
        double paletteHeight) =>
        new(
            Math.Clamp(position.X, 0, Math.Max(0, windowWidth - paletteWidth)),
            Math.Clamp(position.Y, 0, Math.Max(0, windowHeight - paletteHeight)));

    /// <summary>
    /// A place in the window as the fraction of it that settings keep. A window
    /// with no size yet gives 0, the top left, instead of dividing by zero.
    /// </summary>
    public static PointD ToFraction(PointD position, double windowWidth, double windowHeight) =>
        new(
            windowWidth > 0 ? Math.Clamp(position.X / windowWidth, 0, 1) : 0,
            windowHeight > 0 ? Math.Clamp(position.Y / windowHeight, 0, 1) : 0);

    public static PointD FromFraction(PointD fraction, double windowWidth, double windowHeight) =>
        new(fraction.X * windowWidth, fraction.Y * windowHeight);
}
