using System.IO;
using System.IO.Compression;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SQLBI.Whiteboard.Core.Commands;
using SQLBI.Whiteboard.Core.Geometry;
using SQLBI.Whiteboard.Core.Model;
using SQLBI.Whiteboard.Core.Persistence;
using SQLBI.Whiteboard.Core.Settings;
using SQLBI.Whiteboard.Export;

namespace SQLBI.Whiteboard.SmokeTests;

internal static class MermaidPersistenceSmokeTests
{
    private const string Svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"320\" height=\"180\"><rect width=\"320\" height=\"180\" fill=\"#0000ff\"/></svg>";
    private static string Fence(string source) => "```mermaid\n" + source + "\n```";

    public static async Task RunAsync()
    {
        var document = new BoardDocument();
        var original = Text("flowchart LR\n Save --> Reopen");
        document.AddObject(original);
        var ink = InkStrokeObject.Create([new InkPoint(new PointD(60, 120), 0.6f, 1)],
            PenStyle.Default, 1, containerId: original.Id);
        document.AddObject(ink);
        var renderer = new DelayedRenderer();
        var pending = MermaidDocument.CaptureAsync(document, renderer);
        Assert(!pending.IsCompleted, "Saving/exporting must wait for pending diagrams before capturing previews or areas.");
        Assert(original.MermaidSnapshots.IsEmpty, "Preparation must not add derived output to live history records.");
        renderer.Complete();
        var prepared = await pending;
        var rendered = prepared.Objects.OfType<TextBoardObject>().Single();
        Assert(rendered.MermaidSnapshots.Length == 1 && rendered.Bounds.Height > original.Bounds.Height &&
            rendered.MermaidSnapshots[0].Svg == Svg, "Capture must retain successful SVG and fit the final natural height.");
        Assert(document.Objects.OfType<TextBoardObject>().Single() == original &&
            prepared.LinkedStrokes(original.Id).Single() == ink, "Preparing a copy must not mutate the live board or stretch linked ink.");
        Assert(MermaidDocument.Matches(document, prepared), "Derived height and snapshots alone must not mark a saved board dirty.");

        byte[] preview = BoardPreviewRenderer.Render(prepared)!;
        Assert(BluePixels(preview) > 1000, "The embedded board preview must contain the diagram, not a pending placeholder.");
        var loaded = await RoundTrip(prepared, preview);
        var unavailable = new UnavailableRenderer();
        await MermaidDocument.RestoreAsync(loaded);
        var text = loaded.Objects.OfType<TextBoardObject>().Single();
        Assert(MarkdownContent.Parse(text.Text).DiagramsReady && text.Bounds == rendered.Bounds,
            "Reopening must hydrate saved diagrams at their saved size without a browser.");
        var offline = await MermaidDocument.CaptureAsync(loaded, unavailable);
        Assert(unavailable.Calls == 0 && BoardPreviewRenderer.Render(offline)!.SequenceEqual(preview),
            "A saved board must preview and save identically with WebView2 unavailable.");
        await CheckExports(offline);
        CheckHistory(loaded, text);

        // A source edit retains only the diagrams whose exact source still matches.
        var edited = text with { Text = Fence("flowchart LR\n Edited --> NeedsRenderer") + "\n\n" + text.Text };
        loaded.ReplaceObject(edited);
        var failedEdit = await MermaidDocument.CaptureAsync(loaded, unavailable);
        Assert(unavailable.Calls == 1 && failedEdit.Objects.OfType<TextBoardObject>().Single().MermaidSnapshots.Length == 1,
            "An unavailable renderer must preserve unchanged diagrams and omit stale snapshots for edited ones.");
        string explanation = string.Concat(GlyphText(MarkdownContent.Parse(edited.Text).Layout(640).Drawing));
        Assert(explanation.Contains("WebView2", StringComparison.Ordinal), "Regeneration failure must explain the missing runtime.");
        Assert(BluePixels(BoardPreviewRenderer.Render(failedEdit)!) > 1000,
            "An edit failure must not hide a different, valid saved diagram.");

        await CheckStaleCapture();
        await CheckInvalidSnapshots();
        await CheckCancellation();
    }

    private static void CheckHistory(BoardDocument document, TextBoardObject text)
    {
        var history = new CommandHistory();
        var ink = document.LinkedStrokes(text.Id).Single();
        var enlarged = (TextBoardObject)text.WithBounds(new RectD(80, 100, text.Bounds.Width * 2, text.Bounds.Height * 2));
        var movedInk = ink.TransformWithContainer(text.Bounds, enlarged.Bounds);
        history.Execute(new ReplaceObjectsCommand([text, ink], [enlarged, movedInk]), document);
        Assert(enlarged.MermaidSnapshots == text.MermaidSnapshots && enlarged.VisualScale == 2,
            "Saved diagrams must retain snapshot data and scale with the container.");
        history.Undo(document);
        Assert(document.Objects.OfType<TextBoardObject>().Single() == text && document.LinkedStrokes(text.Id).Single() == ink,
            "Undo must restore the original saved diagram and annotations.");
        history.Redo(document);
        history.Undo(document);
        history.Execute(new RemoveObjectsCommand(document.GetDeletionGroup(text.Id)), document);
        Assert(document.Objects.Count == 0, "Deleting a saved diagram container must delete its linked ink.");
        history.Undo(document);
        Assert(document.LinkedStrokes(text.Id).Single() == ink, "Undo deletion must restore annotations without rendering again.");
    }

    private static async Task CheckStaleCapture()
    {
        var document = new BoardDocument();
        var text = Text("flowchart LR\n Captured --> Source");
        document.AddObject(text);
        var renderer = new DelayedRenderer();
        var pending = MermaidDocument.CaptureAsync(document, renderer);
        var edited = text with { Text = Fence("flowchart LR\n New --> Source") };
        document.ReplaceObject(edited);
        renderer.Complete();
        var prepared = await pending;
        Assert(prepared.Objects.OfType<TextBoardObject>().Single().Text == text.Text &&
            document.Objects.Single() == edited && !MermaidDocument.Matches(document, prepared),
            "Typing during a save must not enter the captured file or be marked saved on completion.");
        document.RemoveObject(text.Id);
        Assert(!MermaidDocument.Matches(document, prepared), "Deleting a container during a save must not be undone by a late result.");
    }

    private static async Task CheckInvalidSnapshots()
    {
        string[] unsafeSvgs =
        [
            "<svg",
            Svg.Replace("320", "NaN", StringComparison.Ordinal),
            Svg.Replace("320", "20001", StringComparison.Ordinal),
            "<!DOCTYPE svg [<!ENTITY secret SYSTEM 'file:///missing-secret'>]>" + Svg,
            "<?xml-stylesheet href='https://example.invalid/x.css'?>" + Svg,
            Svg.Replace("</svg>", "<script>alert(1)</script></svg>", StringComparison.Ordinal),
            Svg.Replace("</svg>", "<foreignObject>HTML</foreignObject></svg>", StringComparison.Ordinal),
            Svg.Replace("</svg>", "<image href='file:///missing.png'/></svg>", StringComparison.Ordinal),
            Svg.Replace("<rect", "<rect onclick='alert(1)'", StringComparison.Ordinal),
            Svg.Replace("<rect", "<rect style='fill:red'", StringComparison.Ordinal),
            Svg.Replace("#0000ff", "url(https://example.invalid/fill)", StringComparison.Ordinal),
            Svg.Replace("</svg>", "<use href='#self' id='self'/></svg>", StringComparison.Ordinal),
            Svg.Replace("</svg>", string.Concat(Enumerable.Repeat("<g>", 70)) + string.Concat(Enumerable.Repeat("</g>", 70)) + "</svg>", StringComparison.Ordinal),
        ];
        int index = 0;
        foreach (string svg in unsafeSvgs)
        {
            var text = Text($"flowchart LR\n Unsafe{index++} --> Regenerate");
            var content = MarkdownContent.Parse(text.Text);
            text = text with { MermaidSnapshots = [new(content.DiagramSources.Single(), svg)] };
            var document = new BoardDocument();
            document.AddObject(text);
            await MermaidDocument.RestoreAsync(document);
            Assert(!content.DiagramsReady && content.Snapshots().IsEmpty,
                "Invalid or active SVG must not become a saved drawing.");
            var replacement = new ImmediateRenderer();
            var repaired = await MermaidDocument.CaptureAsync(document, replacement);
            Assert(replacement.Calls == 1 && repaired.Objects.OfType<TextBoardObject>().Single().MermaidSnapshots.Single().Svg == Svg,
                "A corrupt snapshot must regenerate from the source without preventing the board from opening.");
        }

        var legacy = new BoardDocument();
        legacy.AddObject(Text("flowchart LR\n Legacy --> Regenerate"));
        await MermaidDocument.RestoreAsync(legacy);
        var renderer = new ImmediateRenderer();
        Assert((await MermaidDocument.CaptureAsync(legacy, renderer)).Objects.OfType<TextBoardObject>().Single().MermaidSnapshots.Length == 1 && renderer.Calls == 1,
            "Boards saved before snapshots existed must regenerate their diagrams.");
    }

    private static async Task CheckCancellation()
    {
        var document = new BoardDocument();
        document.AddObject(Text("flowchart LR\n Cancel --> Wait"));
        var renderer = new DelayedRenderer();
        using var cancellation = new CancellationTokenSource();
        var pending = MermaidDocument.CaptureAsync(document, renderer, cancellation.Token);
        cancellation.Cancel();
        try { await pending; throw new InvalidOperationException("Cancelled capture completed."); }
        catch (OperationCanceledException) { }
        renderer.Complete();
        var completed = await MermaidDocument.CaptureAsync(document, renderer);
        Assert(completed.Objects.OfType<TextBoardObject>().Single().MermaidSnapshots.Length == 1,
            "Cancelling an output wait must not cancel the shared canvas rendering.");
    }

    public static async Task CheckExports(BoardDocument prepared, string sourceTag = "flowchart", bool themed = false)
    {
        string folder = Path.Combine(Path.GetTempPath(), "Whiteboard-Mermaid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            foreach (bool editable in new[] { false, true })
            {
                var settings = new ExportSettings { Format = ExportFormat.PowerPoint, IncludeNotes = true,
                    SlideContent = editable ? ExportSlideContent.Editable : ExportSlideContent.Picture };
                string pptx = Path.Combine(folder, editable ? "editable.pptx" : "picture.pptx");
                await BoardExporter.ExportAsync(prepared, settings, BoardExporter.Areas(prepared, settings, null), pptx,
                    "Mermaid", null, null, new Progress<ExportProgress>(), CancellationToken.None);
                using var package = DocumentFormat.OpenXml.Packaging.PresentationDocument.Open(pptx, false);
                Assert(!new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(package).Any(), "A Mermaid deck must be schema-valid.");
                using var zip = ZipFile.OpenRead(pptx);
                Assert(zip.Entries.Where(entry => entry.FullName.StartsWith("ppt/media/", StringComparison.Ordinal))
                    .Any(entry => { using var input = entry.Open(); using var bytes = new MemoryStream(); input.CopyTo(bytes); return BluePixels(bytes.ToArray(), themed) > 1000; }),
                    "PowerPoint pictures and editable-slide fallbacks must contain the saved diagram.");
                using var notes = new StreamReader(zip.GetEntry("ppt/notesSlides/notesSlide1.xml")!.Open());
                Assert(notes.ReadToEnd().Contains(sourceTag, StringComparison.Ordinal), "Mermaid source must stay in PowerPoint notes.");
            }
            foreach (bool vector in new[] { false, true })
            {
                var settings = new ExportSettings { Format = ExportFormat.Pdf,
                    PageContent = vector ? ExportPageContent.Vector : ExportPageContent.Picture };
                string pdf = Path.Combine(folder, vector ? "vector.pdf" : "picture.pdf");
                await BoardExporter.ExportAsync(prepared, settings, BoardExporter.Areas(prepared, settings, null), pdf,
                    "Mermaid", null, null, new Progress<ExportProgress>(), CancellationToken.None);
                using var parsed = PdfSharp.Pdf.IO.PdfReader.Open(pdf, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
                Assert(parsed.PageCount == 1, "The saved diagram must export as a readable PDF in either mode.");
            }
        }
        finally
        {
            foreach (string name in new[] { "picture.pptx", "editable.pptx", "picture.pdf", "vector.pdf" })
                File.Delete(Path.Combine(folder, name));
            Directory.Delete(folder);
        }
    }

    private static async Task<BoardDocument> RoundTrip(BoardDocument document, byte[] preview)
    {
        using var stream = new MemoryStream();
        await BoardArchive.SaveAsync(document, stream, previewPng: preview);
        stream.Position = 0;
        using var embedded = new MemoryStream();
        Assert(BoardArchive.TryCopyPreview(stream, embedded) && embedded.ToArray().SequenceEqual(preview),
            "The archive must retain the prepared diagram preview.");
        stream.Position = 0;
        return await BoardArchive.LoadAsync(stream);
    }

    private static TextBoardObject Text(string source) => new(Guid.NewGuid(), 0, new RectD(20, 30, 800, 100),
        "Markdown", Fence(source), 1, TextLanguageIds.Markdown);

    private static int BluePixels(byte[] png, bool themed = false)
    {
        var bitmap = new FormatConvertedBitmap(WpfImageCodec.Decode(png), PixelFormats.Bgra32, null, 0);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        int blue = 0;
        for (int i = 0; i < pixels.Length; i += 4)
            if (pixels[i] > 200 && (themed
                ? pixels[i] > pixels[i + 1] + 8 && pixels[i] > pixels[i + 2] + 8
                : pixels[i + 1] < 50 && pixels[i + 2] < 50)) blue++;
        return blue;
    }

    private static IEnumerable<string> GlyphText(Drawing drawing)
    {
        if (drawing is GlyphRunDrawing glyph && glyph.GlyphRun.Characters is { } characters)
            yield return new string(characters.ToArray());
        else if (drawing is DrawingGroup group)
            foreach (var child in group.Children)
                foreach (string text in GlyphText(child)) yield return text;
    }

    private static MermaidDiagram Diagram() => new(MermaidSvg.Decode(Svg), null, Svg);
    private sealed class DelayedRenderer : IMermaidRenderer
    {
        private readonly TaskCompletionSource<MermaidDiagram> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<MermaidDiagram> RenderAsync(string source) => _result.Task;
        public void Complete() => _result.SetResult(Diagram());
    }
    private sealed class ImmediateRenderer : IMermaidRenderer
    {
        public int Calls { get; private set; }
        public Task<MermaidDiagram> RenderAsync(string source) { Calls++; return Task.FromResult(Diagram()); }
    }
    private sealed class UnavailableRenderer : IMermaidRenderer
    {
        public int Calls { get; private set; }
        public Task<MermaidDiagram> RenderAsync(string source) { Calls++; throw new InvalidOperationException("No runtime"); }
    }
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
