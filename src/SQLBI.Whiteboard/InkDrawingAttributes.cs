using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using SQLBI.Whiteboard.Core.Model;

namespace SQLBI.Whiteboard;

internal static class InkDrawingAttributes
{
    public static DrawingAttributes Create(PenStyle style, double zoom)
    {
        var width = Math.Max(0.1, style.Thickness * zoom);
        var height = width;
        var stylusTip = StylusTip.Ellipse;
        var stylusTipTransform = Matrix.Identity;
        var ignorePressure = false;
        var isHighlighter = false;

        switch (style.Kind)
        {
            case PenKind.Highlighter:
                width *= 4;
                height *= 2;
                stylusTip = StylusTip.Rectangle;
                ignorePressure = true;
                isHighlighter = true;
                break;
            case PenKind.Calligraphy:
                width *= 2.4;
                height *= 0.65;
                stylusTip = StylusTip.Rectangle;
                stylusTipTransform.Rotate(35);
                break;
        }

        return new DrawingAttributes
        {
            Color = Color.FromArgb(
                (byte)(style.Argb >> 24),
                (byte)(style.Argb >> 16),
                (byte)(style.Argb >> 8),
                (byte)style.Argb),
            Width = width,
            Height = height,
            FitToCurve = false,
            IgnorePressure = ignorePressure,
            IsHighlighter = isHighlighter,
            StylusTip = stylusTip,
            StylusTipTransform = stylusTipTransform,
        };
    }
}
