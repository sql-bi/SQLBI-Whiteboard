using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace SQLBI.Whiteboard;

internal sealed class TextClassificationColorizer : DocumentColorizingTransformer
{
    private IReadOnlyList<StyledTextSpan> _spans = [];
    private FontFamily _fontFamily = new("Segoe UI");

    public void Update(
        IReadOnlyList<StyledTextSpan> spans,
        FontFamily fontFamily)
    {
        _spans = spans;
        _fontFamily = fontFamily;
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        int lineStart = line.Offset;
        int lineEnd = line.EndOffset;
        int low = 0;
        int high = _spans.Count;
        while (low < high)
        {
            int middle = low + ((high - low) / 2);
            StyledTextSpan candidate = _spans[middle];
            if (candidate.Start + candidate.Length <= lineStart)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        for (int index = low; index < _spans.Count; index++)
        {
            StyledTextSpan span = _spans[index];
            int spanEnd = span.Start + span.Length;

            if (span.Start >= lineEnd)
            {
                break;
            }

            int start = Math.Max(lineStart, span.Start);
            int end = Math.Min(lineEnd, spanEnd);
            if (start >= end)
            {
                continue;
            }

            ChangeLinePart(start, end, element =>
            {
                element.TextRunProperties.SetForegroundBrush(span.Style.Foreground);
                element.TextRunProperties.SetTypeface(new Typeface(
                    _fontFamily,
                    span.Style.FontStyle,
                    span.Style.FontWeight,
                    FontStretches.Normal));
            });
        }
    }
}
