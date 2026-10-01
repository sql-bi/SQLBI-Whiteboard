using SQLBI.Whiteboard.Core.Geometry;
using SQLBI.Whiteboard.Core.Model;
using SQLBI.Whiteboard.Core.Persistence;

namespace SQLBI.Whiteboard.Core.SmokeTests;

internal static class MermaidArchiveSmokeTests
{
    public static async Task RunAsync()
    {
        const string source = "flowchart LR\n A[Café] --> B[Answer]";
        const string svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"40\" height=\"20\"><rect width=\"40\" height=\"20\"/></svg>";
        var text = new TextBoardObject(Guid.NewGuid(), 0, new RectD(10, 20, 640, 360),
            "Markdown", "```mermaid\n" + source + "\n```", 1.5, TextLanguageIds.Markdown)
        {
            MermaidSnapshots = [new(source, svg)],
        };
        var document = new BoardDocument();
        document.AddObject(text);
        var copy = document.Snapshot();
        Assert(((TextBoardObject)copy.Objects.Single()).MermaidSnapshots == text.MermaidSnapshots,
            "A session snapshot must share immutable diagram metadata.");
        Assert(((TextBoardObject)text.WithBounds(new RectD(20, 40, 1280, 720))).MermaidSnapshots == text.MermaidSnapshots,
            "Moving and scaling must retain saved diagram metadata.");

        var restored = await RoundTrip(document);
        Assert(restored.MermaidSnapshots.SequenceEqual(text.MermaidSnapshots) &&
            restored.Text == text.Text && restored.Bounds == text.Bounds && restored.VisualScale == text.VisualScale,
            "The source, normalized SVG, and container geometry must round-trip.");
        Assert(BoardArchive.VersionFor(document) == BoardArchive.VersionBeforeFrames,
            "Optional diagram output must not require a new archive version.");

        document.ReplaceObject(text with { MermaidSnapshots = [] });
        Assert((await RoundTrip(document)).MermaidSnapshots.IsEmpty,
            "An older board without diagram snapshots must keep loading.");

        document.ReplaceObject(text with
        {
            MermaidSnapshots = [new("", svg), new(source, ""), new(source, svg), new(source, svg),
                new("flowchart LR\n C --> D", new string('x', MermaidSnapshot.MaximumSvgLength + 1))],
        });
        Assert((await RoundTrip(document)).MermaidSnapshots.SequenceEqual(text.MermaidSnapshots),
            "Empty, duplicate, and oversized snapshot metadata must be ignored.");
        document.ReplaceObject(text with
        {
            MermaidSnapshots = [.. Enumerable.Range(0, 30).Select(index => new MermaidSnapshot(source + index, svg))],
        });
        Assert((await RoundTrip(document)).MermaidSnapshots.Length == MermaidSnapshot.MaximumCount,
            "Loading must bound the number of snapshots per container.");
    }

    private static async Task<TextBoardObject> RoundTrip(BoardDocument document)
    {
        using var stream = new MemoryStream();
        await BoardArchive.SaveAsync(document, stream);
        stream.Position = 0;
        return (await BoardArchive.LoadAsync(stream)).Objects.OfType<TextBoardObject>().Single();
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
