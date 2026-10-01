using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using SQLBI.Whiteboard.Core.Commands;
using SQLBI.Whiteboard.Core.Geometry;
using SQLBI.Whiteboard.Core.Model;

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
        Assert(after.Drawing.IsFrozen && after.Height < before.Height && !ReferenceEquals(before, after),
            "Rendering must refresh the frozen drawing and compact the layout to its natural height.");
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
        await CheckSizing();
    }

    private static async Task CheckSizing()
    {
        var small = MarkdownContent.Parse(Fence("flowchart LR\n Small --> Diagram"));
        await small.PrepareDiagramsAsync(new FakeRenderer(width: 400, height: 200));
        var natural = small.Layout(820);
        Assert(Images(natural.Drawing).Single().Rect == new Rect(210, 10, 400, 200) && natural.Height == 220,
            "A 400x200 diagram must stay at 100%, be centered, and use only its height plus padding.");
        var wide = MarkdownContent.Parse(Fence("flowchart LR\n Wide --> Diagram"));
        var renderer = new FakeRenderer(width: 1000, height: 500);
        await wide.PrepareDiagramsAsync(renderer);
        var fitted = wide.Layout(820);
        Assert(Images(fitted.Drawing).Single().Rect == new Rect(10, 10, 800, 400) && fitted.Height == 420,
            "A wide diagram must shrink proportionally to the available width.");
        Assert(wide.Layout(420).Height == 220 && wide.Layout(2020).Height == 520 && renderer.Count == 1,
            "Reflow must fit the available width, stop at natural size, and reuse the cached diagram.");

        string source = Fence("flowchart TD\n Tall --> Diagram");
        var content = MarkdownContent.Parse(source);
        var delayed = new DelayedRenderer();
        var preparation = content.PrepareDiagramsAsync(delayed);
        var document = new BoardDocument();
        var history = new CommandHistory();
        document.Changed += (_, _) => MermaidContainerLayout.Refresh(document);
        var original = new TextBoardObject(Guid.NewGuid(), 0, new RectD(20, 30, 820, 214),
            "Test", source, LanguageId: TextLanguageIds.Markdown);
        history.Execute(new AddObjectCommand(original), document);
        Assert(!content.DiagramsReady && MermaidContainerLayout.Fit(original) == original,
            "Pending diagrams must not normalize a container to an incomplete result.");
        var ink = InkStrokeObject.Create([new InkPoint(new PointD(40, 80), 0.5f, 1)],
            PenStyle.Default, 1, containerId: original.Id);
        document.AddObject(ink);
        delayed.Complete(await new FakeRenderer(width: 400, height: 900).RenderAsync(""));
        await preparation;
        Assert(!MermaidContainerLayout.Refresh(document, original.Id),
            "Completing a render must not alter a container's active source editor.");
        Assert(MermaidContainerLayout.Refresh(document), "Completing a diagram must update the container height.");
        var measured = document.Objects.OfType<TextBoardObject>().Single();
        Assert(measured.Bounds == new RectD(20, 30, 820, 974) && measured.VisualScale == 1 &&
            ReferenceEquals(document.LinkedStrokes(original.Id).Single(), ink),
            "Natural height must grow for tall diagrams without moving, scaling, or stretching ink.");
        Assert(!MermaidContainerLayout.Refresh(document), "A fitted container must not produce repeated document changes.");
        history.Undo(document);
        Assert(!document.Objects.OfType<TextBoardObject>().Any() && !history.CanUndo,
            "Async fitting must not add an undo step after paste.");
        history.Redo(document);
        Assert(document.Objects.OfType<TextBoardObject>().Single() == measured,
            "Redo must restore the measured size, not the pending placeholder dimensions.");

        var scaledBounds = measured.Bounds.WithSize(measured.Bounds.Width * 2, measured.Bounds.Height * 2);
        var scaled = (TextBoardObject)measured.WithBounds(scaledBounds);
        var scaledInk = ink.TransformWithContainer(measured.Bounds, scaledBounds);
        history.Execute(new ReplaceObjectsCommand([measured, ink], [scaled, scaledInk]), document);
        Assert(document.Objects.OfType<TextBoardObject>().Single() == scaled &&
            document.LinkedStrokes(original.Id).Single() == scaledInk,
            "Explicit corner resizing must still scale the container and its linked ink.");
        history.Undo(document);
        Assert(document.Objects.OfType<TextBoardObject>().Single() == measured &&
            document.LinkedStrokes(original.Id).Single() == ink, "Resize undo must restore both the drawing and ink.");
        history.Redo(document);
        Assert(document.Objects.OfType<TextBoardObject>().Single() == scaled, "Resize redo must keep the chosen visual scale.");

        var edited = scaled with { Text = Fence("flowchart LR\n Changed --> Source") };
        var placeholder = edited with { Bounds = edited.Bounds.WithSize(edited.Bounds.Width, 214) };
        Assert(MermaidContainerLayout.InkBoundsAfterEdit(scaled, placeholder) == scaled.Bounds &&
            MermaidContainerLayout.InkBoundsAfterEdit(scaled, edited) == scaled.Bounds,
            "Editing Mermaid must not squeeze annotations into a placeholder or depend on render-cache timing.");
        Assert(MermaidContainerLayout.InkBoundsAfterEdit(measured, scaled) == scaled.Bounds,
            "An explicit width change while editing must still scale linked ink.");
        var plainBefore = measured with { LanguageId = TextLanguageIds.Plain };
        var plainAfter = plainBefore with { Bounds = placeholder.Bounds };
        Assert(MermaidContainerLayout.InkBoundsAfterEdit(plainBefore, plainAfter) == plainAfter.Bounds,
            "Other text editors must keep their existing linked-ink transform behavior.");
        history.Execute(new ReplaceObjectCommand(scaled, edited), document);
        MermaidContainerLayout.Refresh(document);
        Assert(document.Objects.OfType<TextBoardObject>().Single() == edited,
            "An older prepared source must not resize a newer pending edit.");
        history.Undo(document);
        Assert(document.Objects.OfType<TextBoardObject>().Single() == scaled,
            "Undoing a source edit must recover its cached natural layout.");

        var multiple = MarkdownContent.Parse(Fence("flowchart LR\n First --> A") + "\n\n" + Fence("flowchart LR\n Second --> B"));
        var pending = new DelayedRenderer();
        var initial = multiple.Layout(600);
        var batch = multiple.PrepareDiagramsAsync(pending);
        pending.Complete(await new FakeRenderer().RenderAsync(""));
        await Dispatcher.Yield(DispatcherPriority.Background);
        Assert(!multiple.DiagramsReady && ReferenceEquals(initial, multiple.Layout(600)),
            "Multiple diagrams must publish one complete layout instead of repeatedly reflowing following text.");
        pending.Complete(await new FakeRenderer().RenderAsync(""));
        await batch;
        Assert(multiple.DiagramsReady && Images(multiple.Layout(600).Drawing).Count() == 2,
            "All diagrams must become available together after preparation finishes.");
    }

    private static IEnumerable<ImageDrawing> Images(Drawing drawing)
    {
        if (drawing is ImageDrawing image) yield return image;
        else if (drawing is DrawingGroup group)
            foreach (var child in group.Children)
                foreach (var item in Images(child)) yield return item;
    }

    private sealed class DelayedRenderer : IMermaidRenderer
    {
        private TaskCompletionSource<MermaidDiagram> _pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<MermaidDiagram> RenderAsync(string source) => _pending.Task;
        public void Complete(MermaidDiagram result)
        {
            var pending = _pending;
            _pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            pending.SetResult(result);
        }
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
        Assert(Math.Abs(diagram.Image!.Width - (double)svg.Root!.Attribute("width")!) < 0.001 &&
            Math.Abs(diagram.Image.Height - (double)svg.Root.Attribute("height")!) < 0.001,
            "WPF dimensions must match Mermaid's declared viewport, including invisible geometry outside it.");
        var viewport = new Rect(0, 0, diagram.Image.Width, diagram.Image.Height);
        viewport.Inflate(1, 1);
        foreach (var bounds in GlyphBounds(diagram.Image.Drawing, Matrix.Identity))
            Assert(viewport.Contains(bounds), "Bounding the SVG viewport must not clip a diagram label: " + bounds);
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

    private static IEnumerable<Rect> GlyphBounds(Drawing drawing, Matrix parent)
    {
        if (drawing is GlyphRunDrawing glyph) yield return new MatrixTransform(parent).TransformBounds(glyph.Bounds);
        else if (drawing is DrawingGroup group)
        {
            var transform = group.Transform?.Value ?? Matrix.Identity;
            transform.Append(parent);
            foreach (var child in group.Children)
                foreach (var bounds in GlyphBounds(child, transform)) yield return bounds;
        }
    }

    private sealed class FakeRenderer(bool fail = false, double width = 300, double height = 100) : IMermaidRenderer
    {
        public int Count { get; private set; }
        public Task<MermaidDiagram> RenderAsync(string source)
        {
            Count++;
            if (fail) return Task.FromResult(MermaidDiagram.Failure("Test error"));
            var drawing = new GeometryDrawing(Brushes.Blue, null, new RectangleGeometry(new Rect(0, 0, width, height)));
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
