using SQLBI.Whiteboard.Core.Geometry;

namespace SQLBI.Whiteboard.Core.Model;

public abstract record BoardObject(Guid Id, int ZIndex, RectD Bounds);

public interface IBoardContainer
{
}

public readonly record struct InkPoint(PointD Position, float Pressure, long Timestamp);

public enum PenKind
{
    Pen,
    Highlighter,
    Calligraphy,
}

public readonly record struct PenStyle(
    uint Argb,
    double Thickness,
    PenKind Kind = PenKind.Pen)
{
    public static PenStyle Default { get; } = new(0xFF1F2937, 3);
}

public static class PenStyleMetrics
{
    public static double MaximumThickness(PenStyle style) => style.Kind switch
    {
        PenKind.Highlighter => style.Thickness * 4,
        PenKind.Calligraphy => style.Thickness * 3,
        _ => style.Thickness,
    };
}

public static class CalligraphyDynamics
{
    public static float AdjustPressure(
        float rawPressure,
        double speedInDeviceIndependentPixelsPerMillisecond)
    {
        var pressure = rawPressure <= 0
            ? 0.25
            : Math.Clamp(rawPressure, 0.02f, 1f);
        var pressureResponse = Math.Pow(pressure, 0.65);
        var speed = Math.Clamp(speedInDeviceIndependentPixelsPerMillisecond, 0, 12);
        var speedResponse = 1 / (1 + (0.8 * speed));
        return (float)Math.Clamp(
            0.04 + (0.96 * pressureResponse * speedResponse),
            0.04,
            1);
    }
}

public sealed record InkStrokeObject(
    Guid Id,
    int ZIndex,
    RectD Bounds,
    IReadOnlyList<InkPoint> Points,
    PenStyle Style,
    Guid? ContainerId = null) : BoardObject(Id, ZIndex, Bounds)
{
    public static InkStrokeObject Create(
        IEnumerable<InkPoint> points,
        PenStyle style,
        int zIndex,
        Guid? id = null,
        Guid? containerId = null)
    {
        var pointList = points.ToArray();
        if (pointList.Length == 0)
        {
            throw new ArgumentException("A stroke needs at least one point.", nameof(points));
        }

        var maximumThickness = PenStyleMetrics.MaximumThickness(style);
        var bounds = RectD.FromPoints(
            pointList.Select(point => point.Position),
            Math.Max(1, maximumThickness / 2));

        return new InkStrokeObject(
            id ?? Guid.NewGuid(),
            zIndex,
            bounds,
            pointList,
            style,
            containerId);
    }

    public bool Touches(RectD rectangle)
    {
        var contactBounds = rectangle.Inflate(
            Math.Max(0.5, PenStyleMetrics.MaximumThickness(Style) / 2));
        if (!Bounds.Intersects(contactBounds))
        {
            return false;
        }

        if (Points.Any(point => contactBounds.Contains(point.Position)))
        {
            return true;
        }

        for (var index = 1; index < Points.Count; index++)
        {
            if (SegmentIntersectsRectangle(
                    Points[index - 1].Position,
                    Points[index].Position,
                    contactBounds))
            {
                return true;
            }
        }

        return false;
    }

    public InkStrokeObject TransformWithContainer(RectD before, RectD after)
    {
        var scaleX = after.Width / Math.Max(0.000001, before.Width);
        var scaleY = after.Height / Math.Max(0.000001, before.Height);
        var transformedPoints = Points.Select(point => point with
        {
            Position = new PointD(
                after.Left + ((point.Position.X - before.Left) * scaleX),
                after.Top + ((point.Position.Y - before.Top) * scaleY)),
        });
        var thicknessScale = Math.Sqrt(Math.Abs(scaleX * scaleY));
        var transformedStyle = Style with
        {
            Thickness = Math.Max(0.1, Style.Thickness * thicknessScale),
        };

        return Create(
            transformedPoints,
            transformedStyle,
            ZIndex,
            Id,
            ContainerId);
    }

    public bool HitTest(PointD point, double radius)
    {
        if (!Bounds.Inflate(radius).Contains(point))
        {
            return false;
        }

        if (Points.Count == 1)
        {
            return DistanceSquared(Points[0].Position, point) <= radius * radius;
        }

        for (var index = 1; index < Points.Count; index++)
        {
            if (DistanceToSegmentSquared(
                    point,
                    Points[index - 1].Position,
                    Points[index].Position) <= radius * radius)
            {
                return true;
            }
        }

        return false;
    }

    private static double DistanceSquared(PointD first, PointD second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return (dx * dx) + (dy * dy);
    }

    private static double DistanceToSegmentSquared(PointD point, PointD start, PointD end)
    {
        var segmentX = end.X - start.X;
        var segmentY = end.Y - start.Y;
        var lengthSquared = (segmentX * segmentX) + (segmentY * segmentY);
        if (lengthSquared <= double.Epsilon)
        {
            return DistanceSquared(point, start);
        }

        var projection = (((point.X - start.X) * segmentX) + ((point.Y - start.Y) * segmentY)) / lengthSquared;
        projection = Math.Clamp(projection, 0, 1);
        var closest = new PointD(start.X + (projection * segmentX), start.Y + (projection * segmentY));
        return DistanceSquared(point, closest);
    }

    private static bool SegmentIntersectsRectangle(PointD start, PointD end, RectD rectangle)
    {
        var minimum = 0d;
        var maximum = 1d;
        var deltaX = end.X - start.X;
        var deltaY = end.Y - start.Y;

        return ClipSegment(-deltaX, start.X - rectangle.Left, ref minimum, ref maximum) &&
               ClipSegment(deltaX, rectangle.Right - start.X, ref minimum, ref maximum) &&
               ClipSegment(-deltaY, start.Y - rectangle.Top, ref minimum, ref maximum) &&
               ClipSegment(deltaY, rectangle.Bottom - start.Y, ref minimum, ref maximum);
    }

    private static bool ClipSegment(double direction, double distance, ref double minimum, ref double maximum)
    {
        if (Math.Abs(direction) <= double.Epsilon)
        {
            return distance >= 0;
        }

        var ratio = distance / direction;
        if (direction < 0)
        {
            if (ratio > maximum)
            {
                return false;
            }

            minimum = Math.Max(minimum, ratio);
        }
        else
        {
            if (ratio < minimum)
            {
                return false;
            }

            maximum = Math.Min(maximum, ratio);
        }

        return true;
    }
}

public sealed record ImageBoardObject(
    Guid Id,
    int ZIndex,
    RectD Bounds,
    string AssetId) : BoardObject(Id, ZIndex, Bounds), IBoardContainer;

public static class TextLanguageIds
{
    public const string Plain = "plain";
    public const string Dax = "dax";
    public const string SqlServer = "sqlserver";

    public static string Normalize(string? languageId) =>
        languageId?.Trim().ToLowerInvariant() switch
        {
            Dax => Dax,
            SqlServer => SqlServer,
            _ => Plain,
        };
}

public record TextBoardObject(
    Guid Id,
    int ZIndex,
    RectD Bounds,
    string Title,
    string Text,
    double VisualScale = 1,
    string LanguageId = TextLanguageIds.Plain) : BoardObject(Id, ZIndex, Bounds), IBoardContainer;

public enum LiveViewSourceKind
{
    Unknown,
    Window,
    Display,
}

public sealed record LiveViewSourceConfiguration(
    LiveViewSourceKind Kind,
    string DisplayName,
    string? StableId = null);

public sealed record LiveViewBoardObject(
    Guid Id,
    int ZIndex,
    RectD Bounds,
    LiveViewSourceConfiguration Source,
    string? SnapshotAssetId = null,
    int DesiredFrameRate = 15,
    bool CaptureCursor = false,
    bool IsFrozen = false) : BoardObject(Id, ZIndex, Bounds), IBoardContainer;

public sealed record BoardAsset(
    string Id,
    string OriginalFileName,
    string ContentType,
    byte[] Data);
