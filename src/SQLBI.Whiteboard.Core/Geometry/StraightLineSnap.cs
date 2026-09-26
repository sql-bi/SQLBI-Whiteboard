namespace SQLBI.Whiteboard.Core.Geometry;

public enum StraightLineDirection
{
    None,
    Horizontal,
    Vertical,
}

/// <summary>
/// The straight-line constraint is horizontal or vertical only. Holding the
/// modifier is the request for one of those two, so a diagonal drag picks the
/// nearer axis instead of falling back to a free stroke - the modifier would
/// otherwise do nothing for most of the directions a hand actually moves in.
/// </summary>
public static class StraightLineSnap
{
    // How far the pen must travel from the anchor before the axis is settled and
    // kept. Once chosen it is not revisited, because a hand drifting off the
    // axis is still drawing the line along that axis.
    public const double DefaultActivationDistance = 24;

    private static bool HasActivationDistance(
        PointD anchor,
        PointD current,
        double activationDistance = DefaultActivationDistance)
    {
        var deltaX = current.X - anchor.X;
        var deltaY = current.Y - anchor.Y;
        var minimum = Math.Max(0, activationDistance);
        return (deltaX * deltaX) + (deltaY * deltaY) >= minimum * minimum;
    }

    public static StraightLineDirection DetectDirection(
        PointD anchor,
        PointD current,
        double activationDistance = DefaultActivationDistance)
    {
        if (!HasActivationDistance(anchor, current, activationDistance))
        {
            return StraightLineDirection.None;
        }

        return Math.Abs(current.X - anchor.X) >= Math.Abs(current.Y - anchor.Y)
            ? StraightLineDirection.Horizontal
            : StraightLineDirection.Vertical;
    }

    public static PointD Apply(
        PointD point,
        PointD anchor,
        StraightLineDirection direction) => direction switch
        {
            StraightLineDirection.Horizontal => new PointD(point.X, anchor.Y),
            StraightLineDirection.Vertical => new PointD(anchor.X, point.Y),
            _ => point,
        };
}
