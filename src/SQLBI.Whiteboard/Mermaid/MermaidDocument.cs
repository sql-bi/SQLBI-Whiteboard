using SQLBI.Whiteboard.Core.Model;

namespace SQLBI.Whiteboard;

internal static class MermaidDocument
{
    /// <summary>Hydrates saved drawings before the loaded board is displayed, without starting a browser.</summary>
    public static async Task RestoreAsync(BoardDocument document)
    {
        foreach (var text in MarkdownTexts(document))
            await MarkdownContent.Parse(text.Text).RestoreSnapshotsAsync(text.MermaidSnapshots);
        MermaidContainerLayout.Refresh(document);
    }

    /// <summary>
    /// Captures the board before yielding, then waits for its diagrams and fits
    /// their final heights. Later edits cannot change the saved or exported copy.
    /// </summary>
    public static async Task<BoardDocument> CaptureAsync(
        BoardDocument document, IMermaidRenderer renderer, CancellationToken cancellationToken = default)
    {
        var copy = document.Snapshot();
        foreach (var text in MarkdownTexts(copy).ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var content = MarkdownContent.Parse(text.Text);
            if (content.DiagramSources.Count == 0) continue;
            await content.PrepareDiagramsAsync(renderer, text.MermaidSnapshots).WaitAsync(cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
        MermaidContainerLayout.Refresh(copy);
        copy.ReplaceObjects(copy.Objects.OfType<TextBoardObject>().Select(text => text with
        {
            MermaidSnapshots = text.LanguageId == TextLanguageIds.Markdown
                ? MarkdownContent.Parse(text.Text).Snapshots() : [],
        }).ToArray());
        return copy;
    }

    // Ignore derived snapshot metadata and height updates when deciding whether
    // a save still represents the current board after an asynchronous render.
    public static bool Matches(BoardDocument document, BoardDocument saved) =>
        document.ShowFrames == saved.ShowFrames &&
        document.Assets.Count == saved.Assets.Count &&
        document.Assets.All(pair => saved.Assets.TryGetValue(pair.Key, out var asset) && ReferenceEquals(pair.Value, asset)) &&
        document.Objects.Count == saved.Objects.Count &&
        document.Objects.Zip(saved.Objects).All(pair => pair.First is TextBoardObject current && pair.Second is TextBoardObject written
            ? MermaidContainerLayout.Fit(current) with { MermaidSnapshots = written.MermaidSnapshots } == written
            : pair.First == pair.Second);

    private static IEnumerable<TextBoardObject> MarkdownTexts(BoardDocument document) =>
        document.Objects.OfType<TextBoardObject>().Where(text => text.LanguageId == TextLanguageIds.Markdown);
}
