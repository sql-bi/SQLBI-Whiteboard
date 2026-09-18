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
    /// Where the anchor sits on an object that has been turned: the fractions
    /// are of the rectangle before the turn, and the point they name is turned
    /// with it. That is what keeps an arrow on the corner it was dropped on
    /// while the shape it points at is stepped round.
    /// </summary>
    public static PointD PointOn(AnchorFrame frame, ConnectorAnchor anchor) =>
        frame.ToWorld(PointOn(frame.Layout, anchor));

    /// <summary>
    /// The eight points an endpoint binds to: the four corners and the four
    /// side midpoints, in reading order.
    /// </summary>
    public static IReadOnlyList<PointD> BindingPoints(RectD bounds) =>
        BindingFractions.Select(fraction => PointOn(bounds, new ConnectorAnchor(Guid.Empty, fraction.U, fraction.V)))
            .ToArray();

    /// <summary>
    /// The same eight points where a turned object actually has them, which is
    /// where the dots are drawn.
    /// </summary>
    public static IReadOnlyList<PointD> BindingPoints(AnchorFrame frame) =>
        BindingFractions.Select(fraction => PointOn(frame, new ConnectorAnchor(Guid.Empty, fraction.U, fraction.V)))
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
    /// The nearest of the eight on an object that has been turned. The answer
    /// is still a fraction of the rectangle before the turn, so the endpoint
    /// stays on that point through every later turn and resize.
    /// </summary>
    public static ConnectorAnchor NearestBindingPoint(Guid objectId, AnchorFrame frame, PointD point) =>
        NearestBindingPoint(objectId, frame.Layout, frame.ToLayout(point));

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
        PointD nearest = NearestOn(outline is { Count: > 1 } ? outline : Polygon.Corners(bounds), point);
        return new ConnectorAnchor(
            objectId,
            Fraction(nearest.X - bounds.Left, bounds.Width),
            Fraction(nearest.Y - bounds.Top, bounds.Height));
    }

    /// <summary>
    /// The same for an object that has been turned. The outline is where it is
    /// drawn, turn and all, since that is what the pointer is aiming at; the
    /// point found on it is turned back before it becomes a fraction, so the
    /// endpoint holds that place on the border through every later turn.
    /// </summary>
    public static ConnectorAnchor NearestBorderPoint(
        Guid objectId,
        AnchorFrame frame,
        IReadOnlyList<PointD>? outline,
        PointD point)
    {
        PointD nearest = frame.ToLayout(
            NearestOn(outline is { Count: > 1 } ? outline : frame.Corners(), point));
        return new ConnectorAnchor(
            objectId,
            Fraction(nearest.X - frame.Layout.Left, frame.Layout.Width),
            Fraction(nearest.Y - frame.Layout.Top, frame.Layout.Height));
    }

    /// <summary>
    /// The point on a closed border nearest this one.
    /// </summary>
    private static PointD NearestOn(IReadOnlyList<PointD> border, PointD point)
    {
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

        return nearest;
    }

    /// <summary>
    /// The candidate that belongs to none of the eight dots, which is what Ctrl
    /// asks for and what a point on the border between two of them is.
    /// </summary>
    public const int NoDot = -1;

    /// <summary>
    /// What an endpoint let go here would take from this target: the anchor to
    /// record, the point the preview snaps to, and which of the eight dots is
    /// the one, so that the drag shows the answer rather than describing it.
    /// </summary>
    public readonly record struct BindingCandidate(ConnectorAnchor Anchor, PointD Point, int DotIndex);

    /// <summary>
    /// Whether the pointer counts as over this target: the rectangle its
    /// anchors are fractions of, out by the reach, rather than within the reach
    /// of one of the eight points. Being over the object is the question an
    /// arrow asks, and the middle of a shape is as plainly over it as its
    /// corner is. The point is turned back before it is asked, so a turned
    /// shape answers for where it is drawn rather than for the box around it.
    /// </summary>
    public static bool IsWithinBindingReach(AnchorFrame frame, PointD point, double reach) =>
        frame.Layout.Inflate(reach).Contains(frame.ToLayout(point));

    /// <summary>
    /// The candidate this point takes on this target. Anywhere over the target
    /// binds, so the nearest of the eight is always an answer; with
    /// <paramref name="toBorder"/> - Ctrl - it is the nearest point anywhere on
    /// the border instead, which belongs to no dot.
    /// </summary>
    public static BindingCandidate BindingCandidateFor(
        Guid objectId,
        AnchorFrame frame,
        IReadOnlyList<PointD>? outline,
        PointD point,
        bool toBorder)
    {
        if (toBorder)
        {
            ConnectorAnchor border = NearestBorderPoint(objectId, frame, outline, point);
            return new BindingCandidate(border, PointOn(frame, border), NoDot);
        }

        ConnectorAnchor nearest = NearestBindingPoint(objectId, frame, point);
        return new BindingCandidate(nearest, PointOn(frame, nearest), DotIndexOf(nearest));
    }

    /// <summary>
    /// The anchor of a side's middle: one of the eight, named by the side it is
    /// on rather than by its place in the list, which is how a connector handle
    /// asks for the point it was pulled out of.
    /// </summary>
    public static ConnectorAnchor SideAnchor(Guid objectId, FrameSide side) => side switch
    {
        FrameSide.Top => new ConnectorAnchor(objectId, 0.5, 0),
        FrameSide.Right => new ConnectorAnchor(objectId, 1, 0.5),
        FrameSide.Bottom => new ConnectorAnchor(objectId, 0.5, 1),
        _ => new ConnectorAnchor(objectId, 0, 0.5),
    };

    /// <summary>
    /// What a drag out of a shape's connector handle records: the start stays
    /// on the side it came from, whatever the hand does afterwards, and the end
    /// takes whatever it was let go over, or nothing at all when it was let go
    /// over nothing and stays where the hand put it.
    /// </summary>
    public static (ConnectorAnchor Start, ConnectorAnchor? End) ConnectorHandleAnchors(
        Guid shapeId,
        FrameSide side,
        (Guid Id, AnchorFrame Frame, IReadOnlyList<PointD>? Outline)? target,
        PointD dropPoint,
        bool toBorder) => (
        SideAnchor(shapeId, side),
        target is { } found
            ? BindingCandidateFor(found.Id, found.Frame, found.Outline, dropPoint, toBorder).Anchor
            : null);

    /// <summary>
    /// Which of the eight this anchor is, or <see cref="NoDot"/> when it is not
    /// one of them.
    /// </summary>
    public static int DotIndexOf(ConnectorAnchor anchor)
    {
        for (var index = 0; index < BindingFractions.Length; index++)
        {
            if (Math.Abs(BindingFractions[index].U - anchor.U) <= Epsilon &&
                Math.Abs(BindingFractions[index].V - anchor.V) <= Epsilon)
            {
                return index;
            }
        }

        return NoDot;
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
        ConnectorAnchor? endAnchor = null) =>
        Controls(kind, start, end, startAnchor, endAnchor) is { } controls
            ? Flatten(start, controls.First, controls.Second, end)
            : [start, end];

    /// <summary>
    /// The two control points of a curved connector's cubic, or nothing for a
    /// straight one. A writer that draws curves asks for these rather than for
    /// the flattened polyline, so the curve leaves the board and arrives in a
    /// deck as the same cubic.
    /// </summary>
    public static (PointD First, PointD Second)? Controls(
        ConnectorKind kind,
        PointD start,
        PointD end,
        ConnectorAnchor? startAnchor = null,
        ConnectorAnchor? endAnchor = null)
    {
        if (kind != ConnectorKind.CurvedArrow)
        {
            return null;
        }

        var reach = Math.Max(1, Distance(start, end)) * CurveControlFraction;
        PointD startDirection = LeaveDirection(startAnchor, start, end);
        PointD endDirection = LeaveDirection(endAnchor, end, start);
        return (
            new PointD(start.X + (startDirection.X * reach), start.Y + (startDirection.Y * reach)),
            new PointD(end.X + (endDirection.X * reach), end.Y + (endDirection.Y * reach)));
    }

    /// <summary>
    /// The cubic as the polyline everything reads, at <see cref="CurveSegments"/>
    /// steps. Public because the same flattening is what a page draws, and a
    /// second one would be a second answer.
    /// </summary>
    public static IReadOnlyList<PointD> Flatten(PointD start, PointD firstControl, PointD secondControl, PointD end)
    {
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
