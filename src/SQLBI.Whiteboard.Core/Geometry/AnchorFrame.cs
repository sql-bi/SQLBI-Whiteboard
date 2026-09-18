namespace SQLBI.Whiteboard.Core.Geometry;

/// <summary>
/// The rectangle an object's anchors are fractions of, and the turn that
/// rectangle has taken about its own centre. An anchor stays where it was put
/// on the object - the middle of that side, that corner - however the object is
/// turned, so the fractions are read in the unturned rectangle and the point
/// they name is turned with it. An object that has no angle hands over its box
/// and nothing changes.
/// </summary>
public readonly record struct AnchorFrame(RectD Layout, double AngleDegrees)
{
    /// <summary>
    /// The world point that layout point is drawn at.
    /// </summary>
    public PointD ToWorld(PointD layoutPoint) => AngleDegrees == 0
        ? layoutPoint
        : RotatedRectangle.Rotate(layoutPoint - Layout.Center, AngleDegrees) + Layout.Center;

    /// <summary>
    /// The layout point a world point stands on, which is what a hit test and a
    /// dropped endpoint ask for: turn it back, and the unturned rectangle
    /// answers.
    /// </summary>
    public PointD ToLayout(PointD worldPoint) => AngleDegrees == 0
        ? worldPoint
        : RotatedRectangle.Rotate(worldPoint - Layout.Center, -AngleDegrees) + Layout.Center;

    public IReadOnlyList<PointD> Corners() =>
        RotatedRectangle.Corners(Layout.Center, Layout.Width, Layout.Height, AngleDegrees);
}
