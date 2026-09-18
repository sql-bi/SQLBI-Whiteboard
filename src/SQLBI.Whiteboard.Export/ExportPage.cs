using SQLBI.Whiteboard.Core.Model;

namespace SQLBI.Whiteboard.Export;

/// <summary>
/// One slide or page: a title, optional notes, and the picture that fills it. The pixel
/// size is what the layout scales by, so the PNG itself is never decoded on the way out.
/// When <paramref name="Elements"/> is given, a slide is built from them instead of the
/// picture, in the same pixel coordinate space, and the picture is kept for a page that
/// cannot take elements.
/// </summary>
public sealed record ExportPage(
    string Title,
    string? Notes,
    byte[] Png,
    int PixelWidth,
    int PixelHeight,
    IReadOnlyList<SlideElement>? Elements = null);

/// <summary>
/// A rectangle in the page's pixel space: the same space the picture is measured in.
/// </summary>
public readonly record struct SlideRect(double X, double Y, double Width, double Height);

public abstract record SlideElement(SlideRect Bounds);

/// <summary>
/// A picture placed as a picture: an image container, a LiveView frame, or the ink
/// overlay that covers the whole page. Content type is "image/png" or "image/jpeg".
/// </summary>
public sealed record SlideImageElement(SlideRect Bounds, byte[] Data, string ContentType) : SlideElement(Bounds);

public sealed record SlideTextRun(string Text, uint Argb, bool Bold, bool Italic);

/// <summary>
/// An explicitly laid-out paragraph. Margins are in page pixels; a bullet sits
/// HangingIndent pixels to the left of the body and never becomes part of its text.
/// </summary>
public sealed record SlideTextParagraph(
    IReadOnlyList<SlideTextRun> Runs,
    double LeftMargin = 0,
    double HangingIndent = 0,
    string? Bullet = null);

/// <summary>
/// A text container as a text box: the title on its own first line, then the body as
/// runs whose colors and weights are the ones the screen shows. A run may contain line
/// breaks; each starts a new paragraph. Sizes and the padding are in page pixels.
/// When supplied, Paragraphs describes the displayed body; Runs still preserves the source.
/// </summary>
public sealed record SlideTextElement(
    SlideRect Bounds,
    string Title,
    IReadOnlyList<SlideTextRun> Runs,
    string FontFamily,
    double TitleFontSize,
    double BodyFontSize,
    double Padding,
    uint BackgroundArgb,
    uint BorderArgb,
    uint TextArgb,
    IReadOnlyList<SlideTextParagraph>? Paragraphs = null) : SlideElement(Bounds);

/// <summary>
/// A drawn shape as an object rather than as pixels: the box it fills, the kind
/// whose outline <see cref="Core.Geometry.ShapeGeometry"/> describes, and how it
/// is painted. Thickness is in page pixels, as the rectangle is; a null fill is
/// None, and a fill keeps the alpha of the tint the board shows. Bounds is the
/// box the shape was drawn in, before the turn, so a writer places the box and
/// then rotates it about its own centre, which is where the board turns it too.
/// A shape carries its own text as well, centred in the rectangle
/// <see cref="Core.Geometry.ShapeGeometry.TextBox"/> names inside that box and
/// wrapped to its width; TextMargin is the room left round it, in page pixels,
/// which is what a deck writes as the text insets of the shape. ObjectId is the
/// board object this came from: a connector bound to it names it by that, and it
/// is empty when nothing refers to it.
/// </summary>
public sealed record SlideShapeElement(
    SlideRect Bounds,
    ShapeKind Kind,
    uint OutlineArgb,
    uint? FillArgb,
    double Thickness,
    double AngleDegrees = 0,
    string Text = "",
    string FontFamily = "Segoe UI",
    double FontSize = 24,
    uint TextArgb = 0xFF1F2937,
    bool Bold = false,
    bool Italic = false,
    bool Underline = false,
    double TextMargin = 8,
    Guid ObjectId = default) : SlideElement(Bounds);

/// <summary>
/// A label as text rather than as pixels. Bounds is the layout rectangle before
/// the turn, so a writer places the rectangle and then rotates it about its own
/// centre, which is where the board turns it too. FontSize is in page pixels, and
/// the text keeps its line breaks: one line is one paragraph. ObjectId is the
/// board object this came from, as a shape's is.
/// </summary>
public sealed record SlideLabelElement(
    SlideRect Bounds,
    double AngleDegrees,
    string Text,
    string FontFamily,
    double FontSize,
    uint Argb,
    bool Bold,
    bool Italic,
    bool Underline,
    Guid ObjectId = default) : SlideElement(Bounds);

/// <summary>A point in the page's pixel space.</summary>
public readonly record struct SlidePosition(double X, double Y);

/// <summary>
/// What one end of a connector is tied to: the board object, and where on its
/// box, as the fractions the board records. A writer turns that into whatever
/// its own format calls a connection site; one that cannot leaves the end where
/// it is drawn.
/// </summary>
public readonly record struct SlideConnection(Guid ObjectId, double U, double V);

/// <summary>
/// A connector as a line rather than as pixels: where it runs, what it is drawn
/// with, and, for a curved one, the two control points of the cubic the board
/// draws, so a writer never works the curve out a second time. Bounds is the box
/// of every point named here, which is the box the curve stays inside. An Arrow
/// and a CurvedArrow carry a filled head at the end; a Line does not. Each end
/// that is bound to something says so, so a writer whose connectors re-route can
/// tie them to the shape rather than to the page.
/// </summary>
public sealed record SlideConnectorElement(
    SlideRect Bounds,
    ConnectorKind Kind,
    SlidePosition Start,
    SlidePosition End,
    SlidePosition? FirstControl,
    SlidePosition? SecondControl,
    uint Argb,
    double Thickness,
    SlideConnection? StartConnection = null,
    SlideConnection? EndConnection = null) : SlideElement(Bounds);

public enum SlideStrokeKind
{
    /// <summary>An ellipse nib whose diameter follows pressure.</summary>
    Pen = 0,

    /// <summary>A flat rectangle nib four times wider than tall, with no pressure.</summary>
    Highlighter = 1,

    /// <summary>A tall rectangle nib, three times taller than the thickness and 0.65 of it wide, following pressure.</summary>
    Calligraphy = 2,
}

public readonly record struct SlidePoint(double X, double Y, float Pressure);

/// <summary>
/// One stroke in page pixels. Thickness is the nominal width, in page pixels, at
/// the pressure WPF treats as normal (0.5): the nib is Thickness × 2 × pressure across
/// for the kinds that follow pressure. Argb keeps its alpha; a highlighter is translucent.
/// </summary>
public sealed record SlideStroke(
    IReadOnlyList<SlidePoint> Points,
    uint Argb,
    double Thickness,
    SlideStrokeKind Kind);

/// <summary>
/// Ink as vector strokes, for a writer that can draw them. The PowerPoint writer does
/// not: it takes the ink as an image element instead.
/// </summary>
public sealed record SlideInkElement(SlideRect Bounds, IReadOnlyList<SlideStroke> Strokes) : SlideElement(Bounds);

/// <summary>16:9 or 4:3.</summary>
public enum SlideAspect
{
    Wide = 0,
    Standard = 1,
}

public sealed record DeckOptions(SlideAspect Aspect = SlideAspect.Wide);
