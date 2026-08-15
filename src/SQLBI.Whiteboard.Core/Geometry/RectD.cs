namespace SQLBI.Whiteboard.Core.Geometry;

public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public static RectD Empty { get; } = new(0, 0, 0, 0);

    public double Left => X;
    public double Top => Y;
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public PointD Center => new(X + (Width / 2), Y + (Height / 2));

    public bool Contains(PointD point) =>
        point.X >= Left && point.X <= Right && point.Y >= Top && point.Y <= Bottom;

    public bool Intersects(RectD other) =>
        other.Right >= Left && other.Left <= Right &&
        other.Bottom >= Top && other.Top <= Bottom;

    public RectD Inflate(double amount) =>
        new(X - amount, Y - amount, Width + (amount * 2), Height + (amount * 2));

    public RectD Translate(PointD delta) => new(X + delta.X, Y + delta.Y, Width, Height);

    public RectD WithSize(double width, double height) =>
        new(X, Y, Math.Max(1, width), Math.Max(1, height));

    public RectD WithCenteredAspectRatio(double widthToHeightRatio)
    {
        if (!double.IsFinite(widthToHeightRatio) || widthToHeightRatio <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(widthToHeightRatio),
                "Aspect ratio must be a finite positive number.");
        }

        var area = Math.Max(1, Width) * Math.Max(1, Height);
        var width = Math.Sqrt(area * widthToHeightRatio);
        var height = Math.Sqrt(area / widthToHeightRatio);
        return new RectD(
            Center.X - (width / 2),
            Center.Y - (height / 2),
            width,
            height);
    }

    public static RectD FromPoints(IEnumerable<PointD> points, double padding = 0)
    {
        using var enumerator = points.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            return Empty;
        }

        var minX = enumerator.Current.X;
        var minY = enumerator.Current.Y;
        var maxX = minX;
        var maxY = minY;

        while (enumerator.MoveNext())
        {
            minX = Math.Min(minX, enumerator.Current.X);
            minY = Math.Min(minY, enumerator.Current.Y);
            maxX = Math.Max(maxX, enumerator.Current.X);
            maxY = Math.Max(maxY, enumerator.Current.Y);
        }

        return new RectD(
            minX - padding,
            minY - padding,
            Math.Max(1, maxX - minX) + (padding * 2),
            Math.Max(1, maxY - minY) + (padding * 2));
    }
}
