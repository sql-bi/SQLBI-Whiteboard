using ICSharpCode.AvalonEdit.Rendering;
using SQLBI.Whiteboard.Core.Model;

namespace SQLBI.Whiteboard;

internal sealed class PromptBulletGenerator : VisualLineElementGenerator
{
    public bool IsEnabled { get; set; }

    public override int GetFirstInterestedOffset(int startOffset)
    {
        if (!IsEnabled)
        {
            return -1;
        }

        var line = CurrentContext.VisualLine.FirstDocumentLine;
        var prompt = PromptText.ParseLine(CurrentContext.Document.GetText(line));
        var markerOffset = line.Offset + prompt.MarkerOffset;
        return prompt.IsBullet && startOffset <= markerOffset ? markerOffset : -1;
    }

    public override VisualLineElement ConstructElement(int offset) =>
        new BulletMarker(CurrentContext.VisualLine, 1);

    // AvalonEdit inherits wrapped-line indentation by walking the visual prefix's whitespace.
    // Include the dash in that prefix without changing its text, offsets, or caret behavior.
    private sealed class BulletMarker(VisualLine line, int length) : VisualLineText(line, length)
    {
        public override bool IsWhitespace(int visualColumn) => true;

        protected override VisualLineText CreateInstance(int length) => new BulletMarker(ParentVisualLine, length);
    }
}
