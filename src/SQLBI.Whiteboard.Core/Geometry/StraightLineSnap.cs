namespace SQLBI.Whiteboard.Core.Geometry;

public enum StraightLineDirection
{
    None,
    Horizontal,
    Vertical,
}

public static class StraightLineSnap
{
    public const double DefaultActivationDistance = 8;
    public const double DefaultAngleToleranceDegrees = 15;

    public static bool HasActivationDistance(
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
        double angleToleranceDegrees = DefaultAngleToleranceDegrees,
        double activationDistance = DefaultActivationDistance)
    {
        if (!HasActivationDistance(anchor, current, activationDistance))
        {
            return StraightLineDirection.None;
        }

        var deltaX = Math.Abs(current.X - anchor.X);
        var deltaY = Math.Abs(current.Y - anchor.Y);
        var tolerance = Math.Clamp(angleToleranceDegrees, 0, 45);
        var maximumOffAxisRatio = Math.Tan(tolerance * Math.PI / 180);
        if (deltaY <= deltaX * maximumOffAxisRatio)
        {
            return StraightLineDirection.Horizontal;
        }

        return deltaX <= deltaY * maximumOffAxisRatio
            ? StraightLineDirection.Vertical
            : StraightLineDirection.None;
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
