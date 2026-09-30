using SQLBI.Whiteboard.Core.Geometry;
using SQLBI.Whiteboard.Core.Model;

namespace SQLBI.Whiteboard.Core.Import;

/// <summary>
/// An object read from a Microsoft Whiteboard export, in the export's canvas pixels.
/// </summary>
public abstract record MicrosoftWhiteboardItem;

/// <summary>
/// A stroke. Widths has one entry per point: the full width of the ink at that point.
/// For a highlighter that is the height of its tip, and TipWidth is how wide the tip
/// is, or 0 when only the height is known.
/// </summary>
public sealed record MicrosoftWhiteboardStroke(
    IReadOnlyList<PointD> Points,
    IReadOnlyList<double> Widths,
    uint Argb,
    PenKind Kind,
    double TipWidth = 0) : MicrosoftWhiteboardItem;

/// <summary>
/// A picture: an image, a page of an inserted document, or a sticker.
/// </summary>
public sealed record MicrosoftWhiteboardImage(
    RectD Bounds,
    byte[] Bytes,
    string ContentType,
    string Extension) : MicrosoftWhiteboardItem;

/// <summary>
/// How text is written. The family is one of the fonts a label can use. The page's
/// own family, when it names a different one, is where the text box broke its lines,
/// so the lines are found by measuring in it.
/// </summary>
public sealed record MicrosoftWhiteboardFont(
    string Family,
    double Size,
    uint Argb,
    bool Bold,
    bool Italic,
    bool Underline,
    string? PageFamily = null);

/// <summary>
/// A shape, a sticky note, or the frame of a note grid, all of which become shapes.
/// Width and height are the box the outline is drawn in before it is turned.
/// </summary>
public sealed record MicrosoftWhiteboardShape(
    PointD Center,
    double Width,
    double Height,
    double AngleDegrees,
    ShapeKind Kind,
    uint OutlineArgb,
    uint? FillArgb,
    double Thickness,
    string Text,
    MicrosoftWhiteboardFont Font) : MicrosoftWhiteboardItem;

/// <summary>
/// A text box. Its size depends on how the text measures, so it is only known when
/// the text is placed. Origin is the box's top-left corner on the canvas, and Inset,
/// WrapWidth, CenteredWidth, and the font size are in the box's own units, before
/// Scale and the turn apply.
/// </summary>
public sealed record MicrosoftWhiteboardLabel(
    PointD Origin,
    double Scale,
    double AngleDegrees,
    PointD Inset,
    string Text,
    MicrosoftWhiteboardFont Font,
    double WrapWidth,
    double? CenteredWidth) : MicrosoftWhiteboardItem;

/// <summary>
/// A line or an arrow between two points. Arrow means a head at End.
/// </summary>
public sealed record MicrosoftWhiteboardConnector(
    PointD Start,
    PointD End,
    bool Arrow,
    uint Argb,
    double Thickness) : MicrosoftWhiteboardItem;

/// <summary>
/// Content that becomes a Markdown text container: a link card or a comment thread.
/// </summary>
public sealed record MicrosoftWhiteboardTextContainer(
    PointD TopLeft,
    double Width,
    string Title,
    string Markdown) : MicrosoftWhiteboardItem;

/// <summary>
/// Measures text the way the application draws it. Core has no text layout, so the
/// window passes this in when an import is placed.
/// </summary>
public interface IBoardTextMeasure
{
    (double Width, double Height) Label(string text, string fontFamily, double fontSize, bool bold, bool italic);

    double TextContainerHeight(string text, string languageId, double width);
}
