using SQLBI.Whiteboard.Core.Model;

namespace SQLBI.Whiteboard.Core.Geometry;

public enum ShapeSegmentKind
{
    Line,
    Arc,
}

/// <summary>
/// One step of an outline. An arc carries its centre as well as its radii, so
/// walking the outline never has to solve for one, while the radii and the
/// direction are what a renderer's arc primitive asks for.
/// </summary>
public readonly record struct ShapeSegment(
    ShapeSegmentKind Kind,
    PointD End,
    PointD Center,
    double RadiusX,
    double RadiusY,
    bool Clockwise)
{
    public static ShapeSegment Line(PointD end) =>
        new(ShapeSegmentKind.Line, end, default, 0, 0, true);

    public static ShapeSegment Arc(PointD end, PointD center, double radiusX, double radiusY) =>
        new(ShapeSegmentKind.Arc, end, center, radiusX, radiusY, true);
}

/// <summary>
/// A closed outline: where it starts, and the steps back round to there.
/// </summary>
public sealed record ShapeOutline(PointD Start, IReadOnlyList<ShapeSegment> Segments);

/// <summary>
/// The outline of every shape kind, as a function of its box. One description
/// serves the renderer, the hit test, the area selection, and a later vector
/// export, so none of them derives the geometry a second time and they cannot
/// disagree about where a shape's edge is.
/// </summary>
public static class ShapeGeometry
{
    /// <summary>
    /// A rounded rectangle's corner, and a parallelogram's slant, as a fraction
    /// of the box. Proportional rather than absolute, so a shape dragged larger
    /// keeps the look it had when it was drawn.
    /// </summary>
    public const double CornerFraction = 0.2;
    public const double SlantFraction = 0.25;

    /// <summary>
    /// Where a block arrow's head begins, and how thick its shaft is, as
    /// fractions of the box.
    /// </summary>
    public const double ArrowHeadFraction = 0.6;
    public const double ArrowShaftFraction = 0.5;

    /// <summary>
    /// Radians per point when an arc is walked as a polygon: 64 points to the
    /// full circle, which keeps the flattened outline within about a thousandth
    /// of a radius of the curve - far finer than the eight-pixel band a tap has
    /// to land in.
    /// </summary>
    private const double FlatteningStep = Math.PI / 32;

    private const double Epsilon = 0.000001;

    public static double CornerRadius(RectD bounds) =>
        Math.Min(Math.Abs(bounds.Width), Math.Abs(bounds.Height)) * CornerFraction;

    /// <summary>
    /// A stadium's end caps are half of its short side, so the two ends are
    /// semicircles whichever way round the box is.
    /// </summary>
    public static double StadiumRadius(RectD bounds) =>
        Math.Min(Math.Abs(bounds.Width), Math.Abs(bounds.Height)) / 2;

    public static ShapeOutline Describe(ShapeKind kind, RectD bounds) => kind switch
    {
        ShapeKind.Ellipse => Ellipse(bounds),
        ShapeKind.Triangle => Closed(
            new PointD(bounds.Center.X, bounds.Top),
            new PointD(bounds.Right, bounds.Bottom),
            new PointD(bounds.Left, bounds.Bottom)),
        ShapeKind.Pentagon => Closed(Pentagon(bounds)),
        ShapeKind.BlockArrow => Closed(BlockArrow(bounds)),
        ShapeKind.Parallelogram => Closed(Parallelogram(bounds)),
        ShapeKind.Diamond => Closed(
            new PointD(bounds.Center.X, bounds.Top),
            new PointD(bounds.Right, bounds.Center.Y),
            new PointD(bounds.Center.X, bounds.Bottom),
            new PointD(bounds.Left, bounds.Center.Y)),
        ShapeKind.Stadium => Stadium(bounds),
        _ => RoundedRectangle(bounds),
    };

    /// <summary>
    /// The outline as a closed polygon, with the curved kinds walked finely
    /// enough that a point on the curve and a point on the polygon are the same
    /// answer. The last point joins back to the first, as
    /// <see cref="Polygon"/> expects.
    /// </summary>
    public static IReadOnlyList<PointD> Outline(ShapeKind kind, RectD bounds)
    {
        ShapeOutline outline = Describe(kind, bounds);
        var points = new List<PointD> { outline.Start };
        PointD cursor = outline.Start;
        foreach (ShapeSegment segment in outline.Segments)
        {
            if (segment.Kind == ShapeSegmentKind.Arc)
            {
                AppendArc(points, cursor, segment);
            }

            points.Add(segment.End);
            cursor = segment.End;
        }

        // The final step lands back on the start, which a closed polygon does
        // not repeat.
        if (points.Count > 1 && Near(points[^1], points[0]))
        {
            points.RemoveAt(points.Count - 1);
        }

        return points;
    }

    /// <summary>
    /// Whether the point lies within <paramref name="band"/> of the outline
    /// itself. The interior is deliberately not an answer: what is drawn inside
    /// a shape has to stay reachable.
    /// </summary>
    public static bool IsOnOutline(ShapeKind kind, RectD bounds, PointD point, double band)
    {
        IReadOnlyList<PointD> outline = Outline(kind, bounds);
        var reachSquared = band * band;
        for (var index = 0; index < outline.Count; index++)
        {
            if (DistanceToSegmentSquared(
                    point,
                    outline[index],
                    outline[(index + 1) % outline.Count]) <= reachSquared)
            {
                return true;
            }
        }

        return false;
    }

    private static ShapeOutline RoundedRectangle(RectD bounds)
    {
        var radius = CornerRadius(bounds);
        if (radius <= Epsilon)
        {
            return Closed(Polygon.Corners(bounds));
        }

        PointD start = new(bounds.Left + radius, bounds.Top);
        return new ShapeOutline(
            start,
            [
                ShapeSegment.Line(new PointD(bounds.Right - radius, bounds.Top)),
                ShapeSegment.Arc(
                    new PointD(bounds.Right, bounds.Top + radius),
                    new PointD(bounds.Right - radius, bounds.Top + radius),
                    radius,
                    radius),
                ShapeSegment.Line(new PointD(bounds.Right, bounds.Bottom - radius)),
                ShapeSegment.Arc(
                    new PointD(bounds.Right - radius, bounds.Bottom),
                    new PointD(bounds.Right - radius, bounds.Bottom - radius),
                    radius,
                    radius),
                ShapeSegment.Line(new PointD(bounds.Left + radius, bounds.Bottom)),
                ShapeSegment.Arc(
                    new PointD(bounds.Left, bounds.Bottom - radius),
                    new PointD(bounds.Left + radius, bounds.Bottom - radius),
                    radius,
                    radius),
                ShapeSegment.Line(new PointD(bounds.Left, bounds.Top + radius)),
                ShapeSegment.Arc(start, new PointD(bounds.Left + radius, bounds.Top + radius), radius, radius),
            ]);
    }

    private static ShapeOutline Ellipse(RectD bounds)
    {
        var radiusX = Math.Abs(bounds.Width) / 2;
        var radiusY = Math.Abs(bounds.Height) / 2;
        PointD center = bounds.Center;
        PointD start = new(bounds.Left, center.Y);
        return new ShapeOutline(
            start,
            [
                ShapeSegment.Arc(new PointD(bounds.Right, center.Y), center, radiusX, radiusY),
                ShapeSegment.Arc(start, center, radiusX, radiusY),
            ]);
    }

    private static ShapeOutline Stadium(RectD bounds)
    {
        var radius = StadiumRadius(bounds);
        if (radius <= Epsilon)
        {
            return Closed(Polygon.Corners(bounds));
        }

        if (bounds.Width >= bounds.Height)
        {
            PointD start = new(bounds.Left + radius, bounds.Top);
            return new ShapeOutline(
                start,
                [
                    ShapeSegment.Line(new PointD(bounds.Right - radius, bounds.Top)),
                    ShapeSegment.Arc(
                        new PointD(bounds.Right - radius, bounds.Bottom),
                        new PointD(bounds.Right - radius, bounds.Center.Y),
                        radius,
                        radius),
                    ShapeSegment.Line(new PointD(bounds.Left + radius, bounds.Bottom)),
                    ShapeSegment.Arc(
                        start,
                        new PointD(bounds.Left + radius, bounds.Center.Y),
                        radius,
                        radius),
                ]);
        }

        PointD top = new(bounds.Left, bounds.Top + radius);
        return new ShapeOutline(
            top,
            [
                ShapeSegment.Arc(
                    new PointD(bounds.Right, bounds.Top + radius),
                    new PointD(bounds.Center.X, bounds.Top + radius),
                    radius,
                    radius),
                ShapeSegment.Line(new PointD(bounds.Right, bounds.Bottom - radius)),
                ShapeSegment.Arc(
                    new PointD(bounds.Left, bounds.Bottom - radius),
                    new PointD(bounds.Center.X, bounds.Bottom - radius),
                    radius,
                    radius),
                ShapeSegment.Line(top),
            ]);
    }

    /// <summary>
    /// The regular pentagon with a vertex at the top, stretched to fill the box
    /// rather than inscribed in a circle inside it: a shape drawn by dragging a
    /// rectangle should occupy the rectangle it was dragged out of.
    /// </summary>
    private static IReadOnlyList<PointD> Pentagon(RectD bounds)
    {
        PointD[] unit = new PointD[5];
        for (var index = 0; index < unit.Length; index++)
        {
            var angle = (-Math.PI / 2) + (index * 2 * Math.PI / 5);
            unit[index] = new PointD(Math.Cos(angle), Math.Sin(angle));
        }

        var minimumX = unit.Min(point => point.X);
        var minimumY = unit.Min(point => point.Y);
        var spanX = Math.Max(Epsilon, unit.Max(point => point.X) - minimumX);
        var spanY = Math.Max(Epsilon, unit.Max(point => point.Y) - minimumY);
        return unit
            .Select(point => new PointD(
                bounds.Left + ((point.X - minimumX) / spanX * bounds.Width),
                bounds.Top + ((point.Y - minimumY) / spanY * bounds.Height)))
            .ToArray();
    }

    private static IReadOnlyList<PointD> BlockArrow(RectD bounds)
    {
        var neck = bounds.Left + (bounds.Width * ArrowHeadFraction);
        var shaft = bounds.Height * ArrowShaftFraction / 2;
        return
        [
            new PointD(bounds.Left, bounds.Center.Y - shaft),
            new PointD(neck, bounds.Center.Y - shaft),
            new PointD(neck, bounds.Top),
            new PointD(bounds.Right, bounds.Center.Y),
            new PointD(neck, bounds.Bottom),
            new PointD(neck, bounds.Center.Y + shaft),
            new PointD(bounds.Left, bounds.Center.Y + shaft),
        ];
    }

    private static IReadOnlyList<PointD> Parallelogram(RectD bounds)
    {
        var slant = bounds.Width * SlantFraction;
        return
        [
            new PointD(bounds.Left + slant, bounds.Top),
            new PointD(bounds.Right, bounds.Top),
            new PointD(bounds.Right - slant, bounds.Bottom),
            new PointD(bounds.Left, bounds.Bottom),
        ];
    }

    private static ShapeOutline Closed(params PointD[] points) =>
        Closed((IReadOnlyList<PointD>)points);

    private static ShapeOutline Closed(IReadOnlyList<PointD> points) => new(
        points[0],
        [.. points.Skip(1).Select(ShapeSegment.Line), ShapeSegment.Line(points[0])]);

    /// <summary>
    /// The points between the cursor and the arc's end, exclusive of both. The
    /// board's Y grows downward, so a clockwise sweep is an increasing angle.
    /// </summary>
    private static void AppendArc(List<PointD> points, PointD from, ShapeSegment segment)
    {
        var radiusX = Math.Max(Epsilon, segment.RadiusX);
        var radiusY = Math.Max(Epsilon, segment.RadiusY);
        var start = Math.Atan2((from.Y - segment.Center.Y) / radiusY, (from.X - segment.Center.X) / radiusX);
        var end = Math.Atan2(
            (segment.End.Y - segment.Center.Y) / radiusY,
            (segment.End.X - segment.Center.X) / radiusX);
        if (segment.Clockwise)
        {
            while (end <= start)
            {
                end += 2 * Math.PI;
            }
        }
        else
        {
            while (end >= start)
            {
                end -= 2 * Math.PI;
            }
        }

        var steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(end - start) / FlatteningStep));
        for (var step = 1; step < steps; step++)
        {
            var angle = start + ((end - start) * step / steps);
            points.Add(new PointD(
                segment.Center.X + (radiusX * Math.Cos(angle)),
                segment.Center.Y + (radiusY * Math.Sin(angle))));
        }
    }

    private static bool Near(PointD first, PointD second) =>
        Math.Abs(first.X - second.X) <= Epsilon && Math.Abs(first.Y - second.Y) <= Epsilon;

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
        return DistanceSquared(point, new PointD(
            start.X + (projection * segmentX),
            start.Y + (projection * segmentY)));
    }

    private static double DistanceSquared(PointD first, PointD second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return (dx * dx) + (dy * dy);
    }
}
