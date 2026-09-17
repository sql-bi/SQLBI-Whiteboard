using SQLBI.Whiteboard.Core.Model;

namespace SQLBI.Whiteboard.Core.Geometry;

/// <summary>
/// Where a connector runs, and where it can be bound. One description serves
/// the renderer, the hit test, the area selection, the binding dots, and a later
/// vector export, so none of them works the curve out a second time and they
/// cannot disagree about where the line is.
/// </summary>
public static class ConnectorGeometry
{
    /// <summary>
    /// How far a curved connector's control points leave each end, as a
    /// fraction of the distance between the ends: proportional rather than
    /// absolute, so a curve keeps its look when the shapes it joins are moved
    /// apart.
    /// </summary>
    public const double CurveControlFraction = 0.4;

    /// <summary>
    /// Segments the cubic is flattened into. The polyline is what is drawn,
    /// hit tested, and boxed, so it is fine enough that the curve and the
    /// polyline are the same answer at any zoom the board offers.
    /// </summary>
    public const int CurveSegments = 32;

    /// <summary>
    /// The arrowhead, in multiples of the line's thickness. A head this much
    /// larger than the line reads as an arrow rather than as a blot at any
    /// size the four pen thicknesses give.
    /// </summary>
    public const double ArrowLengthFactor = 4;
    public const double ArrowWidthFactor = 2.5;

    private const double Epsilon = 0.000001;

    /// <summary>
    /// Where the anchor sits on that object's box now. U and V are fractions of
    /// the box, so an object that is moved, scaled, or stretched carries the
    /// endpoint with it without anything being stored a second time.
    /// </summary>
    public static PointD PointOn(RectD bounds, ConnectorAnchor anchor) => new(
        bounds.Left + (bounds.Width * anchor.U),
        bounds.Top + (bounds.Height * anchor.V));

    /// <summary>
    /// The eight points an endpoint binds to: the four corners and the four
    /// side midpoints, in reading order.
    /// </summary>
    public static IReadOnlyList<PointD> BindingPoints(RectD bounds) =>
        BindingFractions.Select(fraction => PointOn(bounds, new ConnectorAnchor(Guid.Empty, fraction.U, fraction.V)))
            .ToArray();

    /// <summary>
    /// The binding point nearest the pointer, as the anchor that will follow the
    /// object: each of U and V is 0, a half, or 1, which is what makes a bound
    /// endpoint stay on the corner or the side midpoint it was dropped on.
    /// </summary>
    public static ConnectorAnchor NearestBindingPoint(Guid objectId, RectD bounds, PointD point)
    {
        (double U, double V) best = BindingFractions[0];
        var bestDistance = double.PositiveInfinity;
        foreach ((double u, double v) in BindingFractions)
        {
            var distance = DistanceSquared(point, PointOn(bounds, new ConnectorAnchor(objectId, u, v)));
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = (u, v);
            }
        }

        return new ConnectorAnchor(objectId, best.U, best.V);
    }

    /// <summary>
    /// The nearest point anywhere on the border, which is what Ctrl asks for: a
    /// shape's own outline where there is one, the box otherwise. The answer is
    /// still expressed as a fraction of the box, so the endpoint follows a
    /// resize like any other anchor.
    /// </summary>
    public static ConnectorAnchor NearestBorderPoint(
        Guid objectId,
        RectD bounds,
        IReadOnlyList<PointD>? outline,
        PointD point)
    {
        IReadOnlyList<PointD> border = outline is { Count: > 1 } ? outline : Polygon.Corners(bounds);
        PointD nearest = border[0];
        var bestDistance = double.PositiveInfinity;
        for (var index = 0; index < border.Count; index++)
        {
            PointD candidate = ClosestOnSegment(point, border[index], border[(index + 1) % border.Count]);
            var distance = DistanceSquared(point, candidate);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                nearest = candidate;
            }
        }

        return new ConnectorAnchor(
            objectId,
            Fraction(nearest.X - bounds.Left, bounds.Width),
            Fraction(nearest.Y - bounds.Top, bounds.Height));
    }

    /// <summary>
    /// The path of the connector, as the polyline everything else reads: two
    /// points for a straight one, the flattened cubic for a curved one.
    /// </summary>
    public static IReadOnlyList<PointD> Polyline(
        ConnectorKind kind,
        PointD start,
        PointD end,
        ConnectorAnchor? startAnchor = null,
        ConnectorAnchor? endAnchor = null)
    {
        if (kind != ConnectorKind.CurvedArrow)
        {
            return [start, end];
        }

        var reach = Math.Max(1, Distance(start, end)) * CurveControlFraction;
        PointD startDirection = LeaveDirection(startAnchor, start, end);
        PointD endDirection = LeaveDirection(endAnchor, end, start);
        PointD firstControl = new(start.X + (startDirection.X * reach), start.Y + (startDirection.Y * reach));
        PointD secondControl = new(end.X + (endDirection.X * reach), end.Y + (endDirection.Y * reach));

        var points = new PointD[CurveSegments + 1];
        for (var step = 0; step <= CurveSegments; step++)
        {
            points[step] = Cubic(start, firstControl, secondControl, end, (double)step / CurveSegments);
        }

        return points;
    }

    /// <summary>
    /// The filled triangle at the end, following the last segment's tangent, or
    /// nothing at all for a plain line.
    /// </summary>
    public static IReadOnlyList<PointD>? Arrowhead(
        ConnectorKind kind,
        IReadOnlyList<PointD> polyline,
        double thickness)
    {
        ArgumentNullException.ThrowIfNull(polyline);
        if (kind == ConnectorKind.Line || polyline.Count < 2)
        {
            return null;
        }

        PointD tip = polyline[^1];
        PointD direction = Tangent(polyline);
        var length = thickness * ArrowLengthFactor;
        var half = thickness * ArrowWidthFactor / 2;
        PointD back = new(tip.X - (direction.X * length), tip.Y - (direction.Y * length));
        PointD across = new(-direction.Y, direction.X);
        return
        [
            tip,
            new PointD(back.X + (across.X * half), back.Y + (across.Y * half)),
            new PointD(back.X - (across.X * half), back.Y - (across.Y * half)),
        ];
    }

    /// <summary>
    /// The polyline as it is drawn: stopped at the base of the arrowhead, so a
    /// thick line does not poke through the tip of its own arrow.
    /// </summary>
    public static IReadOnlyList<PointD> LinePath(
        ConnectorKind kind,
        IReadOnlyList<PointD> polyline,
        double thickness)
    {
        ArgumentNullException.ThrowIfNull(polyline);
        if (kind == ConnectorKind.Line || polyline.Count < 2)
        {
            return polyline;
        }

        var remaining = thickness * ArrowLengthFactor;
        var points = polyline.ToList();
        while (points.Count > 2)
        {
            var segment = Distance(points[^2], points[^1]);
            if (segment > remaining)
            {
                break;
            }

            remaining -= segment;
            points.RemoveAt(points.Count - 1);
        }

        var last = Distance(points[^2], points[^1]);
        if (last <= Epsilon)
        {
            return points;
        }

        var keep = Math.Max(0, last - remaining) / last;
        points[^1] = new PointD(
            points[^2].X + ((points[^1].X - points[^2].X) * keep),
            points[^2].Y + ((points[^1].Y - points[^2].Y) * keep));
        return points;
    }

    /// <summary>
    /// The box the document indexes: the path and the arrowhead, out to the
    /// far edge of the line.
    /// </summary>
    public static RectD Bounds(
        ConnectorKind kind,
        PointD start,
        PointD end,
        double thickness,
        ConnectorAnchor? startAnchor = null,
        ConnectorAnchor? endAnchor = null)
    {
        IReadOnlyList<PointD> polyline = Polyline(kind, start, end, startAnchor, endAnchor);
        IReadOnlyList<PointD>? head = Arrowhead(kind, polyline, thickness);
        IEnumerable<PointD> points = head is null ? polyline : polyline.Concat(head);
        return RectD.FromPoints(points, Math.Max(0.5, thickness / 2));
    }

    /// <summary>
    /// Whether the point is within <paramref name="reach"/> of the path.
    /// </summary>
    public static bool IsOnPath(IReadOnlyList<PointD> polyline, PointD point, double reach)
    {
        ArgumentNullException.ThrowIfNull(polyline);
        var reachSquared = reach * reach;
        if (polyline.Count == 1)
        {
            return DistanceSquared(polyline[0], point) <= reachSquared;
        }

        for (var index = 1; index < polyline.Count; index++)
        {
            if (DistanceSquared(point, ClosestOnSegment(point, polyline[index - 1], polyline[index])) <= reachSquared)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The way the curve leaves this end: outwards from the side of the box the
    /// anchor sits on, the diagonal at a corner, and horizontally towards the
    /// other end when the endpoint is free. An anchor that is not on the border
    /// at all - the middle of a shape's outline, say - counts as free.
    /// </summary>
    private static PointD LeaveDirection(ConnectorAnchor? anchor, PointD from, PointD towards)
    {
        if (anchor is { } bound)
        {
            var x = bound.U <= Epsilon ? -1 : bound.U >= 1 - Epsilon ? 1 : 0;
            var y = bound.V <= Epsilon ? -1 : bound.V >= 1 - Epsilon ? 1 : 0;
            if (x != 0 || y != 0)
            {
                return Normalize(new PointD(x, y));
            }
        }

        return new PointD(towards.X >= from.X ? 1 : -1, 0);
    }

    private static PointD Tangent(IReadOnlyList<PointD> polyline)
    {
        for (var index = polyline.Count - 1; index > 0; index--)
        {
            PointD direction = polyline[index] - polyline[index - 1];
            if (Math.Abs(direction.X) > Epsilon || Math.Abs(direction.Y) > Epsilon)
            {
                return Normalize(direction);
            }
        }

        return new PointD(1, 0);
    }

    private static PointD Cubic(PointD first, PointD second, PointD third, PointD fourth, double t)
    {
        var inverse = 1 - t;
        var a = inverse * inverse * inverse;
        var b = 3 * inverse * inverse * t;
        var c = 3 * inverse * t * t;
        var d = t * t * t;
        return new PointD(
            (first.X * a) + (second.X * b) + (third.X * c) + (fourth.X * d),
            (first.Y * a) + (second.Y * b) + (third.Y * c) + (fourth.Y * d));
    }

    private static PointD Normalize(PointD direction)
    {
        var length = Math.Sqrt((direction.X * direction.X) + (direction.Y * direction.Y));
        return length <= Epsilon ? new PointD(1, 0) : new PointD(direction.X / length, direction.Y / length);
    }

    private static double Fraction(double offset, double size) =>
        size <= Epsilon ? 0 : Math.Clamp(offset / size, 0, 1);

    private static PointD ClosestOnSegment(PointD point, PointD start, PointD end)
    {
        var segmentX = end.X - start.X;
        var segmentY = end.Y - start.Y;
        var lengthSquared = (segmentX * segmentX) + (segmentY * segmentY);
        if (lengthSquared <= double.Epsilon)
        {
            return start;
        }

        var projection = Math.Clamp(
            (((point.X - start.X) * segmentX) + ((point.Y - start.Y) * segmentY)) / lengthSquared,
            0,
            1);
        return new PointD(start.X + (projection * segmentX), start.Y + (projection * segmentY));
    }

    private static double Distance(PointD first, PointD second) =>
        Math.Sqrt(DistanceSquared(first, second));

    private static double DistanceSquared(PointD first, PointD second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return (dx * dx) + (dy * dy);
    }

    /// <summary>
    /// The eight binding points as fractions of the box: the corners and the
    /// side midpoints, top-left first and clockwise.
    /// </summary>
    private static readonly (double U, double V)[] BindingFractions =
    [
        (0, 0), (0.5, 0), (1, 0), (1, 0.5), (1, 1), (0.5, 1), (0, 1), (0, 0.5),
    ];
}
