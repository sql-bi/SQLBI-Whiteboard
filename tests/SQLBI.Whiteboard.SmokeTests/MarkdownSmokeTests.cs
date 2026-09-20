using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Markdig.Syntax;
using Markdig;
using Markdig.Extensions.Tables;
using SQLBI.Whiteboard.Core.Geometry;
using SQLBI.Whiteboard.Core.Model;
using SQLBI.Whiteboard.Core.Settings;
using SQLBI.Whiteboard.Export;

namespace SQLBI.Whiteboard.SmokeTests;

internal static class MarkdownSmokeTests
{
    private const string Source = """
        # Explaining store eligibility

        Use **all selected years**, not just *the preceding year*. Compare `Sales[Amount]` with the selected period.

        | Rule | What it means | Result |
        | :--- | :--- | ---: |
        | Previous year | A store only needs sales in the year before the current year. | 42 |
        | Entire period | Keep stores open throughout the selected years. This longer explanation wraps inside its own cell. | **28** |
        | Unicode | Café, 世界 and an escaped pipe: A\|B | 7 |

        ## Steps to check

        1. Identify the selected years.
        2. Check each store:
           - Include only stores that cover the whole period.
           - Keep the explanation alongside the table.

        > A missing year is not necessarily a missing store.

        ```sql
        SELECT StoreKey, SUM(Amount) AS Sales
        FROM dbo.Sales
        GROUP BY StoreKey;
        ```

        ---

        [Reference](https://example.com) and ~~an obsolete rule~~.
        """;

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
        if (failure is not null) throw new InvalidOperationException("Markdown smoke tests failed.", failure);
    }

    private static void Check(string? previewPath)
    {
        var language = TextLanguageRegistry.Resolve(TextLanguageIds.Markdown);
        Assert(language.CanDetect && language.WordWrap && !language.CanFormat &&
            language.FormattingRequestUri is null && language.Analyze(Source, "Old").Title == "Markdown" &&
            !language.TryFormat(Source, 65, out var original) && original == Source,
            "Markdown should preserve editable source and never offer a code formatter.");
        Assert(TextLanguageRegistry.ResolveFromOrder(Source, null) == TextLanguageIds.Markdown &&
            TextLanguageRegistry.ResolveFromOrder(Source, [TextLanguageIds.Plain]) == TextLanguageIds.Plain &&
            !language.TryAccept("An ordinary note about sales.") &&
            !language.TryAccept("42") && !language.TryAccept("a * b"),
            "Markdown detection must recognize structure and respect an explicit plain-text-first order.");
        foreach (string snippet in new[] { "# Heading", "- First\n- Second", "1. One\n2. Two",
            "**Bold** and *italic*", "> Quoted", "```sql\nSELECT 1;\n```", "| A | B |\n| --- | --- |\n| 1 | 2 |" })
            Assert(language.TryAccept(snippet), "Markdown detection missed: " + snippet);

        var content = MarkdownContent.Parse(Source);
        Assert(content.Document.Descendants<Table>().Single().Count == 4 &&
            content.Document.Descendants<ListBlock>().Count() == 2,
            "The parser should retain table rows and nested list structure.");
        var layout = content.Layout(620);
        Assert(layout.Drawing.IsFrozen && ReferenceEquals(layout, content.Layout(620)) &&
            ReferenceEquals(content, MarkdownContent.Parse(Source)),
            "Repeated frames must reuse the parsed document and frozen layout.");
        Assert(content.Layout(280).Height > layout.Height && double.IsFinite(layout.Height),
            "Narrow tables and prose must wrap and grow vertically.");
        Assert(Glyphs(layout.Drawing).Select(g => g.ForegroundBrush.ToString()).Distinct().Count() > 3,
            "Inline styling and fenced SQL highlighting must survive into the drawing.");
        foreach (double width in new[] { 160d, 300d, 640d })
        {
            double height = TextContainerVisual.MeasureDesiredHeight(Source, width, 1, 1, TextLanguageIds.Markdown);
            double scaled = TextContainerVisual.MeasureDesiredHeight(Source, width * 2, 2, 2, TextLanguageIds.Markdown);
            Assert(Math.Abs(height * 2 - scaled) < 0.01 &&
                height >= content.Layout(width - 20).Height + 48,
                "Auto-height must fit the final line and scale without reflowing.");
            double sourceHeight = TextContainerVisual.MeasureDesiredHeight(Source, width, 1, 1,
                TextLanguageIds.Markdown, editing: true);
            Assert(double.IsFinite(sourceHeight) && sourceHeight > 60, "The source editor needs its own height.");
        }
        // Malformed, empty, very narrow and non-Latin content should stay drawable.
        foreach (string source in new[] { "", "**unfinished", "## 世界\n\nשלום", "![alt](https://example.invalid/a.png)",
            "| A | B |\n| --- | --- |\n| longlonglonglongword | one<br>two |", "<script>alert(1)</script>" })
            Assert(double.IsFinite(MarkdownContent.Parse(source).Layout(30).Height), "Malformed/unsupported markup must stay safe.");

        CheckClipboard();
        CheckExports(previewPath);
    }

    private static void CheckClipboard()
    {
        var raw = new DataObject(DataFormats.UnicodeText, Source);
        Assert(ClipboardMarkdown.TryGetText(raw, out var source, out bool tagged) && source == Source && !tagged,
            "Copying raw Markdown must preserve the exact source.");
        var explicitData = new DataObject(DataFormats.UnicodeText, "Plain fallback");
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("A simple note — 世界"));
        stream.Position = 3;
        explicitData.SetData(ClipboardMarkdown.Format, stream);
        Assert(ClipboardMarkdown.TryGetText(explicitData, out source, out tagged) &&
            source == "A simple note — 世界" && tagged && stream.Position == 3 && stream.CanRead,
            "An explicit Markdown stream selects the type without consuming the clipboard stream.");
        var invalid = new DataObject();
        invalid.SetData(ClipboardMarkdown.Format, new byte[] { 0xff, 0xfe, 0xff });
        Assert(!ClipboardMarkdown.TryGetText(invalid, out _, out _), "Invalid UTF-8 is an optional hint, not a failed paste.");

        const string html = """
            <h2>Résumé 😀</h2><p>A <strong>bold</strong> and <em>careful</em> answer.</p>
            <table><thead><tr><th>Rule</th><th align="right">Count</th></tr></thead>
            <tbody><tr><td>A | B<br>Second line</td><td><code>a|b</code></td></tr></tbody></table>
            <ol start="3"><li>Check <strong>all</strong> years<ul><li>Include nested items</li></ul></li><li>Finish</li></ol>
            <blockquote>Keep the assumptions.</blockquote>
            <pre><code class="language-sql">SELECT 1;
              -- keep indentation &amp; Unicode café</code></pre>
            <script>SECRET_SCRIPT</script><style>SECRET_STYLE</style><button>Copy code</button>
            <p><a href="javascript:alert(1)">Unsafe link</a><img src="file:///private" alt="not loaded"></p>
            """;
        Assert(ClipboardMarkdown.TryConvertHtml(html, out var converted),
            "Structured HTML must convert to Markdown.");
        Assert(converted.Contains("## Résumé 😀") && converted.Contains("**bold**"),
            "HTML conversion must preserve headings, emphasis, and Unicode.");
        Assert(converted.Contains("```sql\nSELECT 1;\n  -- keep indentation & Unicode café"),
            "HTML code blocks must preserve indentation and Unicode with LF line endings.");
        Assert(!converted.Contains("SECRET") && !converted.Contains("javascript:") &&
            !converted.Contains("file:///") && !converted.Contains("Copy code"),
            "HTML conversion must not import scripts, UI controls, or external resources.");
        CheckClipboardLineEndings(html);
        var parsed = MarkdownContent.Parse(converted).Document;
        Assert(parsed.Descendants<Table>().Single() is { Count: 2 } table &&
            table.OfType<TableRow>().All(row => row.Count == 2) &&
            parsed.Descendants<ListBlock>().Count() == 2 && parsed.Descendants<FencedCodeBlock>().Count() == 1,
            "HTML tables (including pipes inside code), nested lists, and code fences must parse back correctly.");

        var selected = new DataObject(DataFormats.UnicodeText, "Résumé\nRule Count\nA B");
        selected.SetData(DataFormats.Html, ClipboardHtml(html));
        Assert(ClipboardMarkdown.TryGetText(selected, out source, out tagged) &&
            source == converted && !tagged && !source.Contains("Outside"),
            "A selected rich response must honor CF_HTML byte offsets, including non-ASCII text before the fragment.");
        selected.SetData(DataFormats.UnicodeText, "Rule\tCount\nA\t`x`");
        Assert(ClipboardMarkdown.TryGetText(selected, out source, out _) &&
            MarkdownContent.Parse(source).Document.Descendants<Table>().Count() == 1,
            "Markdown-looking text in a flattened table must not hide the richer HTML table.");
        selected.SetData(DataFormats.UnicodeText, "Résumé\nRule Count\nA B");
        selected.SetData(DataFormats.Html, "StartFragment:9999999\r\nEndFragment:1\r\n<!--StartFragment-->" + html + "<!--EndFragment-->");
        Assert(ClipboardMarkdown.TryGetText(selected, out source, out _) && source == converted,
            "Bad offsets should fall back to fragment markers.");
        Assert(!ClipboardMarkdown.TryConvertHtml("<div>ordinary plain text</div>", out _),
            "Unstructured HTML must leave existing plain-text/code paste behavior alone.");
        var complete = new DataObject(DataFormats.UnicodeText, Source);
        complete.SetData(DataFormats.Html, Markdig.Markdown.ToHtml(Source,
            new MarkdownPipelineBuilder().UsePipeTables().UseEmphasisExtras().Build()));
        Assert(ClipboardMarkdown.TryGetText(complete, out source, out _) && source == Source,
            "When HTML and Markdown are equivalent, keep the original Markdown verbatim.");
        Assert(ClipboardMarkdown.TryGetContainerText(selected, [TextLanguageIds.Plain], out source, out var languageId) &&
            languageId == TextLanguageIds.Plain && source == "Résumé\nRule Count\nA B",
            "Plain-text-first must keep the clipboard's plain text, not converted Markdown syntax.");
        var code = new DataObject(DataFormats.UnicodeText, "SELECT CustomerKey FROM dbo.Customer;");
        code.SetData(DataFormats.Html, "<pre><code>SELECT CustomerKey FROM dbo.Customer;</code></pre>");
        Assert(ClipboardMarkdown.TryGetContainerText(code, null, out source, out languageId) &&
            languageId == TextLanguageIds.SqlServer && source == "SELECT CustomerKey FROM dbo.Customer;",
            "A copied SQL snippet with accompanying HTML must retain existing SQL detection.");
        Assert(ClipboardMarkdown.TryGetContainerText(complete, null, out source, out languageId) &&
            languageId == TextLanguageIds.Markdown && source == Source,
            "A whole Markdown answer should paste directly as a Markdown container.");
        explicitData.SetData(ClipboardMarkdown.Format, "SELECT 1;");
        Assert(ClipboardMarkdown.TryGetContainerText(explicitData, [TextLanguageIds.Plain], out source, out languageId) &&
            languageId == TextLanguageIds.Markdown && source == "SELECT 1;",
            "An explicitly copied Markdown container must keep its type regardless of syntax or detection order.");
        var prompt = new DataObject(DataFormats.UnicodeText, "- Keep Prompt");
        prompt.SetData(ClipboardPrompt.MetadataFormat, """{"type":"Prompt","version":1}""");
        Assert(ClipboardPrompt.TryGetText(prompt, out source) && source == "- Keep Prompt",
            "The existing explicit Prompt clipboard metadata must keep working.");
    }

    private static void CheckClipboardLineEndings(string html)
    {
        Assert(ClipboardMarkdown.TryConvertHtml(html.ReplaceLineEndings("\n"), out var expected),
            "The LF HTML fixture must convert.");
        foreach (var (name, newline) in new[] { ("LF", "\n"), ("CRLF", "\r\n"), ("CR", "\r") })
        {
            string input = html.ReplaceLineEndings(newline);
            Assert(ClipboardMarkdown.TryConvertHtml(input, out var converted) && converted == expected,
                $"{name} HTML must produce the same Markdown as LF HTML.");
            Assert(ClipboardMarkdown.TryConvertHtml(ClipboardHtml(input), out converted) && converted == expected,
                $"{name} CF_HTML must honor original UTF-8 byte offsets before normalizing code line endings.");
            Assert(ClipboardMarkdown.TryConvertHtml($"<p><code>first{newline}second</code></p>", out converted) &&
                converted == "` first second `",
                $"{name} line endings inside inline code must become one space.");

            string original = Source.ReplaceLineEndings(newline);
            var raw = new DataObject(DataFormats.UnicodeText, original);
            Assert(ClipboardMarkdown.TryGetText(raw, out var text, out bool tagged) && text == original && !tagged,
                $"Raw {name} Markdown must keep its original line endings.");
            raw.SetData(DataFormats.Html, Markdig.Markdown.ToHtml(original,
                new MarkdownPipelineBuilder().UsePipeTables().UseEmphasisExtras().Build()));
            Assert(ClipboardMarkdown.TryGetText(raw, out text, out tagged) && text == original && !tagged,
                $"Raw {name} Markdown must stay verbatim when equivalent HTML accompanies it.");
            raw.SetData(ClipboardMarkdown.Format, Encoding.UTF8.GetBytes(original));
            Assert(ClipboardMarkdown.TryGetText(raw, out text, out tagged) && text == original && tagged,
                $"Explicit UTF-8 {name} Markdown must keep its original line endings.");
        }
        Assert(ClipboardMarkdown.TryConvertHtml("<pre>first\r\n  second\r    third\nfourth</pre>", out var mixed) &&
            mixed == "```\nfirst\n  second\n    third\nfourth\n```",
            "Mixed code-block line endings must normalize without changing indentation.");
    }

    private static string ClipboardHtml(string html)
    {
        // No fragment markers: these cases must use the offsets, not the fallback.
        const string before = "<html><body>é😀\r\n<strong>Outside before</strong>\r\n";
        const string after = "\r\n<strong>Outside after</strong></body></html>";
        const string headerTemplate = "Version:1.0\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
        int headerLength = Encoding.UTF8.GetByteCount(string.Format(headerTemplate, 0, 0, 0, 0));
        int fragmentStart = headerLength + Encoding.UTF8.GetByteCount(before);
        int fragmentEnd = fragmentStart + Encoding.UTF8.GetByteCount(html);
        return string.Format(headerTemplate, headerLength,
            fragmentEnd + Encoding.UTF8.GetByteCount(after), fragmentStart, fragmentEnd) + before + html + after;
    }

    private static void CheckExports(string? previewPath)
    {
        double height = TextContainerVisual.MeasureDesiredHeight(Source, 640, 1, 1, TextLanguageIds.Markdown);
        var document = new BoardDocument();
        var text = new TextBoardObject(Guid.NewGuid(), 0, new RectD(0, 0, 640, height),
            "Text", Source, 1, TextLanguageIds.Markdown);
        document.AddObject(text);
        var bitmap = BoardRasterizer.Render(document, text.Bounds, 640, (int)Math.Ceiling(height), paddingFraction: 0);
        var area = BoardExporter.Areas(document, new ExportSettings { PageModel = ExportPageModel.WholeBoard }, null).Single();
        var elements = EditableSlide.Build(document, area, 1280, 1400, null, inkAsStrokes: true);
        Assert(elements.Count == 1 && elements[0] is SlideImageElement,
            "Editable/vector export should preserve Markdown appearance as a picture, never expose raw syntax.");
        ExportPage[] pages = [new ExportPage("Markdown", Source, WpfImageCodec.EncodePng(bitmap), 640,
            (int)Math.Ceiling(height), elements)];
        using var deck = new MemoryStream();
        PptxDeckWriter.Write(deck, pages, new DeckOptions());
        deck.Position = 0;
        using (var package = DocumentFormat.OpenXml.Packaging.PresentationDocument.Open(deck, false))
        {
            var errors = new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(package).ToArray();
            Assert(errors.Length == 0, "The Markdown deck must be schema-valid.");
        }
        deck.Position = 0;
        using (var zip = new ZipArchive(deck, ZipArchiveMode.Read, leaveOpen: true))
        {
            Assert(zip.Entries.Any(entry => entry.FullName.StartsWith("ppt/media/", StringComparison.Ordinal)),
                "The rendered Markdown must be embedded in the deck.");
            using var notes = new StreamReader(zip.GetEntry("ppt/notesSlides/notesSlide1.xml")!.Open());
            Assert(notes.ReadToEnd().Contains("Explaining store eligibility"), "Markdown source must remain in speaker notes.");
        }
        using var pdf = new MemoryStream();
        PdfDocumentWriter.Write(pdf, pages, new PdfOptions(PdfPageSize.A4, Landscape: true));
        pdf.Position = 0;
        using var parsed = PdfSharp.Pdf.IO.PdfReader.Open(pdf, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        Assert(parsed.PageCount == 1, "The PDF must remain readable.");
        if (!string.IsNullOrWhiteSpace(previewPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(previewPath))!);
            using var output = File.Create(previewPath);
            output.Write(WpfImageCodec.EncodePng(bitmap));
        }
    }

    private static IEnumerable<GlyphRunDrawing> Glyphs(Drawing drawing)
    {
        if (drawing is GlyphRunDrawing glyph) yield return glyph;
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children)
                foreach (var item in Glyphs(child)) yield return item;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
