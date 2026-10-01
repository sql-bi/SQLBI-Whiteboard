using SQLBI.Whiteboard.Core.Geometry;
using SQLBI.Whiteboard.Core.Model;

namespace SQLBI.Whiteboard;

internal static class MermaidContainerLayout
{
    public static bool Refresh(BoardDocument document, Guid? editingId = null)
    {
        var replacements = document.Objects.OfType<TextBoardObject>()
            .Where(text => text.Id != editingId)
            .Select(text => (Before: text, After: Fit(text)))
            .Where(pair => pair.Before != pair.After)
            .Select(pair => pair.After).ToArray();
        if (replacements.Length == 0) return false;

        // Height is derived from the source, width, and visual scale. Reapply it
        // after undo/redo as well as rendering, without adding a history entry
        // or transforming ink when only the measured content height changed.
        document.ReplaceObjects(replacements);
        return true;
    }

    public static TextBoardObject Fit(TextBoardObject text)
    {
        if (text.LanguageId != TextLanguageIds.Markdown) return text;
        var content = MarkdownContent.Parse(text.Text);
        if (content.DiagramSources.Count == 0 || !content.DiagramsReady) return text;
        double height = TextContainerVisual.MeasureDesiredHeight(
            text.Text, text.Bounds.Width, text.VisualScale, languageId: TextLanguageIds.Markdown);
        return Math.Abs(height - text.Bounds.Height) < 0.001
            ? text
            : text with { Bounds = text.Bounds.WithSize(text.Bounds.Width, height) };
    }

    public static RectD InkBoundsAfterEdit(TextBoardObject before, TextBoardObject after)
    {
        static bool HasDiagram(TextBoardObject text) => text.LanguageId == TextLanguageIds.Markdown &&
            MarkdownContent.Parse(text.Text).DiagramSources.Count > 0;
        if (!HasDiagram(before) && !HasDiagram(after)) return after.Bounds;

        // The final height might still be unknown. A source edit must not squeeze
        // ink into the pending placeholder or depend on whether a render was cached.
        // An explicit width change in the editor still scales the linked strokes.
        double scale = after.Bounds.Width / before.Bounds.Width;
        return after.Bounds.WithSize(after.Bounds.Width, before.Bounds.Height * scale);
    }
}
