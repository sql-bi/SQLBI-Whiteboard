namespace SQLBI.Whiteboard.Core.Geometry;

public readonly record struct PointD(double X, double Y)
{
    public static PointD operator +(PointD left, PointD right) =>
        new(left.X + right.X, left.Y + right.Y);

    public static PointD operator -(PointD left, PointD right) =>
        new(left.X - right.X, left.Y - right.Y);

    public static PointD operator *(PointD point, double factor) =>
        new(point.X * factor, point.Y * factor);
}
