using System.Windows;
using SQLBI.Whiteboard.Core.Import;

namespace SQLBI.Whiteboard.Import;

/// <summary>
/// Measures imported text with the same layout the board draws it with, so an
/// imported label or container is the size it would have been if typed here.
/// </summary>
internal sealed class WpfBoardTextMeasure(double pixelsPerDip) : IBoardTextMeasure
{
    public (double Width, double Height) Label(string text, string fontFamily, double fontSize, bool bold, bool italic)
    {
        Size size = LabelVisual.Measure(text, fontFamily, fontSize, bold, italic, pixelsPerDip);
        return (size.Width, size.Height);
    }

    public double TextContainerHeight(string text, string languageId, double width) =>
        TextContainerVisual.MeasureDesiredHeight(text, width, 1, pixelsPerDip, languageId);
}
