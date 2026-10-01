using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using SQLBI.Whiteboard.Core.Geometry;
using SQLBI.Whiteboard.Core.Model;
using SQLBI.Whiteboard.Core.Persistence;

namespace SQLBI.Whiteboard.SmokeTests;

internal static class MermaidCatalogSmokeTests
{
    private static IEnumerable<(string Name, string Source)> Samples()
    {
        foreach (string path in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "MermaidCatalogue"), "*.md").Order())
        {
            string name = "";
            foreach (var block in Markdown.Parse(File.ReadAllText(path)))
            {
                if (block is HeadingBlock { Level: 2, Inline: { } inline })
                    name = string.Concat(inline.OfType<LiteralInline>().Select(item => item.Content.ToString()));
                else if (block is FencedCodeBlock fence && MermaidSource.IsDiagram(fence))
                    yield return (name, fence.Lines.ToString());
            }
        }
    }

    public static void CheckSources()
    {
        var samples = Samples().ToArray();
        Assert(samples.Length == 30 && samples.Select(item => item.Name).Distinct().Count() == 30,
            "The additional catalogue must cover 27 families and three railroad notation variants.");
        foreach (var sample in samples)
            Assert(MermaidSource.Validate(sample.Source) is null, "The catalogue source must pass validation: " + sample.Name);
    }

    public static async Task RunAsync(MermaidRenderer renderer, string? previewPath)
    {
        var failures = new List<string>();
        using var closedRenderer = new MermaidRenderer(() => throw new InvalidOperationException("No browser may start"));
        closedRenderer.Dispose();
        foreach (var (name, source) in Samples())
        {
            try
            {
                var diagram = await renderer.RenderAsync(source);
                Assert(diagram.Image is { IsFrozen: true }, name + ": " + diagram.Error);
                if (previewPath is not null) SavePreview(previewPath, name, diagram);
                MermaidSmokeTests.CheckLabels(diagram, ["Alpha"]);
                var svg = XDocument.Parse(diagram.Svg!);
                Assert(!svg.Descendants().Any(element => element.Name.LocalName is "foreignObject" or "switch" or "script" or "style" or "a") &&
                    !svg.Descendants().Attributes().Any(attribute => attribute.Name.LocalName is "style" or "href" or "src" ||
                        attribute.Name.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase)),
                    name + ": normalized diagrams must contain only passive SVG.");
                if (name == "User journey")
                {
                    MermaidSmokeTests.CheckLabels(diagram, ["Plan", "Alpha", "Beta"]);
                    Assert(svg.Descendants().Count(element => element.Name.LocalName == "text" && element.Value == "Alpha") == 1,
                        "HTML journey labels must not duplicate their SVG fallback.");
                }
                int count = renderer.RenderCount;
                Assert(ReferenceEquals(diagram, await renderer.RenderAsync(source)) && count == renderer.RenderCount,
                    name + ": unchanged sources must use the cache.");
                string markdown = "```mermaid\n" + source + "\n```";
                var board = new BoardDocument();
                board.AddObject(new TextBoardObject(Guid.NewGuid(), 0, new RectD(0, 0, 800, 100), name,
                    markdown, 1, TextLanguageIds.Markdown));
                var prepared = await MermaidDocument.CaptureAsync(board, renderer);
                byte[] before = BoardPreviewRenderer.Render(prepared)!;
                using var archive = new MemoryStream();
                await BoardArchive.SaveAsync(prepared, archive);
                archive.Position = 0;
                var loaded = await BoardArchive.LoadAsync(archive);
                var loadedText = loaded.Objects.OfType<TextBoardObject>().Single();
                Assert(!ReferenceEquals(markdown, loadedText.Text) && !MarkdownContent.Parse(loadedText.Text).DiagramsReady,
                    name + ": reopening must exercise snapshot decoding, not an existing Markdown cache entry.");
                Assert(loadedText.MermaidSnapshots.Length == 1,
                    name + ": the saved board must contain a diagram snapshot.");
                await MermaidDocument.RestoreAsync(loaded);
                var offline = await MermaidDocument.CaptureAsync(loaded, closedRenderer);
                Assert(MermaidPersistenceSmokeTests.SamePixels(before, BoardPreviewRenderer.Render(offline)!) && closedRenderer.RenderCount == 0,
                    name + ": saved output must reopen identically without a browser.");
                // Cover gradients, rotated labels, and measured HTML labels in
                // both picture and editable/vector export paths.
                if (name is "Sankey" or "XY chart" or "Event modeling")
                    await MermaidPersistenceSmokeTests.CheckExports(offline, source.Split('\n')[0], exactPictures: true);
                Console.WriteLine($"Mermaid catalogue {name}: passed ({diagram.Image!.Width:0} x {diagram.Image.Height:0})");
            }
            catch (Exception exception) { failures.Add(name + ": " + exception.Message); Console.WriteLine(failures[^1]); }
        }
        Assert(failures.Count == 0, "Mermaid catalogue failures:\n" + string.Join("\n", failures));
        await CheckVariants(renderer, previewPath);
    }

    private static async Task CheckVariants(MermaidRenderer renderer, string? previewPath)
    {
        (string Name, string Source, string[] Labels)[] variants =
        [
            ("C4 container", "C4Container\n Container(a, \"Alpha\", \"SQL\", \"Data\")", ["Alpha", "Data"]),
            ("C4 component", "C4Component\n Component(a, \"Alpha\", \"C#\", \"Service\")", ["Alpha", "Service"]),
            ("C4 dynamic", "C4Dynamic\n Container(a, \"Alpha\", \"SQL\")\n Container(b, \"Beta\", \"C#\")\n Rel(a,b,\"Uses\")", ["Alpha", "Beta", "Uses"]),
            ("C4 deployment", "C4Deployment\n Deployment_Node(a, \"Alpha\", \"Server\") {\n Container(b, \"Beta\", \"SQL\")\n }", ["Alpha", "Beta"]),
            ("ELK flowchart", "flowchart-elk LR\n A[Alpha] --> B[Beta]", ["Alpha", "Beta"]),
            ("Event labels", "eventmodeling\n tf 01 ui Alpha\n tf 02 cmd Beta { description: 'Café' quantity: 12 }\n tf 03 evt Accepted", ["Alpha", "Beta", "description", "Café", "quantity"]),
            ("Wrapped journey", "journey\n section Plan\n Alpha with a wrapped label: 5: Analyst", ["Alpha", "wrapped", "label"]),
            ("Linked class", "classDiagram\n class Alpha\n click Alpha href \"https://example.invalid/\"", ["Alpha"]),
        ];
        foreach (var (name, source, labels) in variants)
        {
            var diagram = await renderer.RenderAsync(source);
            Assert(diagram.Image is not null, name + ": " + diagram.Error);
            if (previewPath is not null) SavePreview(previewPath, name, diagram);
            MermaidSmokeTests.CheckLabels(diagram, labels);
            if (name == "Event labels")
                Assert(XDocument.Parse(diagram.Svg!).Descendants().Any(element => element.Value == "Beta" &&
                    (string?)element.Attribute("font-weight") == "700"), "HTML label emphasis must survive conversion to SVG text.");
            if (name == "Linked class")
                Assert(!XDocument.Parse(diagram.Svg!).Descendants().Any(element => element.Name.LocalName == "a"),
                    "Class links must not remain interactive in saved SVG.");
            Console.WriteLine($"Mermaid variant {name}: passed");
        }
    }

    private static void SavePreview(string path, string name, MermaidDiagram diagram)
    {
        string stem = Path.Combine(Path.GetDirectoryName(path)!, "catalogue-" + name.Replace(' ', '-').ToLowerInvariant());
        File.WriteAllText(stem + ".svg", diagram.Svg);
        var visual = new DrawingVisual();
        double scale = Math.Min(1, 1400 / Math.Max(diagram.Image!.Width, diagram.Image.Height));
        var bounds = new Rect(0, 0, diagram.Image.Width * scale + 20, diagram.Image.Height * scale + 20);
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, bounds);
            context.DrawImage(diagram.Image, new Rect(10, 10, bounds.Width - 20, bounds.Height - 20));
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(bounds.Width), (int)Math.Ceiling(bounds.Height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(stem + ".png");
        encoder.Save(stream);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
