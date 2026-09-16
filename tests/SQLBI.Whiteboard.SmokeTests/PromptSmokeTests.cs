using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.TextFormatting;
using System.Xml.Linq;
using ICSharpCode.AvalonEdit;
using SQLBI.Whiteboard.Core.Geometry;
using SQLBI.Whiteboard.Core.Model;
using SQLBI.Whiteboard.Core.Settings;
using SQLBI.Whiteboard.Core.Viewport;
using SQLBI.Whiteboard.Export;

namespace SQLBI.Whiteboard.SmokeTests;

internal static class PromptSmokeTests
{
    private const string Source = "Analyze the selected period and explain your assumptions.\r\n\r\n- Identify the stores that were open throughout the entire selected period, even when the period spans several years.\r\n- Explain any missing sales and how they affect eligibility.\r\n  - Keep the original source text intact when copying.\r\n\r\nReturn a concise explanation with an example.";

    public static void Run(string? previewPath)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { Check(previewPath); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new InvalidOperationException("Prompt smoke tests failed.", failure);
        }
    }

    private static void Check(string? previewPath)
    {
        ClipboardPromptSmokeTests.Run();
        var language = TextLanguageRegistry.Resolve(TextLanguageIds.Prompt);
        Assert(language.DisplayName == "Prompt" && language.WordWrap && !language.ShowLineNumbers &&
            !language.CanDetect && !language.CanFormat && language.FormattingRequestUri is null &&
            !language.TryAccept(Source) && !language.TryFormat(Source, 65, out var unchanged) && unchanged == Source &&
            language.Analyze(Source, "Old title").Title == "Prompt" &&
            TextLanguageRegistry.ResolveFromOrder(Source, [TextLanguageIds.Prompt, TextLanguageIds.Plain]) == TextLanguageIds.Plain,
            "Prompt is selected explicitly, does not rewrite text, and never sends F6 to a code-formatting issue.");
        var layout = new PromptTextLayout(Source, 330, 18, 1, Brushes.Black);
        Assert(layout.Paragraphs.Count == 7 && layout.Paragraphs[0].Origin.X == 0 &&
            layout.Paragraphs[2].Origin.X > 0 && layout.Paragraphs[2].Marker?.Text == "•" &&
            layout.Paragraphs[4].Origin.X > layout.Paragraphs[2].Origin.X && layout.Paragraphs[6].Origin.X == 0,
            "Display mode should indent bullets, preserve indented lists and blank lines, and leave prose alone.");
        Assert(new PromptTextLayout(Source, 180, 18, 1, Brushes.Black).Height > layout.Height &&
            Math.Abs(new PromptTextLayout(Source, 660, 36, 1, Brushes.Black).Height - layout.Height * 2) < 0.1,
            "Narrowing should reflow a prompt, while scaling should preserve its line breaks.");
        var desired = TextContainerVisual.MeasureDesiredHeight(Source, 350, 1, 1, TextLanguageIds.Prompt);
        Assert(desired >= layout.Height + TextContainerVisual.TitleBarHeight + 2 * TextContainerVisual.ContentPadding,
            "The container height must include every wrapped prompt line and its chrome.");

        var editor = new TextEditor
        {
            FontFamily = new FontFamily("Segoe UI"), FontSize = 18, WordWrap = true,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Text = Source,
        };
        var generator = new PromptBulletGenerator { IsEnabled = true };
        editor.TextArea.TextView.ElementGenerators.Add(generator);
        LayoutEditor(editor, 330);
        var view = editor.TextArea.TextView;
        var bullet = view.GetVisualLine(3);
        Assert(bullet.TextLines.Count > 1, "The sample bullet must wrap for the indentation test.");
        var expected = bullet.TextLines[0].GetDistanceFromCharacterHit(new CharacterHit(2, 0));
        var actual = WrappedIndent(bullet);
        Assert(expected > 0 && Math.Abs(actual - expected) < 0.1 && editor.Text == Source,
            $"Wrapped editor lines must align with '- ': expected {expected}, actual {actual}, inherit {editor.Options.InheritWordWrapIndentation}, source unchanged {editor.Text == Source}, elements {string.Join(",", bullet.Elements.Select(e => e.GetType().Name))}.");
        Assert(WrappedIndent(view.GetVisualLine(1)) == 0,
            "Non-list paragraphs must keep normal wrapping in the editor.");
        var initialLines = bullet.TextLines.Count;
        LayoutEditor(editor, 180);
        Assert(view.GetVisualLine(3).TextLines.Count > initialLines, "Resizing must reflow the editor's bullets.");
        editor.FontSize = 36;
        LayoutEditor(editor, 660);
        Assert(Math.Abs(WrappedIndent(view.GetVisualLine(3)) - expected * 2) < 0.2,
            "Editor zoom must scale the indent along with the font.");
        editor.FontSize = 18;
        generator.IsEnabled = false;
        view.Redraw();
        LayoutEditor(editor, 330);
        Assert(WrappedIndent(view.GetVisualLine(3)) == 0 && editor.Text == Source,
            "Switching out of Prompt must remove bullet indentation without changing the text.");
        generator.IsEnabled = true;
        view.Redraw();
        editor.Document.Remove(editor.Document.GetLineByNumber(3).Offset, 2);
        LayoutEditor(editor, 330);
        Assert(WrappedIndent(view.GetVisualLine(3)) == 0, "Removing a marker should restore ordinary wrapping.");
        editor.Undo();
        LayoutEditor(editor, 330);
        Assert(editor.Text == Source && WrappedIndent(view.GetVisualLine(3)) > 0,
            "Undo should restore the literal source and its hanging indent.");
        var nested = view.GetVisualLine(5);
        Assert(Math.Abs(WrappedIndent(nested) - nested.TextLines[0].GetDistanceFromCharacterHit(new CharacterHit(4, 0))) < 0.1,
            "Indented bullet continuations should align after the marker too.");
        foreach (var scale in new[] { 1d, 2d })
        {
            editor.FontSize = 18 * scale;
            foreach (var width in new[] { 160d, 240d, 350d, 600d })
            {
                LayoutEditor(editor, (width - 22) * scale);
                var fitted = TextContainerVisual.MeasureDesiredHeight(Source, width * scale, scale, 1, TextLanguageIds.Prompt);
                Assert(fitted + 0.1 >= view.DocumentHeight + 48 * scale,
                    $"Prompt auto-height must fit the editor at width {width}, scale {scale}.");
            }
        }
        editor.FontSize = 18;
        LayoutEditor(editor, 330);
        CheckExports(desired);
        if (!string.IsNullOrWhiteSpace(previewPath)) RenderPreview(previewPath, editor, desired);
        editor.Document = null;
    }

    private static double WrappedIndent(ICSharpCode.AvalonEdit.Rendering.VisualLine line) =>
        line.TextLines[1].GetDistanceFromCharacterHit(new CharacterHit(line.TextLines[0].Length, 0));

    private static void LayoutEditor(TextEditor editor, double width)
    {
        editor.Width = width;
        editor.Height = 900;
        editor.ApplyTemplate();
        editor.Measure(new Size(width, 900));
        editor.Arrange(new Rect(0, 0, width, 900));
        editor.UpdateLayout();
        // Lay out the actual text view directly: this test has no desktop presentation source.
        var view = editor.TextArea.TextView;
        TextBlock.SetFontFamily(view, editor.FontFamily);
        TextBlock.SetFontSize(view, editor.FontSize);
        ((IScrollInfo)view).CanHorizontallyScroll = false;
        view.Measure(new Size(width, 900));
        view.Arrange(new Rect(0, 0, width, 900));
        view.EnsureVisualLines();
    }

    private static void CheckExports(double height)
    {
        var document = new BoardDocument();
        document.AddObject(new TextBoardObject(Guid.NewGuid(), 0, new RectD(0, 0, 350, height), "Text", Source, 1, TextLanguageIds.Prompt));
        var area = BoardExporter.Areas(document, new ExportSettings { PageModel = ExportPageModel.WholeBoard }, null).Single();
        var elements = EditableSlide.Build(document, area, 700, 900, null, inkAsStrokes: true);
        var text = elements.OfType<SlideTextElement>().Single();
        Assert(string.Concat(text.Runs.Select(run => run.Text)) == Source &&
            text.Paragraphs is { Count: 7 } paragraphs && paragraphs.Count(p => p.Bullet == "•") == 3 &&
            paragraphs[2].LeftMargin == paragraphs[2].HangingIndent && paragraphs[2].LeftMargin > 0 &&
            paragraphs[4].LeftMargin > paragraphs[4].HangingIndent && paragraphs[6].LeftMargin == 0,
            "Editable exports should carry bullet paragraphs separately from the unchanged source runs.");
        byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
        ExportPage[] pages = [new ExportPage("Prompt", Source, png, 700, 900, elements)];
        using var deck = new MemoryStream();
        PptxDeckWriter.Write(deck, pages, new DeckOptions());
        deck.Position = 0;
        using (var package = new ZipArchive(deck, ZipArchiveMode.Read, leaveOpen: true))
        {
            using var stream = package.GetEntry("ppt/slides/slide1.xml")!.Open();
            var xml = XDocument.Load(stream);
            XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
            var bullets = xml.Descendants(a + "pPr").Where(p => p.Element(a + "buChar") is not null).ToArray();
            Assert(bullets.Length == 3 && bullets.All(p => (int?)p.Attribute("marL") > 0 && (int?)p.Attribute("indent") < 0),
                "PowerPoint should receive native bullets with hanging indents, not bullet-looking text.");
        }
        deck.Position = 0;
        using (var package = DocumentFormat.OpenXml.Packaging.PresentationDocument.Open(deck, false))
        {
            var errors = new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(package).ToArray();
            Assert(errors.Length == 0, "The Prompt deck must be schema-valid: " + string.Join("; ", errors.Select(e => e.Description)));
        }
        using var pdf = new MemoryStream();
        PdfDocumentWriter.Write(pdf, pages, new PdfOptions(PdfPageSize.A4, Landscape: true));
        pdf.Position = 0;
        using var parsed = PdfSharp.Pdf.IO.PdfReader.Open(pdf, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        Assert(parsed.PageCount == 1, "The vector PDF should remain readable with Prompt paragraphs.");
    }

    private static void RenderPreview(string path, TextEditor editor, double height)
    {
        var visual = new DrawingVisual();
        var camera = new Camera2D();
        camera.Resize(780, 700);
        camera.Restore(new PointD(390, 350), 1);
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, 780, 700));
            TextContainerVisual.Draw(context,
                new TextBoardObject(Guid.NewGuid(), 0, new RectD(15, 15, 350, height), "Text", Source, 1, TextLanguageIds.Prompt), camera, 1);
            var bitmap = new RenderTargetBitmap(330, 900, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(editor.TextArea.TextView);
            context.DrawImage(bitmap, new Rect(405, 53, 330, 900));
        }
        var preview = new RenderTargetBitmap(780, 700, 96, 96, PixelFormats.Pbgra32);
        preview.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(preview));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var output = File.Create(path);
        encoder.Save(output);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
