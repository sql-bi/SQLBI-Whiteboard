using SQLBI.Whiteboard.Core.Geometry;
using SQLBI.Whiteboard.Core.Settings;

namespace SQLBI.Whiteboard.Core.Model;

/// <summary>
/// Everything the board retains. The four questions a gesture asks of an
/// object - move it there, put it at that depth, are you under this point, does
/// this area take you - are answered by the object rather than by a switch in
/// the window, so a new kind of object is a new record and nothing else.
/// </summary>
public abstract record BoardObject(Guid Id, int ZIndex, RectD Bounds)
{
    /// <summary>
    /// Screen pixels, divided by the zoom when hit testing, so what a tap
    /// reaches around a line stays the same size under the pen at any zoom.
    /// </summary>
    public const double HitBand = 8;

    public abstract BoardObject WithBounds(RectD bounds);

    public abstract BoardObject WithZIndex(int zIndex);

    /// <summary>
    /// Whether a select gesture at this world point takes hold of this object.
    /// </summary>
    public abstract bool HitTest(PointD worldPoint, double zoom);

    /// <summary>
    /// The rectangle a connector's anchors are fractions of. For everything
    /// upright it is the box the document indexes; an object that has been
    /// turned hands over the rectangle before the turn and its angle, so an
    /// endpoint bound to one of its corners stays on that corner.
    /// </summary>
    public virtual AnchorFrame AnchorFrame => new(Bounds, 0);

    /// <summary>
    /// Whether an area gesture can take this object at all. A frame is a guide
    /// the author draws around other things, so a band drawn over one would
    /// otherwise pick up the slide along with its contents.
    /// </summary>
    public virtual bool IsAreaSelectable => true;

    /// <summary>
    /// Whether the area takes this object under <paramref name="rule"/>. The
    /// box is the whole answer for everything the area sees as a rectangle;
    /// a stroke has its own, because its box is mostly empty.
    /// </summary>
    public virtual bool IsTakenBy(SelectionArea area, AreaSelection rule)
    {
        ArgumentNullException.ThrowIfNull(area);
        return rule == AreaSelection.FullyInside
            ? area.ContainsRectangle(Bounds)
            : area.IntersectsRectangle(Bounds);
    }
}

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
            if (Polygon.RectangleIntersectsSegment(
                    contactBounds,
                    Points[index - 1].Position,
                    Points[index].Position))
            {
                return true;
            }
        }

        return false;
    }

    public override BoardObject WithBounds(RectD bounds) => TransformWithContainer(Bounds, bounds);

    public override BoardObject WithZIndex(int zIndex) => this with { ZIndex = zIndex };

    public override bool HitTest(PointD worldPoint, double zoom) =>
        HitTestWithin(worldPoint, HitBand / Math.Max(zoom, 0.000001));

    /// <summary>
    /// A stroke is a line through a mostly empty box, so an area asks its
    /// points and the segments between them rather than the box.
    /// </summary>
    public override bool IsTakenBy(SelectionArea area, AreaSelection rule)
    {
        ArgumentNullException.ThrowIfNull(area);
        if (rule == AreaSelection.FullyInside)
        {
            return Points.All(point => area.Contains(point.Position));
        }

        if (!area.IntersectsRectangle(Bounds))
        {
            return false;
        }

        if (Points.Any(point => area.Contains(point.Position)))
        {
            return true;
        }

        for (var index = 1; index < Points.Count; index++)
        {
            if (area.IntersectsSegment(Points[index - 1].Position, Points[index].Position))
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

    /// <summary>
    /// The stroke turned about that point. Ink linked to a shape or a label is
    /// carried round when its container is turned, the way it is carried along
    /// when the container moves. A turn changes nothing about the pen, so the
    /// thickness is left as it is and only the box is worked out again.
    /// </summary>
    public InkStrokeObject Rotate(PointD center, double degrees)
    {
        if (degrees == 0 || !double.IsFinite(degrees))
        {
            return this;
        }

        return Create(
            Points.Select(point => point with
            {
                Position = RotatedRectangle.Rotate(point.Position - center, degrees) + center,
            }),
            Style,
            ZIndex,
            Id,
            ContainerId);
    }

    /// <summary>
    /// Whether the stroke passes within <paramref name="radius"/> of the point.
    /// The eraser's reach and a select tap are the same question at two
    /// different radii.
    /// </summary>
    public bool HitTestWithin(PointD point, double radius)
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
}

public sealed record ImageBoardObject(
    Guid Id,
    int ZIndex,
    RectD Bounds,
    string AssetId) : BoardObject(Id, ZIndex, Bounds), IBoardContainer
{
    public override BoardObject WithBounds(RectD bounds) => this with { Bounds = bounds };

    public override BoardObject WithZIndex(int zIndex) => this with { ZIndex = zIndex };

    public override bool HitTest(PointD worldPoint, double zoom) => Bounds.Contains(worldPoint);
}

public static class TextLanguageIds
{
    public const string Plain = "plain";
    public const string Prompt = "prompt";
    public const string Markdown = "markdown";
    public const string Dax = "dax";
    public const string SqlServer = "sqlserver";
    public const string Kql = "kql";
    public const string Python = "python";
    public const string C = "c";
    public const string Cpp = "cpp";
    public const string Java = "java";
    public const string CSharp = "csharp";
    public const string JavaScript = "javascript";
    public const string TypeScript = "typescript";
    public const string VbNet = "vbnet";
    public const string R = "r";
    public const string Rust = "rust";
    public const string Php = "php";

    /// <summary>
    /// Every language a text container can be set to, in the order the selector
    /// offers them. Choosing a language is not the same as recognizing one: all
    /// of these are saved and restored, and only <see cref="DetectionOrder"/>
    /// ever claims a paste of its own.
    /// </summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Plain, Prompt, Markdown, Dax, SqlServer, Kql,
        Python, C, Cpp, Java, CSharp, JavaScript, TypeScript, VbNet, R, Rust, Php,
    ];

    /// <summary>
    /// The languages that can recognize a snippet, in the default snippet
    /// format order, which is also the order a language missing from a saved
    /// order joins it in. Plain text accepts everything, so it comes last: a
    /// paste is code if any language says it is, and text otherwise, with no
    /// setting to change.
    /// </summary>
    public static IReadOnlyList<string> DetectionOrder { get; } = [Dax, SqlServer, Kql, Markdown, Plain];

    /// <summary>
    /// Whether the language can claim a paste. The rest are chosen by hand, so
    /// they take no part in the snippet format order.
    /// </summary>
    public static bool CanDetect(string? languageId) =>
        Known(languageId) is { } id && DetectionOrder.Contains(id, StringComparer.Ordinal);

    public static string Normalize(string? languageId) => Known(languageId) ?? Plain;

    /// <summary>
    /// The default orders earlier releases wrote into settings, with plain text
    /// first. A saved order equal to one of these was never chosen by anyone, so
    /// an upgrade may replace it with <see cref="DetectionOrder"/>.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> LegacyDefaultOrders { get; } =
    [
        [Plain, Dax, SqlServer],
        [Plain, Dax, SqlServer, Kql],
    ];

    public static bool IsLegacyDefaultOrder(IEnumerable<string>? languageIds)
    {
        if (languageIds is null)
        {
            return false;
        }

        var order = languageIds.Select(Normalize).ToArray();
        return LegacyDefaultOrders.Any(legacy => legacy.SequenceEqual(order, StringComparer.Ordinal));
    }

    /// <summary>
    /// Drops unknown names and duplicates, and adds the languages the saved
    /// order does not know. Those go in front of plain text wherever it sits,
    /// because an order that puts plain text after some languages means "try
    /// the languages first"; only an order that starts with plain text means
    /// "keep my pastes plain", and there they go last. A language that is only
    /// ever chosen by hand is skipped rather than read as plain text, which
    /// would move plain text up an order it was never part of.
    /// </summary>
    public static IReadOnlyList<string> NormalizeOrder(IEnumerable<string>? languageIds)
    {
        var ordered = new List<string>();
        if (languageIds is not null)
        {
            foreach (var languageId in languageIds)
            {
                if (Known(languageId) is { } known &&
                    !DetectionOrder.Contains(known, StringComparer.Ordinal))
                {
                    continue;
                }

                var normalized = Normalize(languageId);
                if (!ordered.Contains(normalized, StringComparer.Ordinal))
                {
                    ordered.Add(normalized);
                }
            }
        }

        var missing = DetectionOrder.Where(languageId => !ordered.Contains(languageId, StringComparer.Ordinal)).ToArray();
        var plainIndex = ordered.IndexOf(Plain);
        if (plainIndex > 0)
        {
            ordered.InsertRange(plainIndex, missing);
        }
        else
        {
            ordered.AddRange(missing);
        }

        return ordered;
    }

    /// <summary>
    /// The issue collecting votes for formatting and automatic detection of a
    /// language Whiteboard only colors. The languages that already format have
    /// none, and neither has plain text: F6 answers those itself.
    /// </summary>
    public static string? FormattingRequestUrl(string? languageId) => Normalize(languageId) switch
    {
        Python => Issue(108),
        C => Issue(109),
        Cpp => Issue(110),
        Java => Issue(111),
        CSharp => Issue(112),
        JavaScript => Issue(113),
        TypeScript => Issue(114),
        VbNet => Issue(115),
        R => Issue(116),
        Rust => Issue(117),
        Php => Issue(118),
        _ => null,
    };

    private static string Issue(int number) =>
        $"https://github.com/sql-bi/SQLBI-Whiteboard/issues/{number}";

    private static string? Known(string? languageId) =>
        languageId?.Trim().ToLowerInvariant() is { } id && All.Contains(id, StringComparer.Ordinal)
            ? id
            : null;
}

public record TextBoardObject(
    Guid Id,
    int ZIndex,
    RectD Bounds,
    string Title,
    string Text,
    double VisualScale = 1,
    string LanguageId = TextLanguageIds.Plain) : BoardObject(Id, ZIndex, Bounds), IBoardContainer
{
    /// <summary>
    /// The text scales with the box, as it does when the corner handle is
    /// dragged: a container that changed size without its text changing with it
    /// would reflow, and reflowing is what the width handle is for.
    /// </summary>
    public override BoardObject WithBounds(RectD bounds) => this with
    {
        Bounds = bounds,
        VisualScale = VisualScale * (bounds.Width / Math.Max(0.000001, Bounds.Width)),
    };

    public override BoardObject WithZIndex(int zIndex) => this with { ZIndex = zIndex };

    public override bool HitTest(PointD worldPoint, double zoom) => Bounds.Contains(worldPoint);
}

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
    bool IsFrozen = false) : BoardObject(Id, ZIndex, Bounds), IBoardContainer
{
    public override BoardObject WithBounds(RectD bounds) => this with { Bounds = bounds };

    public override BoardObject WithZIndex(int zIndex) => this with { ZIndex = zIndex };

    public override bool HitTest(PointD worldPoint, double zoom) => Bounds.Contains(worldPoint);
}

/// <summary>
/// A rectangle the author draws to say "this is a slide". It is not a container:
/// strokes never link to it, and it never takes part in the single-container
/// test, so a frame around a picture does not stop ink from linking to the
/// picture. It is selected by its edge or its title tab, not by its inside,
/// which is how the things inside it stay reachable.
/// </summary>
public sealed record FrameBoardObject(
    Guid Id,
    int ZIndex,
    RectD Bounds,
    string Title) : BoardObject(Id, ZIndex, Bounds)
{
    /// <summary>
    /// Screen pixels, divided by the zoom when hit testing, so the band and the
    /// tab stay the same size under the pen whatever the zoom.
    /// </summary>
    public const double EdgeBand = 8;
    public const double TabWidth = 160;
    public const double TabHeight = 22;

    public RectD TabRect(double zoom) => new(
        Bounds.Left,
        Bounds.Top,
        Math.Min(Bounds.Width, TabWidth / Math.Max(zoom, 0.000001)),
        Math.Min(Bounds.Height, TabHeight / Math.Max(zoom, 0.000001)));

    public override bool HitTest(PointD worldPoint, double zoom)
    {
        var band = EdgeBand / Math.Max(zoom, 0.000001);
        if (!Bounds.Inflate(band).Contains(worldPoint))
        {
            return false;
        }

        return !Bounds.Inflate(-band).Contains(worldPoint) || TabRect(zoom).Contains(worldPoint);
    }

    public override BoardObject WithBounds(RectD bounds) => this with { Bounds = bounds };

    public override BoardObject WithZIndex(int zIndex) => this with { ZIndex = zIndex };

    /// <summary>
    /// A band drawn over a slide means the things on it, not the slide.
    /// </summary>
    public override bool IsAreaSelectable => false;
}

/// <summary>
/// The eight shapes, in the order the Insert row and the toolbar flyout offer
/// them. The first is a rectangle with rounded corners rather than a square.
/// </summary>
public enum ShapeKind
{
    RoundedRectangle,
    Ellipse,
    Triangle,
    Pentagon,
    BlockArrow,
    Parallelogram,
    Diamond,
    Stadium,
}

/// <summary>
/// A drawn shape. It is a container, so ink that touches only this one links to
/// it and moves with it - and it is taken hold of by its outline alone, never by
/// its interior, so whatever is drawn inside it stays reachable. A shape that
/// has been turned keeps the box it was drawn in as its layout size, the way a
/// label keeps the size its text measured: <see cref="BoardObject.Bounds"/> is
/// then the axis-aligned box of that rectangle once it is turned about its
/// centre, which is what the document indexes and what an export reads.
///
/// A shape also carries its own text, written in the same properties a label
/// has and starting empty. The text has no size of its own: it is laid out
/// inside <see cref="ShapeGeometry.TextBox"/> of the box the shape was drawn in
/// and turned with the shape, so nothing here has to be measured and a shape
/// that says nothing costs nothing.
/// </summary>
public sealed record ShapeBoardObject(
    Guid Id,
    int ZIndex,
    RectD Bounds,
    ShapeKind Kind,
    uint OutlineArgb,
    uint? FillArgb,
    double Thickness,
    double AngleDegrees,
    double LayoutWidth,
    double LayoutHeight,
    string Text = "",
    string FontFamily = LabelStyles.DefaultFontFamily,
    double FontSize = LabelStyles.DefaultFontSize,
    uint TextArgb = LabelStyles.DefaultArgb,
    bool Bold = false,
    bool Italic = false,
    bool Underline = false) : BoardObject(Id, ZIndex, Bounds), IBoardContainer
{
    /// <summary>
    /// What a shape is drawn with when nothing says otherwise, and what a saved
    /// thickness that makes no sense falls back to.
    /// </summary>
    public const double DefaultThickness = 4;

    private const double Epsilon = 0.000001;

    /// <summary>
    /// A shape in the box it was dragged out of, upright unless a file says
    /// otherwise.
    /// </summary>
    public static ShapeBoardObject Create(
        Guid id,
        int zIndex,
        RectD bounds,
        ShapeKind kind,
        uint outlineArgb,
        uint? fillArgb,
        double thickness,
        double angleDegrees = 0)
    {
        var angle = RotatedRectangle.NormalizeAngle(angleDegrees);
        var width = Math.Max(1, bounds.Width);
        var height = Math.Max(1, bounds.Height);
        return new ShapeBoardObject(
            id,
            zIndex,
            angle == 0 ? bounds : RotatedRectangle.Bounds(bounds.Center, width, height, angle),
            kind,
            outlineArgb,
            fillArgb,
            thickness,
            angle,
            width,
            height);
    }

    /// <summary>
    /// The box the outline is described in: the one the shape was drawn in,
    /// centred where the shape now is, before the turn.
    /// </summary>
    public RectD LayoutBounds => AngleDegrees == 0
        ? Bounds
        : new RectD(
            Bounds.Center.X - (LayoutWidth / 2),
            Bounds.Center.Y - (LayoutHeight / 2),
            LayoutWidth,
            LayoutHeight);

    public override AnchorFrame AnchorFrame => new(LayoutBounds, AngleDegrees);

    /// <summary>
    /// Where the shape's own text is laid out, in the box before the turn. What
    /// draws it turns it with the shape, as it turns the outline.
    /// </summary>
    public RectD TextBounds => ShapeGeometry.TextBox(Kind, LayoutBounds);

    /// <summary>
    /// The corner handle changes the size, and a shape is the one container
    /// allowed to change proportion under it. What the box is asked for is read
    /// along the shape's own axes, so a shape turned by a quarter grows sideways
    /// when the handle is pulled sideways. A shape standing on a corner cannot
    /// tell its two axes apart - either of them widens the box by the same
    /// amount - so it takes the two factors as one. The text goes up and down
    /// with the shape only when both axes take the same factor: a shape pulled
    /// wider is a shape with more room, and its words reflow at the size they
    /// were written in.
    /// </summary>
    public override BoardObject WithBounds(RectD bounds)
    {
        var horizontal = Factor(bounds.Width, Bounds.Width);
        var vertical = Factor(bounds.Height, Bounds.Height);
        var fontSize = FontSize * (Math.Abs(horizontal - vertical) <= horizontal * Epsilon ? horizontal : 1);
        if (AngleDegrees == 0)
        {
            return this with
            {
                Bounds = bounds,
                LayoutWidth = Math.Max(1, bounds.Width),
                LayoutHeight = Math.Max(1, bounds.Height),
                FontSize = fontSize,
            };
        }

        var radians = AngleDegrees * Math.PI / 180;
        double width;
        double height;
        if (Math.Abs(Math.Cos(radians)) > 1 - Epsilon)
        {
            width = LayoutWidth * horizontal;
            height = LayoutHeight * vertical;
        }
        else if (Math.Abs(Math.Sin(radians)) > 1 - Epsilon)
        {
            width = LayoutWidth * vertical;
            height = LayoutHeight * horizontal;
        }
        else
        {
            var together = Math.Sqrt(horizontal * vertical);
            width = LayoutWidth * together;
            height = LayoutHeight * together;
        }

        width = Math.Max(1, width);
        height = Math.Max(1, height);
        return this with
        {
            Bounds = RotatedRectangle.Bounds(bounds.Center, width, height, AngleDegrees),
            LayoutWidth = width,
            LayoutHeight = height,
            FontSize = fontSize,
        };
    }

    /// <summary>
    /// Turned about its centre, which is what keeps a shape where it is while
    /// the property bar steps it round.
    /// </summary>
    public ShapeBoardObject WithAngle(double angleDegrees)
    {
        var angle = RotatedRectangle.NormalizeAngle(angleDegrees);
        return this with
        {
            Bounds = RotatedRectangle.Bounds(Bounds.Center, LayoutWidth, LayoutHeight, angle),
            AngleDegrees = angle,
        };
    }

    public override BoardObject WithZIndex(int zIndex) => this with { ZIndex = zIndex };

    /// <summary>
    /// The outline where it is drawn: described in the box before the turn, and
    /// turned about the centre point by point.
    /// </summary>
    public IReadOnlyList<PointD> Outline()
    {
        IReadOnlyList<PointD> outline = ShapeGeometry.Outline(Kind, LayoutBounds);
        if (AngleDegrees == 0)
        {
            return outline;
        }

        AnchorFrame frame = AnchorFrame;
        return outline.Select(frame.ToWorld).ToArray();
    }

    /// <summary>
    /// The four corners of the box the shape was drawn in, where they are now,
    /// which is what the selection outline follows.
    /// </summary>
    public IReadOnlyList<PointD> Corners() =>
        RotatedRectangle.Corners(Bounds.Center, LayoutWidth, LayoutHeight, AngleDegrees);

    /// <summary>
    /// The band along the outline. The point is turned back rather than the
    /// outline turned forward, since the outline is described in the box before
    /// the turn either way.
    /// </summary>
    public override bool HitTest(PointD worldPoint, double zoom)
    {
        var band = HitBand / Math.Max(zoom, 0.000001);
        return Bounds.Inflate(band).Contains(worldPoint) &&
               ShapeGeometry.IsOnOutline(Kind, LayoutBounds, AnchorFrame.ToLayout(worldPoint), band);
    }

    private static double Factor(double after, double before)
    {
        var factor = after / Math.Max(Epsilon, before);
        return double.IsFinite(factor) && factor > 0 ? factor : 1;
    }

    /// <summary>
    /// The outline answers the area too, so a band that crosses a circle's
    /// corner of empty box does not take it while one that crosses the curve
    /// does.
    /// </summary>
    public override bool IsTakenBy(SelectionArea area, AreaSelection rule)
    {
        ArgumentNullException.ThrowIfNull(area);
        IReadOnlyList<PointD> outline = Outline();
        if (rule == AreaSelection.FullyInside)
        {
            return outline.All(area.Contains);
        }

        if (!area.IntersectsRectangle(Bounds))
        {
            return false;
        }

        if (outline.Any(area.Contains))
        {
            return true;
        }

        for (var index = 0; index < outline.Count; index++)
        {
            if (area.IntersectsSegment(outline[index], outline[(index + 1) % outline.Count]))
            {
                return true;
            }
        }

        return false;
    }
}


/// <summary>
/// Text written straight onto the board, at an angle if it was turned. What is
/// stored is the text, how it is written, and the size the layout took when it
/// was last measured: Core never measures text, so the window writes the layout
/// size in and everything here follows from it. <see cref="BoardObject.Bounds"/>
/// is the axis-aligned box of that layout rectangle once it is turned about its
/// centre, which is what the document indexes and what an export reads.
/// </summary>
public sealed record FreeTextBoardObject(
    Guid Id,
    int ZIndex,
    RectD Bounds,
    string Text,
    string FontFamily,
    double FontSize,
    uint Argb,
    bool Bold,
    bool Italic,
    bool Underline,
    double AngleDegrees,
    double LayoutWidth,
    double LayoutHeight) : BoardObject(Id, ZIndex, Bounds), IBoardContainer
{
    public static FreeTextBoardObject Create(
        Guid id,
        int zIndex,
        PointD center,
        string text,
        string fontFamily,
        double fontSize,
        uint argb,
        bool bold,
        bool italic,
        bool underline,
        double angleDegrees,
        double layoutWidth,
        double layoutHeight)
    {
        var angle = RotatedRectangle.NormalizeAngle(angleDegrees);
        var width = Math.Max(1, layoutWidth);
        var height = Math.Max(1, layoutHeight);
        return new FreeTextBoardObject(
            id,
            zIndex,
            RotatedRectangle.Bounds(center, width, height, angle),
            text,
            fontFamily,
            fontSize,
            argb,
            bold,
            italic,
            underline,
            angle,
            width,
            height);
    }

    /// <summary>
    /// The text and the size it now measures, with the top-left corner of the
    /// layout left where it was: text grows away from where it was started
    /// rather than pushing outwards from the middle.
    /// </summary>
    public FreeTextBoardObject WithLayout(string text, double layoutWidth, double layoutHeight)
    {
        var width = Math.Max(1, layoutWidth);
        var height = Math.Max(1, layoutHeight);
        PointD topLeft = RotatedRectangle.TopLeft(
            Bounds.Center,
            LayoutWidth,
            LayoutHeight,
            AngleDegrees);
        PointD center = RotatedRectangle.CenterFromTopLeft(topLeft, width, height, AngleDegrees);
        return this with
        {
            Bounds = RotatedRectangle.Bounds(center, width, height, AngleDegrees),
            Text = text,
            LayoutWidth = width,
            LayoutHeight = height,
        };
    }

    /// <summary>
    /// Turned about its centre, which is what keeps a label where it is while
    /// the property bar steps it round.
    /// </summary>
    public FreeTextBoardObject WithAngle(double angleDegrees)
    {
        var angle = RotatedRectangle.NormalizeAngle(angleDegrees);
        return this with
        {
            Bounds = RotatedRectangle.Bounds(Bounds.Center, LayoutWidth, LayoutHeight, angle),
            AngleDegrees = angle,
        };
    }

    public IReadOnlyList<PointD> Corners() =>
        RotatedRectangle.Corners(Bounds.Center, LayoutWidth, LayoutHeight, AngleDegrees);

    /// <summary>
    /// A label answers for its anchors the way a shape does: on the rectangle
    /// it is drawn in rather than on the box around it, so an arrow dropped on
    /// the corner of a turned label lands on the corner a reader sees.
    /// </summary>
    public override AnchorFrame AnchorFrame => new(
        AngleDegrees == 0
            ? Bounds
            : new RectD(
                Bounds.Center.X - (LayoutWidth / 2),
                Bounds.Center.Y - (LayoutHeight / 2),
                LayoutWidth,
                LayoutHeight),
        AngleDegrees);

    /// <summary>
    /// The corner handle scales the text rather than the box: the font and the
    /// layout take the same factor, and the box is what they come to. A move
    /// leaves the size alone, since the factor is then one.
    /// </summary>
    public override BoardObject WithBounds(RectD bounds)
    {
        var scale = bounds.Width / Math.Max(0.000001, Bounds.Width);
        if (!double.IsFinite(scale) || scale <= 0)
        {
            scale = 1;
        }

        var width = Math.Max(1, LayoutWidth * scale);
        var height = Math.Max(1, LayoutHeight * scale);
        return this with
        {
            Bounds = RotatedRectangle.Bounds(bounds.Center, width, height, AngleDegrees),
            FontSize = FontSize * scale,
            LayoutWidth = width,
            LayoutHeight = height,
        };
    }

    public override BoardObject WithZIndex(int zIndex) => this with { ZIndex = zIndex };

    /// <summary>
    /// Anywhere on the rotated rectangle, so a label is taken hold of where it
    /// looks like it is rather than anywhere in the box around it.
    /// </summary>
    public override bool HitTest(PointD worldPoint, double zoom) =>
        RotatedRectangle.Contains(Bounds.Center, LayoutWidth, LayoutHeight, AngleDegrees, worldPoint);

    public override bool IsTakenBy(SelectionArea area, AreaSelection rule)
    {
        ArgumentNullException.ThrowIfNull(area);
        IReadOnlyList<PointD> corners = Corners();
        if (rule == AreaSelection.FullyInside)
        {
            return corners.All(area.Contains);
        }

        if (!area.IntersectsRectangle(Bounds))
        {
            return false;
        }

        if (corners.Any(area.Contains))
        {
            return true;
        }

        for (var index = 0; index < corners.Count; index++)
        {
            if (area.IntersectsSegment(corners[index], corners[(index + 1) % corners.Count]))
            {
                return true;
            }
        }

        // An area drawn entirely within the label still takes it, which the
        // edges alone cannot say.
        return Polygon.Contains(corners, area.Bounds.Center);
    }
}

/// <summary>
/// The three connectors, in the order the Insert row and the toolbar flyout
/// offer them.
/// </summary>
public enum ConnectorKind
{
    Line,
    Arrow,
    CurvedArrow,
}

/// <summary>
/// An endpoint tied to a point on another object's box, as a fraction of it
/// each way: a corner is 0 or 1 in both, a side midpoint has a half in one. The
/// fraction rather than the point is what is kept, so the endpoint follows the
/// object through a move, a scale, and a stretch without being recorded twice.
/// </summary>
public readonly record struct ConnectorAnchor(Guid ObjectId, double U, double V)
{
    /// <summary>
    /// The anchor as a file could have it, with a fraction outside the box
    /// brought back onto it.
    /// </summary>
    public static ConnectorAnchor Normalize(Guid objectId, double u, double v) => new(
        objectId,
        double.IsFinite(u) ? Math.Clamp(u, 0, 1) : 0,
        double.IsFinite(v) ? Math.Clamp(v, 0, 1) : 0);
}

/// <summary>
/// A line between two points, either of which may be bound to another object.
/// It is not a container: nothing links to a connector, and a connector inside a
/// shape is not part of it. What is stored is where the two ends are, so a board
/// opens with its connectors where they were drawn without anything having to
/// be worked out from the objects around them. With AutoRoute a bound end is
/// moved to the side facing the other end whenever either object moves, which is
/// what keeps an arrow between two shapes sensible as they are rearranged; Fixed
/// - the default - leaves it on the point it was dropped on.
/// </summary>
public sealed record ConnectorBoardObject(
    Guid Id,
    int ZIndex,
    RectD Bounds,
    ConnectorKind Kind,
    PointD Start,
    PointD End,
    uint Argb,
    double Thickness,
    ConnectorAnchor? StartAnchor = null,
    ConnectorAnchor? EndAnchor = null,
    bool AutoRoute = false) : BoardObject(Id, ZIndex, Bounds)
{
    /// <summary>
    /// What a connector is drawn with when nothing says otherwise, and what a
    /// saved thickness that makes no sense falls back to.
    /// </summary>
    public const double DefaultThickness = 4;

    /// <summary>
    /// How near the pointer has to be to a binding point, in screen pixels, for
    /// the eight to be offered and the nearest one taken on release. Twice the
    /// band a tap uses, because this is a drop rather than a tap: the endpoint
    /// is already where the hand put it, and the question is only whether it
    /// meant the object.
    /// </summary>
    public const double BindingReach = 16;

    /// <summary>
    /// Whether an endpoint can bind to this object. A shape or a container, and
    /// never a frame, a stroke, or another connector: those are not things an
    /// arrow points at.
    /// </summary>
    public static bool CanBind(BoardObject item) => item is IBoardContainer;

    public static ConnectorBoardObject Create(
        Guid id,
        int zIndex,
        ConnectorKind kind,
        PointD start,
        PointD end,
        uint argb,
        double thickness,
        ConnectorAnchor? startAnchor = null,
        ConnectorAnchor? endAnchor = null,
        bool autoRoute = false) => new(
        id,
        zIndex,
        ConnectorGeometry.Bounds(kind, start, end, thickness, startAnchor, endAnchor),
        kind,
        start,
        end,
        argb,
        thickness,
        startAnchor,
        endAnchor,
        autoRoute);

    /// <summary>
    /// The path the connector runs along. The frames are the ones the ends are
    /// bound to, where the caller has them: a curve bound to a turned shape
    /// leaves the side as that side now faces, and without them it leaves along
    /// the unturned box, which is what a turn puts right again on the next
    /// <see cref="Follow"/>.
    /// </summary>
    public IReadOnlyList<PointD> Polyline(AnchorFrame? startFrame = null, AnchorFrame? endFrame = null) =>
        ConnectorGeometry.Polyline(Kind, Start, End, StartAnchor, EndAnchor, startFrame, endFrame);

    public IReadOnlyList<PointD>? Arrowhead(AnchorFrame? startFrame = null, AnchorFrame? endFrame = null) =>
        ConnectorGeometry.Arrowhead(Kind, Polyline(startFrame, endFrame), Thickness);

    /// <summary>
    /// The same connector between two other points, with its box worked out
    /// again: a curve's box is the curve's, not the two ends'.
    /// </summary>
    public ConnectorBoardObject WithEndpoints(
        PointD start,
        PointD end,
        ConnectorAnchor? startAnchor,
        ConnectorAnchor? endAnchor,
        AnchorFrame? startFrame = null,
        AnchorFrame? endFrame = null) => this with
        {
            Bounds = ConnectorGeometry.Bounds(
                Kind,
                start,
                end,
                Thickness,
                startAnchor,
                endAnchor,
                startFrame,
                endFrame),
            Start = start,
            End = end,
            StartAnchor = startAnchor,
            EndAnchor = endAnchor,
        };

    public ConnectorBoardObject WithKind(ConnectorKind kind) =>
        (this with { Kind = kind }).WithEndpoints(Start, End, StartAnchor, EndAnchor);

    public ConnectorBoardObject WithThickness(double thickness) =>
        (this with { Thickness = thickness }).WithEndpoints(Start, End, StartAnchor, EndAnchor);

    /// <summary>
    /// The endpoints bound to this object, brought to where it now is. It is how
    /// a connector follows what it points at through a move, a resize, a turn,
    /// an undo, and a group gesture.
    /// </summary>
    public ConnectorBoardObject Follow(BoardObject attached)
    {
        ArgumentNullException.ThrowIfNull(attached);
        AnchorFrame frame = attached.AnchorFrame;
        PointD start = StartAnchor is { } startAnchor && startAnchor.ObjectId == attached.Id
            ? ConnectorGeometry.PointOn(frame, startAnchor)
            : Start;
        PointD end = EndAnchor is { } endAnchor && endAnchor.ObjectId == attached.Id
            ? ConnectorGeometry.PointOn(frame, endAnchor)
            : End;
        return start == Start && end == End
            ? this
            : WithEndpoints(
                start,
                end,
                StartAnchor,
                EndAnchor,
                StartAnchor?.ObjectId == attached.Id ? frame : null,
                EndAnchor?.ObjectId == attached.Id ? frame : null);
    }

    /// <summary>
    /// The anchors chosen again for the sides that now face each other, and the
    /// bound ends moved onto them. A Fixed connector - the default - is handed
    /// back as it is; only one that routes itself is asked where its ends
    /// belong. Each end is aimed at the other object's centre rather than at the
    /// other endpoint when both are bound, so the pair settles on one answer
    /// whichever end is worked out first.
    /// </summary>
    public ConnectorBoardObject Reroute(AnchorFrame? startFrame, AnchorFrame? endFrame)
    {
        if (!AutoRoute)
        {
            return this;
        }

        PointD startTowards = endFrame is { } towardsEnd ? towardsEnd.Layout.Center : End;
        PointD endTowards = startFrame is { } towardsStart ? towardsStart.Layout.Center : Start;
        ConnectorAnchor? start = StartAnchor is { } bothStart && startFrame is { } atStart
            ? ConnectorGeometry.AutoAnchor(bothStart.ObjectId, atStart, startTowards)
            : StartAnchor;
        ConnectorAnchor? end = EndAnchor is { } bothEnd && endFrame is { } atEnd
            ? ConnectorGeometry.AutoAnchor(bothEnd.ObjectId, atEnd, endTowards)
            : EndAnchor;
        PointD startPoint = start is { } movedStart && startFrame is { } startBox
            ? ConnectorGeometry.PointOn(startBox, movedStart)
            : Start;
        PointD endPoint = end is { } movedEnd && endFrame is { } endBox
            ? ConnectorGeometry.PointOn(endBox, movedEnd)
            : End;
        return start == StartAnchor && end == EndAnchor && startPoint == Start && endPoint == End
            ? this
            : WithEndpoints(startPoint, endPoint, start, end, startFrame, endFrame);
    }

    /// <summary>
    /// Freed from that object, left where it is. Deleting a shape detaches the
    /// arrows that pointed at it rather than taking them with it.
    /// </summary>
    public ConnectorBoardObject Detach(Guid objectId)
    {
        ConnectorAnchor? start = StartAnchor?.ObjectId == objectId ? null : StartAnchor;
        ConnectorAnchor? end = EndAnchor?.ObjectId == objectId ? null : EndAnchor;
        return start == StartAnchor && end == EndAnchor
            ? this
            : WithEndpoints(Start, End, start, end);
    }

    /// <summary>
    /// Freed from everything, which is what dragging a connector by its body
    /// means: it was taken away from what it joined.
    /// </summary>
    public ConnectorBoardObject Detach() =>
        StartAnchor is null && EndAnchor is null ? this : WithEndpoints(Start, End, null, null);

    /// <summary>
    /// The ends carried with the box, which is what a move or a group scale
    /// does to a connector. The anchors stay: what a gesture moved is the
    /// object the connector is bound to as well.
    /// </summary>
    public override BoardObject WithBounds(RectD bounds) => WithEndpoints(
        MapPoint(Start, Bounds, bounds),
        MapPoint(End, Bounds, bounds),
        StartAnchor,
        EndAnchor);

    public override BoardObject WithZIndex(int zIndex) => this with { ZIndex = zIndex };

    public override bool HitTest(PointD worldPoint, double zoom) => HitTest(worldPoint, zoom, null, null);

    /// <summary>
    /// The same tap against the curve as it is drawn. The board asks with the
    /// frames its ends are bound to, so what the eye sees and what the hand
    /// takes are one line rather than two.
    /// </summary>
    public bool HitTest(PointD worldPoint, double zoom, AnchorFrame? startFrame, AnchorFrame? endFrame) =>
        ConnectorGeometry.IsOnPath(
            Polyline(startFrame, endFrame),
            worldPoint,
            HitBand / Math.Max(zoom, 0.000001));

    /// <summary>
    /// The path answers the area, as a stroke's points do: the box of a curve
    /// or a diagonal is mostly empty.
    /// </summary>
    public override bool IsTakenBy(SelectionArea area, AreaSelection rule) =>
        IsTakenBy(area, rule, null, null);

    /// <summary>
    /// The same question against the curve as it is drawn, for the same reason
    /// the tap is.
    /// </summary>
    public bool IsTakenBy(
        SelectionArea area,
        AreaSelection rule,
        AnchorFrame? startFrame,
        AnchorFrame? endFrame)
    {
        ArgumentNullException.ThrowIfNull(area);
        IReadOnlyList<PointD> path = Polyline(startFrame, endFrame);
        if (rule == AreaSelection.FullyInside)
        {
            return path.All(area.Contains);
        }

        if (!area.IntersectsRectangle(Bounds))
        {
            return false;
        }

        if (path.Any(area.Contains))
        {
            return true;
        }

        for (var index = 1; index < path.Count; index++)
        {
            if (area.IntersectsSegment(path[index - 1], path[index]))
            {
                return true;
            }
        }

        return false;
    }

    private static PointD MapPoint(PointD point, RectD before, RectD after) => new(
        after.Left + ((point.X - before.Left) * (after.Width / Math.Max(0.000001, before.Width))),
        after.Top + ((point.Y - before.Top) * (after.Height / Math.Max(0.000001, before.Height))));
}

public sealed record BoardAsset(
    string Id,
    string OriginalFileName,
    string ContentType,
    byte[] Data);
