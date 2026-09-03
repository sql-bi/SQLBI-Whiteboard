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
/// A text container as a text box: the title on its own first line, then the body as
/// runs whose colors and weights are the ones the screen shows. A run may contain line
/// breaks; each starts a new paragraph. Sizes and the padding are in page pixels.
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
    uint TextArgb) : SlideElement(Bounds);

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
