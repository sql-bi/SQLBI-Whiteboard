using System.Globalization;
using System.Windows;
using System.Windows.Media;
using SQLBI.Whiteboard.Core.Model;

namespace SQLBI.Whiteboard;

/// <summary>
/// How a label is measured and written. Core keeps a label's layout size but
/// never works it out: text is measured by the framework that draws it, so the
/// window measures here whenever the text, the font, the size, or the style
/// changes, and puts the answer back into the object.
/// </summary>
internal static class LabelVisual
{
    /// <summary>
    /// The size an empty label keeps, so that a label being typed into has a
    /// box to draw a caret in and a rectangle to select.
    /// </summary>
    private const string EmptyPlaceholder = " ";

    public static Size Measure(
        string text,
        string fontFamily,
        double fontSize,
        bool bold,
        bool italic,
        double pixelsPerDip)
    {
        FormattedText formatted = Format(
            text,
            fontFamily,
            fontSize,
            bold,
            italic,
            underline: false,
            argb: 0xFF000000,
            pixelsPerDip);
        return new Size(
            Math.Max(1, formatted.WidthIncludingTrailingWhitespace),
            Math.Max(1, formatted.Height));
    }

    public static Size Measure(FreeTextBoardObject label, double pixelsPerDip) =>
        Measure(label.Text, label.FontFamily, label.FontSize, label.Bold, label.Italic, pixelsPerDip);

    /// <summary>
    /// The label at a given size on screen. The size is passed in rather than
    /// taken from the label, because what is drawn is the label's size times
    /// the zoom.
    /// </summary>
    public static FormattedText Format(FreeTextBoardObject label, double fontSize, double pixelsPerDip) =>
        Format(
            label.Text,
            label.FontFamily,
            fontSize,
            label.Bold,
            label.Italic,
            label.Underline,
            label.Argb,
            pixelsPerDip);

    public static FormattedText Format(
        string text,
        string fontFamily,
        double fontSize,
        bool bold,
        bool italic,
        bool underline,
        uint argb,
        double pixelsPerDip)
    {
        var formatted = new FormattedText(
            string.IsNullOrEmpty(text) ? EmptyPlaceholder : text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            TypefaceFor(fontFamily, bold, italic),
            Math.Max(1, fontSize),
            Brush(argb),
            pixelsPerDip)
        {
            TextAlignment = TextAlignment.Left,
            Trimming = TextTrimming.None,
        };
        if (underline)
        {
            formatted.SetTextDecorations(TextDecorations.Underline);
        }

        return formatted;
    }

    public static Typeface TypefaceFor(string fontFamily, bool bold, bool italic) => new(
        new FontFamily(fontFamily),
        italic ? FontStyles.Italic : FontStyles.Normal,
        bold ? FontWeights.Bold : FontWeights.Normal,
        FontStretches.Normal);

    public static SolidColorBrush Brush(uint argb)
    {
        var brush = new SolidColorBrush(Color.FromArgb(
            (byte)(argb >> 24),
            (byte)(argb >> 16),
            (byte)(argb >> 8),
            (byte)argb));
        brush.Freeze();
        return brush;
    }
}
