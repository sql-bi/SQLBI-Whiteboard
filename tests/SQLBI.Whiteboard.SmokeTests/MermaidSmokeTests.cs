using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;

namespace SQLBI.Whiteboard.SmokeTests;

internal static class MermaidSmokeTests
{
    private const string Flow = "flowchart LR\n A[Question] --> B{Needs data?}\n B -->|Yes| C[Query model]\n B -->|No| D[Answer]\n C --> D";
    private const string Sequence = "sequenceDiagram\n participant U as User\n participant W as Whiteboard\n U->>W: Paste Markdown\n W-->>U: Draw diagram";
    private const string Er = "erDiagram\n CUSTOMER ||--o{ ORDER : places\n CUSTOMER {\n int id PK\n string name\n }\n ORDER {\n int id PK\n int customerId FK\n }";
    private const string Styled = "flowchart TD\n A[\"`**Revenue** in €\nCost and margin`\"] --> B[\"Café 日本語\"]";
    private static string Fence(string source) => "```mermaid\n" + source + "\n```";

    public static void Run(bool browser, string? previewPath)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            Dispatcher.CurrentDispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await Check();
                    if (browser) await CheckBrowser(previewPath);
                }
                catch (Exception exception) { failure = exception; }
                finally { Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromMinutes(2))) throw new TimeoutException("Mermaid smoke tests timed out.");
        if (failure is not null) throw new InvalidOperationException("Mermaid smoke tests failed.", failure);
    }

    private static async Task Check()
    {
        Assert(MermaidSource.Validate(Flow) is null && MermaidSource.Validate(Sequence) is null &&
            MermaidSource.Validate(Er) is null, "The prototype must accept its three diagram families.");
        Assert(MermaidSource.Validate("%% Comment\n" + Flow) is null, "A leading comment must be allowed.");
        Assert(MermaidSource.Validate("pie\n\"A\": 10") is not null, "Unsupported types need an explicit message.");
        Assert(MermaidSource.Validate("%%{init: {'securityLevel':'loose'}}%%\n" + Flow) is not null &&
            MermaidSource.Validate("---\nconfig: {}\n---\n" + Flow) is not null,
            "Pasted configuration must not override the local security settings.");
        Assert(MermaidSource.Validate(new string('x', MermaidSource.MaximumCharacters + 1)) is not null,
            "Oversized diagrams must be rejected before reaching the browser.");
        Assert(MarkdownContent.Parse("```MERMAID\n" + Flow + "\n```").DiagramSources.Count == 1,
            "The Mermaid fence tag must be case insensitive.");

        string source = "# Mixed Markdown\n\n" + Fence(Flow) + "\n\n| A | B |\n|---|---|\n| One | Two |\n\n" + Fence(Flow);
        var content = MarkdownContent.Parse(source);
        var before = content.Layout(600);
        var fake = new FakeRenderer();
        await content.PrepareDiagramsAsync(fake);
        await content.PrepareDiagramsAsync(fake);
        var after = content.Layout(600);
        Assert(fake.Count == 1 && content.DiagramSources.Count == 1, "Duplicate source must render once.");
        Assert(after.Drawing.IsFrozen && after.Height == before.Height && !ReferenceEquals(before, after),
            "Rendering must refresh a frozen drawing without moving later Markdown or changing container height.");
        Assert(ReferenceEquals(after, content.Layout(600)), "An unchanged layout must be reused.");
        for (int i = 0; i < 100; i++) content.Layout(300 + i);
        Assert(fake.Count == 1, "Reflow and zoom must not rerun the diagram engine.");
        var nested = MarkdownContent.Parse("> ```mermaid\n> flowchart LR\n> A --> B\n> ```");
        await nested.PrepareDiagramsAsync(new FakeRenderer());
        Assert(nested.DiagramSources.Count == 1 && nested.Layout(300).Drawing.IsFrozen,
            "Mermaid inside a block quote must use the same preparation and layout path.");

        var ordinary = MarkdownContent.Parse("```sql\nSELECT 1;\n```");
        Assert(ordinary.DiagramSources.Count == 0, "Ordinary code blocks must retain their existing rendering.");
        var failing = MarkdownContent.Parse(Fence("flowchart LR\n["));
        await failing.PrepareDiagramsAsync(new FakeRenderer(fail: true));
        Assert(failing.Layout(300).Drawing.IsFrozen, "A failed diagram must leave the Markdown drawable.");
        var many = MarkdownContent.Parse(string.Join("\n\n",
            Enumerable.Range(0, 20).Select(i => Fence($"flowchart LR\n A --> N{i}"))));
        var bounded = new FakeRenderer();
        await many.PrepareDiagramsAsync(bounded);
        Assert(bounded.Count == MermaidSource.MaximumDiagrams, "A document must not queue unlimited diagrams.");
    }

    private static async Task CheckBrowser(string? previewPath)
    {
        using var host = new HwndSource(new HwndSourceParameters("Mermaid smoke tests")
            { Width = 1600, Height = 1200, WindowStyle = 0 });
        using var renderer = new MermaidRenderer(() => host.Handle,
            Path.Combine(Path.GetTempPath(), "SQLBI.Whiteboard.MermaidTests"));
        var results = new List<MermaidDiagram>();
        foreach (string source in new[] { Flow, Sequence, Er })
        {
            var clock = Stopwatch.StartNew();
            var result = await renderer.RenderAsync(source);
            Assert(result.Image is { IsFrozen: true, Width: > 0, Height: > 0 },
                $"Browser rendering failed for {source.Split('\n')[0]}: {result.Error}");
            results.Add(result);
            CheckLabels(result, source == Flow ? ["Question", "Needs", "data?", "Query", "model", "Answer"] :
                source == Sequence ? ["User", "Whiteboard", "Paste Markdown", "Draw diagram"] :
                ["CUSTOMER", "ORDER", "places", "customerId"]);
            if (previewPath is not null)
                File.WriteAllText(Path.ChangeExtension(previewPath, $"{results.Count}.svg"), result.Svg);
            Console.WriteLine($"Mermaid {source.Split('\n')[0]}: {clock.ElapsedMilliseconds} ms, " +
                $"{result.Image!.Width:0} x {result.Image.Height:0}");
        }
        int count = renderer.RenderCount;
        Assert(ReferenceEquals(results[0], await renderer.RenderAsync(Flow)) && renderer.RenderCount == count,
            "An unchanged diagram must reuse its cached frozen image.");
        var invalid = await renderer.RenderAsync("flowchart LR\n A[");
        Assert(invalid.Error is not null, "Invalid Mermaid must report an error.");
        Assert((await renderer.RenderAsync("flowchart LR\n A --> B")).Image is not null,
            "A syntax error must not prevent later diagrams from rendering.");
        var styled = await renderer.RenderAsync(Styled);
        Assert(styled.Image is not null, "Multiline styled Unicode labels must render: " + styled.Error);
        CheckLabels(styled, ["Revenue", "€", "Cost", "margin", "Café", "日本語"]);
        var content = MarkdownContent.Parse("# Mermaid prototype\n\n" + Fence(Flow) + "\n\n" +
            "| Input | Output |\n|---|---|\n| Mermaid source | Cached WPF drawing |\n\n" +
            Fence(Sequence) + "\n\n" + Fence(Er) + "\n\n" + Fence(Styled));
        await content.PrepareDiagramsAsync(renderer);
        if (previewPath is not null)
        {
            var layout = content.Layout(850);
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawRectangle(Brushes.White, null, new Rect(0, 0, 890, layout.Height + 40));
                context.PushTransform(new TranslateTransform(20, 20));
                context.DrawDrawing(layout.Drawing);
                context.Pop();
            }
            var bitmap = new RenderTargetBitmap(890, (int)Math.Ceiling(layout.Height + 40), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(previewPath);
            encoder.Save(stream);
        }
        var pending = renderer.RenderAsync("sequenceDiagram\n A->>B: Closing");
        renderer.Dispose();
        await pending;
        Assert((await renderer.RenderAsync(Flow)).Error is not null, "Closing must reject further rendering safely.");
        using var starting = new MermaidRenderer(() => host.Handle,
            Path.Combine(Path.GetTempPath(), "SQLBI.Whiteboard.MermaidTests"));
        var startup = starting.RenderAsync(Flow);
        starting.Dispose();
        Assert((await startup).Error is not null, "Closing during browser startup must cancel safely.");
    }

    private static void CheckLabels(MermaidDiagram diagram, string[] labels)
    {
        var svg = XDocument.Parse(diagram.Svg!);
        var text = svg.Descendants().Where(element => element.Name.LocalName == "text").ToArray();
        Assert(text.Length > 0 && text.All(element => (string?)element.Attribute("text-anchor") == "start"),
            "Flattened labels must retain their measured left-edge position after CSS is removed.");
        Assert(!svg.Descendants().Any(element => element.Name.LocalName == "tspan"),
            "Nested SVG text must be flattened for SharpVectors.");
        Assert(!svg.Descendants().Any(element => element.Name.LocalName == "rect" &&
            (element.Attribute("width") is null || element.Attribute("height") is null)),
            "Empty SVG background rectangles must not obscure labels.");
        string glyphText = string.Concat(GlyphText(diagram.Image!.Drawing));
        foreach (string label in labels)
            Assert(glyphText.Contains(label, StringComparison.Ordinal), "Missing WPF diagram label: " + label);
    }

    private static IEnumerable<string> GlyphText(Drawing drawing)
    {
        if (drawing is GlyphRunDrawing glyph && glyph.GlyphRun.Characters is { } characters)
            yield return new string(characters.ToArray());
        else if (drawing is DrawingGroup group)
            foreach (var child in group.Children)
                foreach (string text in GlyphText(child)) yield return text;
    }

    private sealed class FakeRenderer(bool fail = false) : IMermaidRenderer
    {
        public int Count { get; private set; }
        public Task<MermaidDiagram> RenderAsync(string source)
        {
            Count++;
            if (fail) return Task.FromResult(MermaidDiagram.Failure("Test error"));
            var drawing = new GeometryDrawing(Brushes.Blue, null, new RectangleGeometry(new Rect(0, 0, 300, 100)));
            var image = new DrawingImage(drawing);
            image.Freeze();
            return Task.FromResult(new MermaidDiagram(image, null));
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
