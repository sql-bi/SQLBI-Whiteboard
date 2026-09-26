namespace SQLBI.Whiteboard.Core.Geometry;

/// <summary>
/// The containment tests an area selection is made of. A rubber band and a
/// lasso apply the same two tests to every object - partly inside, wholly
/// inside - so the tests for the rectangle and the polygon sit side by side
/// here, and the document never has to know which one was drawn.
/// </summary>
public static class Polygon
{
    /// <summary>
    /// The even-odd rule: a ray cast to the right crosses an odd number of
    /// edges for a point inside. A lasso uses it because a loop drawn back
    /// across itself then leaves a hole where the drawing shows one.
    /// </summary>
    public static bool Contains(IReadOnlyList<PointD> polygon, PointD point)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        if (polygon.Count < 3)
        {
            return false;
        }

        var inside = false;
        for (int index = 0, previous = polygon.Count - 1; index < polygon.Count; previous = index++)
        {
            PointD current = polygon[index];
            PointD last = polygon[previous];
            if ((current.Y > point.Y) != (last.Y > point.Y) &&
                point.X < (((last.X - current.X) * (point.Y - current.Y)) /
                           (last.Y - current.Y)) + current.X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    /// <summary>
    /// Every corner inside. A concave polygon can still bite into the middle of
    /// a rectangle whose corners are all inside, and that is deliberate. For
    /// anything the area treats as a box, "fully inside" means all its corners
    /// are inside, so a lasso drawn around a picture takes it whatever its outline does
    /// between the corners.
    /// </summary>
    public static bool ContainsRectangle(IReadOnlyList<PointD> polygon, RectD rectangle) =>
        Corners(rectangle).All(corner => Contains(polygon, corner));

    public static bool IntersectsRectangle(IReadOnlyList<PointD> polygon, RectD rectangle)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        if (polygon.Count < 3)
        {
            return false;
        }

        if (Corners(rectangle).Any(corner => Contains(polygon, corner)))
        {
            return true;
        }

        for (var index = 0; index < polygon.Count; index++)
        {
            PointD start = polygon[index];
            if (rectangle.Contains(start))
            {
                return true;
            }

            if (RectangleIntersectsSegment(rectangle, start, polygon[(index + 1) % polygon.Count]))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IntersectsSegment(IReadOnlyList<PointD> polygon, PointD start, PointD end)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        if (polygon.Count < 3)
        {
            return false;
        }

        if (Contains(polygon, start) || Contains(polygon, end))
        {
            return true;
        }

        for (var index = 0; index < polygon.Count; index++)
        {
            if (SegmentsIntersect(
                    start,
                    end,
                    polygon[index],
                    polygon[(index + 1) % polygon.Count]))
            {
                return true;
            }
        }

        return false;
    }

    public static bool RectangleContainsRectangle(RectD area, RectD rectangle) =>
        rectangle.Left >= area.Left &&
        rectangle.Right <= area.Right &&
        rectangle.Top >= area.Top &&
        rectangle.Bottom <= area.Bottom;

    /// <summary>
    /// Liang-Barsky: the segment is clipped against the four edges in turn, and
    /// survives when some stretch of it is left.
    /// </summary>
    public static bool RectangleIntersectsSegment(RectD area, PointD start, PointD end)
    {
        var minimum = 0d;
        var maximum = 1d;
        var deltaX = end.X - start.X;
        var deltaY = end.Y - start.Y;

        return Clip(-deltaX, start.X - area.Left, ref minimum, ref maximum) &&
               Clip(deltaX, area.Right - start.X, ref minimum, ref maximum) &&
               Clip(-deltaY, start.Y - area.Top, ref minimum, ref maximum) &&
               Clip(deltaY, area.Bottom - start.Y, ref minimum, ref maximum);
    }

    public static IReadOnlyList<PointD> Corners(RectD rectangle) =>
    [
        new(rectangle.Left, rectangle.Top),
        new(rectangle.Right, rectangle.Top),
        new(rectangle.Right, rectangle.Bottom),
        new(rectangle.Left, rectangle.Bottom),
    ];

    public static bool SegmentsIntersect(
        PointD firstStart,
        PointD firstEnd,
        PointD secondStart,
        PointD secondEnd)
    {
        var first = Orientation(firstStart, firstEnd, secondStart);
        var second = Orientation(firstStart, firstEnd, secondEnd);
        var third = Orientation(secondStart, secondEnd, firstStart);
        var fourth = Orientation(secondStart, secondEnd, firstEnd);

        if (first != second && third != fourth)
        {
            return true;
        }

        return (first == 0 && OnSegment(firstStart, secondStart, firstEnd)) ||
               (second == 0 && OnSegment(firstStart, secondEnd, firstEnd)) ||
               (third == 0 && OnSegment(secondStart, firstStart, secondEnd)) ||
               (fourth == 0 && OnSegment(secondStart, firstEnd, secondEnd));
    }

    private static int Orientation(PointD first, PointD second, PointD third)
    {
        var value = ((second.Y - first.Y) * (third.X - second.X)) -
                    ((second.X - first.X) * (third.Y - second.Y));
        if (Math.Abs(value) <= 0.000000001)
        {
            return 0;
        }

        return value > 0 ? 1 : -1;
    }

    private static bool OnSegment(PointD start, PointD point, PointD end) =>
        point.X <= Math.Max(start.X, end.X) && point.X >= Math.Min(start.X, end.X) &&
        point.Y <= Math.Max(start.Y, end.Y) && point.Y >= Math.Min(start.Y, end.Y);

    private static bool Clip(double direction, double distance, ref double minimum, ref double maximum)
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
