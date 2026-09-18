namespace SQLBI.Whiteboard.Core.Geometry;

/// <summary>
/// The rectangle an object's anchors are fractions of, and the turn that
/// rectangle has taken about its own centre. An anchor stays where it was put
/// on the object - the middle of that side, that corner - however the object is
/// turned, so the fractions are read in the unturned rectangle and the point
/// they name is turned with it. An object that has no angle hands over its box
/// and nothing changes.
/// </summary>
public readonly record struct AnchorFrame(RectD Layout, double AngleDegrees)
{
    /// <summary>
    /// The world point that layout point is drawn at.
    /// </summary>
    public PointD ToWorld(PointD layoutPoint) => AngleDegrees == 0
        ? layoutPoint
        : RotatedRectangle.Rotate(layoutPoint - Layout.Center, AngleDegrees) + Layout.Center;

    /// <summary>
    /// The layout point a world point stands on, which is what a hit test and a
    /// dropped endpoint ask for: turn it back, and the unturned rectangle
    /// answers.
    /// </summary>
    public PointD ToLayout(PointD worldPoint) => AngleDegrees == 0
        ? worldPoint
        : RotatedRectangle.Rotate(worldPoint - Layout.Center, -AngleDegrees) + Layout.Center;

    public IReadOnlyList<PointD> Corners() =>
        RotatedRectangle.Corners(Layout.Center, Layout.Width, Layout.Height, AngleDegrees);

    /// <summary>
    /// Screen pixels between the top edge and the rotation handle, so the
    /// handle stands the same distance clear of the object at any zoom.
    /// </summary>
    public const double RotationHandleOffset = 24;

    /// <summary>
    /// The middle of the top side, where it is drawn: what the rotation handle
    /// is tied to.
    /// </summary>
    public PointD TopCenter() => ToWorld(new PointD(Layout.Center.X, Layout.Top));

    /// <summary>
    /// Where the rotation handle stands: clear of the middle of the top side,
    /// along the direction that is up for this object rather than up on the
    /// screen, so the handle turns with what it turns.
    /// </summary>
    public PointD RotationHandle(double zoom) =>
        TopCenter() +
        RotatedRectangle.Rotate(
            new PointD(0, -RotationHandleOffset / Math.Max(zoom, 0.000001)),
            AngleDegrees);

    /// <summary>
    /// Screen pixels between a side and the connector handle that starts an
    /// arrow from it, so the four arrows stand the same distance clear of the
    /// object at any zoom, as the rotation handle does.
    /// </summary>
    public const double ConnectorHandleOffset = 14;

    /// <summary>
    /// The middle of one side, where it is drawn.
    /// </summary>
    public PointD SideMidpoint(FrameSide side) => ToWorld(side switch
    {
        FrameSide.Top => new PointD(Layout.Center.X, Layout.Top),
        FrameSide.Right => new PointD(Layout.Right, Layout.Center.Y),
        FrameSide.Bottom => new PointD(Layout.Center.X, Layout.Bottom),
        _ => new PointD(Layout.Left, Layout.Center.Y),
    });

    /// <summary>
    /// The way a side faces, turned with the object: what is up for the object
    /// rather than up on the screen.
    /// </summary>
    public PointD OutwardNormal(FrameSide side)
    {
        PointD normal = side switch
        {
            FrameSide.Top => new PointD(0, -1),
            FrameSide.Right => new PointD(1, 0),
            FrameSide.Bottom => new PointD(0, 1),
            _ => new PointD(-1, 0),
        };

        return AngleDegrees == 0 ? normal : RotatedRectangle.Rotate(normal, AngleDegrees);
    }

    /// <summary>
    /// The four places an arrow can be pulled out of this object: clear of each
    /// side's middle, along the way that side faces, so they turn with the
    /// object and none of them ever sits on the outline it belongs to.
    /// </summary>
    public IReadOnlyList<ConnectorHandle> ConnectorHandles(double zoom)
    {
        var offset = ConnectorHandleOffset / Math.Max(zoom, 0.000001);
        return
        [
            HandleOn(FrameSide.Top, offset),
            HandleOn(FrameSide.Right, offset),
            HandleOn(FrameSide.Bottom, offset),
            HandleOn(FrameSide.Left, offset),
        ];
    }

    private ConnectorHandle HandleOn(FrameSide side, double offset)
    {
        PointD normal = OutwardNormal(side);
        return new ConnectorHandle(side, SideMidpoint(side) + (normal * offset), normal);
    }
}

/// <summary>
/// A side of an object's frame, read before the turn: the top side stays the
/// object's own top however the object is standing.
/// </summary>
public enum FrameSide
{
    Top,
    Right,
    Bottom,
    Left,
}

/// <summary>
/// One of the four arrows a selected shape offers: which side it belongs to,
/// where it is drawn, and the way it points.
/// </summary>
public readonly record struct ConnectorHandle(FrameSide Side, PointD Point, PointD Normal);
