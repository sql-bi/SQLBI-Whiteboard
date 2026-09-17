namespace SQLBI.Whiteboard.Core.Geometry;

/// <summary>
/// A rectangle turned about its centre, and the axis-aligned box that holds it.
/// A label is stored as its layout size and an angle, and the document indexes
/// the box, so the two have to be computed from each other in one place: the
/// hit test, the area test, the renderer, and the editor all ask here rather
/// than each keeping their own trigonometry.
/// </summary>
public static class RotatedRectangle
{
    /// <summary>
    /// The step a rotation is allowed to take. Anything else - a hand-edited
    /// file, a board from a later release - is snapped to it on the way in.
    /// </summary>
    public const double AngleStep = 45;

    /// <summary>
    /// The angle as a multiple of <see cref="AngleStep"/> in [0, 360).
    /// </summary>
    public static double NormalizeAngle(double angleDegrees)
    {
        if (!double.IsFinite(angleDegrees))
        {
            return 0;
        }

        var steps = (int)Math.Round(angleDegrees / AngleStep, MidpointRounding.AwayFromZero);
        var turn = (int)(360 / AngleStep);
        return (((steps % turn) + turn) % turn) * AngleStep;
    }

    /// <summary>
    /// The four corners in the order <see cref="Polygon.Corners"/> uses, so a
    /// rotated rectangle and an upright one answer an area test the same way.
    /// </summary>
    public static IReadOnlyList<PointD> Corners(
        PointD center,
        double width,
        double height,
        double angleDegrees)
    {
        var halfWidth = width / 2;
        var halfHeight = height / 2;
        return
        [
            Rotate(new PointD(-halfWidth, -halfHeight), angleDegrees) + center,
            Rotate(new PointD(halfWidth, -halfHeight), angleDegrees) + center,
            Rotate(new PointD(halfWidth, halfHeight), angleDegrees) + center,
            Rotate(new PointD(-halfWidth, halfHeight), angleDegrees) + center,
        ];
    }

    public static RectD Bounds(PointD center, double width, double height, double angleDegrees)
    {
        IReadOnlyList<PointD> corners = Corners(center, width, height, angleDegrees);
        var left = corners.Min(corner => corner.X);
        var top = corners.Min(corner => corner.Y);
        var right = corners.Max(corner => corner.X);
        var bottom = corners.Max(corner => corner.Y);
        return new RectD(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// Whether the point is on the rectangle: the point is turned back by the
    /// angle about the centre, and the upright rectangle answers.
    /// </summary>
    public static bool Contains(
        PointD center,
        double width,
        double height,
        double angleDegrees,
        PointD point)
    {
        PointD local = Rotate(point - center, -angleDegrees);
        return Math.Abs(local.X) <= width / 2 && Math.Abs(local.Y) <= height / 2;
    }

    /// <summary>
    /// Where the rectangle's own top-left corner lands once it is turned.
    /// </summary>
    public static PointD TopLeft(PointD center, double width, double height, double angleDegrees) =>
        Corners(center, width, height, angleDegrees)[0];

    /// <summary>
    /// The centre a rectangle of this size and angle has when its top-left
    /// corner is where it was. Text grows down and to the right from where it
    /// was started, which is what keeps a label still while it is typed.
    /// </summary>
    public static PointD CenterFromTopLeft(
        PointD topLeft,
        double width,
        double height,
        double angleDegrees) =>
        topLeft + Rotate(new PointD(width / 2, height / 2), angleDegrees);

    /// <summary>
    /// Clockwise on screen, which is the direction a positive angle turns
    /// everything else the board draws.
    /// </summary>
    public static PointD Rotate(PointD point, double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        return new PointD(
            (point.X * cos) - (point.Y * sin),
            (point.X * sin) + (point.Y * cos));
    }
}
