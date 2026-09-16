using SQLBI.Whiteboard.Core.Geometry;

namespace SQLBI.Whiteboard.Core.Model;

/// <summary>
/// What an area gesture takes hold of.
/// </summary>
public enum AreaSelection
{
    /// <summary>
    /// Default: an object is taken as soon as its geometry meets the area.
    /// </summary>
    PartlyInside = 0,

    /// <summary>
    /// Every point of a stroke, and every corner of anything else, has to be
    /// inside.
    /// </summary>
    FullyInside = 1,
}

/// <summary>
/// Whether the selection grows to what the area caught touches.
/// </summary>
public enum ExtendSelection
{
    /// <summary>
    /// Default: the area's answer is the selection.
    /// </summary>
    Ignore = 0,

    /// <summary>
    /// One round: everything touching what the area took joins it.
    /// </summary>
    Single = 1,

    /// <summary>
    /// Rounds until one adds nothing. An object already examined is never
    /// examined again, so a ring of things that touch each other terminates.
    /// </summary>
    Recursive = 2,
}

/// <summary>
/// The shape a select gesture drew: a rubber band, or a lasso closed back to
/// where it started. Both answer the same three questions, so an object is
/// tested against either without knowing which it is.
/// </summary>
public sealed class SelectionArea
{
    private readonly IReadOnlyList<PointD> _polygon;

    private SelectionArea(RectD bounds, IReadOnlyList<PointD> polygon)
    {
        Bounds = bounds;
        _polygon = polygon;
    }

    /// <summary>
    /// The axis-aligned box of the area, which is the area itself for a
    /// rubber band and a cheap rejection for a lasso.
    /// </summary>
    public RectD Bounds { get; }

    public bool IsLasso => _polygon.Count > 0;

    public static SelectionArea Rectangle(RectD bounds) => new(Normalized(bounds), []);

    public static SelectionArea Rectangle(PointD corner, PointD opposite) =>
        Rectangle(new RectD(
            Math.Min(corner.X, opposite.X),
            Math.Min(corner.Y, opposite.Y),
            Math.Abs(opposite.X - corner.X),
            Math.Abs(opposite.Y - corner.Y)));

    public static SelectionArea Lasso(IEnumerable<PointD> points)
    {
        PointD[] path = [.. points];
        return new SelectionArea(RectD.FromPoints(path), path);
    }

    public bool Contains(PointD point) =>
        IsLasso ? Polygon.Contains(_polygon, point) : Bounds.Contains(point);

    public bool ContainsRectangle(RectD rectangle) =>
        IsLasso
            ? Polygon.ContainsRectangle(_polygon, rectangle)
            : Polygon.RectangleContainsRectangle(Bounds, rectangle);

    public bool IntersectsRectangle(RectD rectangle) =>
        IsLasso
            ? Polygon.IntersectsRectangle(_polygon, rectangle)
            : Bounds.Intersects(rectangle);

    public bool IntersectsSegment(PointD start, PointD end) =>
        IsLasso
            ? Polygon.IntersectsSegment(_polygon, start, end)
            : Polygon.RectangleIntersectsSegment(Bounds, start, end);

    private static RectD Normalized(RectD bounds) => new(
        Math.Min(bounds.Left, bounds.Right),
        Math.Min(bounds.Top, bounds.Bottom),
        Math.Abs(bounds.Width),
        Math.Abs(bounds.Height));
}
