using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using SQLBI.Whiteboard.Core.Model;

namespace SQLBI.Whiteboard;

internal sealed class MarkdownTextLayout
{
    private const double FontSize = TextContainerVisual.BodyFontSize;
    private static readonly Brush Ink = Brush(0xFF1F2937);
    private static readonly Brush Muted = Brush(0xFF64748B);
    private static readonly Brush Accent = Brush(0xFF2563EB);
    private static readonly Brush CodeInk = Brush(0xFF9A3412);
    private static readonly Brush CodeBackground = Brush(0xFFF1F5F9);
    private static readonly Brush AlternateRow = Brush(0xFFF8FAFC);
    private static readonly Pen Rule = new(Brush(0xFFCBD5E1), 1);

    public MarkdownTextLayout(MarkdownDocument document, double width)
    {
        (Drawing, Height) = Blocks(document, width);
        Drawing.Freeze();
    }

    public DrawingGroup Drawing { get; }
    public double Height { get; }

    private static (DrawingGroup Drawing, double Height) Blocks(ContainerBlock blocks, double width)
    {
        var drawing = new DrawingGroup();
        double y = 0;
        using (var context = drawing.Open())
        {
            foreach (Block block in blocks)
            {
                if (y > 0) y += 10;
                var item = Block(block, width);
                DrawAt(context, item.Drawing, 0, y);
                y += item.Height;
            }
        }

        return (drawing, Math.Max(FontSize * 1.25, y));
    }

    private static (DrawingGroup Drawing, double Height) Block(Block block, double width)
    {
        var drawing = new DrawingGroup();
        double height = 0;
        using (var context = drawing.Open())
        {
            switch (block)
            {
                case HeadingBlock heading:
                    var headingText = InlineText(heading.Inline, width,
                        heading.Level switch { 1 => 30, 2 => 25, 3 => 22, _ => 19 }, bold: true);
                    context.DrawText(headingText, new Point(0, 2));
                    height = headingText.Height + 6;
                    break;
                case ParagraphBlock paragraph:
                    var text = InlineText(paragraph.Inline, width);
                    context.DrawText(text, new Point());
                    height = text.Height;
                    break;
                case Table table:
                    height = DrawTable(context, table, width);
                    break;
                case ListBlock list:
                    int number = int.TryParse(list.OrderedStart, out int first) ? first : 1;
                    foreach (var child in list.OfType<ListItemBlock>())
                    {
                        string marker = list.IsOrdered ? $"{number++}." : "•";
                        var label = PlainText(marker, FontSize);
                        double indent = Math.Min(width / 2, Math.Max(26, label.Width + 9));
                        var item = Blocks(child, Math.Max(1, width - indent));
                        context.DrawText(label, new Point(Math.Max(0, indent - label.Width - 8), height));
                        DrawAt(context, item.Drawing, indent, height);
                        height += item.Height + (list.IsLoose ? 10 : 4);
                    }
                    height = Math.Max(0, height - (list.IsLoose ? 10 : 4));
                    break;
                case QuoteBlock quote:
                    var quoted = Blocks(quote, Math.Max(1, width - 20));
                    height = quoted.Height + 8;
                    context.DrawRectangle(CodeBackground, null, new Rect(0, 0, width, height));
                    context.DrawRectangle(Muted, null, new Rect(0, 0, Math.Min(3, width), height));
                    DrawAt(context, quoted.Drawing, Math.Min(16, width / 2), 4);
                    break;
                case CodeBlock code:
                    string source = code.Lines.ToString();
                    var formatted = PlainText(source, 16, monospace: true);
                    formatted.MaxTextWidth = Math.Max(1, width - 20);
                    if (code is FencedCodeBlock fence)
                    {
                        string? id = CodeLanguage(fence.Info);
                        if (id is not null)
                        {
                            foreach (var span in TextLanguageRegistry.Resolve(id).Analyze(source, "").Spans)
                            {
                                if (span.Start < 0 || span.Length <= 0 || span.Start + span.Length > source.Length) continue;
                                formatted.SetForegroundBrush(span.Style.Foreground, span.Start, span.Length);
                                formatted.SetFontWeight(span.Style.FontWeight, span.Start, span.Length);
                                formatted.SetFontStyle(span.Style.FontStyle, span.Start, span.Length);
                            }
                        }
                    }
                    height = formatted.Height + 20;
                    context.DrawRoundedRectangle(CodeBackground, null, new Rect(0, 0, width, height), 4, 4);
                    context.PushClip(new RectangleGeometry(new Rect(0, 0, width, height)));
                    context.DrawText(formatted, new Point(10, 10));
                    context.Pop();
                    break;
                case ThematicBreakBlock:
                    context.DrawLine(Rule, new Point(0, 8), new Point(width, 8));
                    height = 16;
                    break;
                case HtmlBlock html:
                    // Markup is text, never a browser surface. It cannot execute scripts,
                    // navigate, or load an image/file named by the pasted document.
                    var literal = PlainText(html.Lines.ToString(), FontSize);
                    literal.MaxTextWidth = Math.Max(1, width);
                    context.DrawText(literal, new Point());
                    height = literal.Height;
                    break;
                case ContainerBlock container:
                    var nested = Blocks(container, width);
                    context.DrawDrawing(nested.Drawing);
                    height = nested.Height;
                    break;
            }
        }

        return (drawing, height);
    }

    private static double DrawTable(DrawingContext context, Table table, double width)
    {
        var rows = table.OfType<TableRow>().ToArray();
        int count = Math.Max(1, rows.Select(row => row.Count).DefaultIfEmpty(1).Max());
        var preferred = Enumerable.Repeat(70d, count).ToArray();
        foreach (var row in rows)
        {
            for (int column = 0; column < row.Count; column++)
            {
                var cell = (TableCell)row[column];
                double natural = cell.OfType<ParagraphBlock>()
                    .Select(p => InlineText(p.Inline, 10000, bold: row.IsHeader).WidthIncludingTrailingWhitespace)
                    .DefaultIfEmpty(70).Max();
                preferred[column] = Math.Max(preferred[column], Math.Min(350, natural + 16));
            }
        }

        // Every column receives a readable base share; the remaining width goes
        // to the longer cells. Even a long URL cannot push the table off the page.
        double total = preferred.Sum();
        double minimum = Math.Min(90, width / count / 2);
        double[] widths = preferred.Select(value => minimum + (width - minimum * count) * value / total).ToArray();
        double y = 0;
        for (int rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            var row = rows[rowIndex];
            var cells = new List<(DrawingGroup Drawing, double Height)>();
            for (int column = 0; column < count; column++)
            {
                var cellDrawing = new DrawingGroup();
                double cellHeight = 0;
                if (column < row.Count)
                {
                    using var cellContext = cellDrawing.Open();
                    foreach (var paragraph in ((TableCell)row[column]).OfType<ParagraphBlock>())
                    {
                        var text = InlineText(paragraph.Inline, Math.Max(1, widths[column] - 16), bold: row.IsHeader);
                        if (column < table.ColumnDefinitions.Count)
                        {
                            text.TextAlignment = table.ColumnDefinitions[column].Alignment switch
                            {
                                TableColumnAlign.Center => TextAlignment.Center,
                                TableColumnAlign.Right => TextAlignment.Right,
                                _ => TextAlignment.Left,
                            };
                        }
                        cellContext.DrawText(text, new Point(0, cellHeight));
                        cellHeight += text.Height;
                    }
                }
                cells.Add((cellDrawing, cellHeight));
            }

            double height = Math.Max(FontSize * 1.25, cells.Max(cell => cell.Height)) + 16;
            double x = 0;
            for (int column = 0; column < count; column++)
            {
                var rect = new Rect(x, y, widths[column], height);
                context.DrawRectangle(row.IsHeader ? CodeBackground : rowIndex % 2 == 0 ? AlternateRow : Brushes.White, Rule, rect);
                context.PushClip(new RectangleGeometry(rect));
                DrawAt(context, cells[column].Drawing, x + 8, y + 8);
                context.Pop();
                x += widths[column];
            }
            y += height;
        }
        return y;
    }

    private readonly record struct InlineStyle(bool Bold = false, bool Italic = false,
        bool Code = false, bool Link = false, bool Strike = false);
    private readonly record struct Run(int Start, int Length, InlineStyle Style);

    private static FormattedText InlineText(ContainerInline? inline, double width, double size = FontSize, bool bold = false)
    {
        var source = new StringBuilder();
        var runs = new List<Run>();
        if (inline is not null) Append(inline, new InlineStyle(Bold: bold), source, runs);
        var text = PlainText(source.ToString(), size);
        text.MaxTextWidth = Math.Max(1, width);
        foreach (var run in runs)
        {
            if (run.Length == 0) continue;
            if (run.Style.Bold) text.SetFontWeight(FontWeights.Bold, run.Start, run.Length);
            if (run.Style.Italic) text.SetFontStyle(FontStyles.Italic, run.Start, run.Length);
            if (run.Style.Code)
            {
                text.SetFontFamily("Consolas", run.Start, run.Length);
                text.SetForegroundBrush(CodeInk, run.Start, run.Length);
            }
            if (run.Style.Link)
            {
                text.SetForegroundBrush(Accent, run.Start, run.Length);
                text.SetTextDecorations(TextDecorations.Underline, run.Start, run.Length);
            }
            if (run.Style.Strike) text.SetTextDecorations(TextDecorations.Strikethrough, run.Start, run.Length);
        }
        return text;
    }

    private static void Append(Inline inline, InlineStyle style, StringBuilder text, List<Run> runs)
    {
        string? value = null;
        switch (inline)
        {
            case LiteralInline literal: value = literal.Content.ToString(); break;
            case CodeInline code: value = code.Content; style = style with { Code = true }; break;
            case LineBreakInline line: value = line.IsHard ? "\n" : " "; break;
            case HtmlEntityInline entity: value = entity.Transcoded.ToString(); break;
            case HtmlInline html: value = html.Tag.Equals("<br>", StringComparison.OrdinalIgnoreCase) ||
                html.Tag.Equals("<br/>", StringComparison.OrdinalIgnoreCase) ||
                html.Tag.Equals("<br />", StringComparison.OrdinalIgnoreCase) ? "\n" : html.Tag; break;
            case AutolinkInline link: value = link.Url; style = style with { Link = true }; break;
            case EmphasisInline emphasis:
                style = emphasis.DelimiterChar == '~' ? style with { Strike = true } :
                    emphasis.DelimiterCount >= 2 ? style with { Bold = true } : style with { Italic = true };
                break;
            case LinkInline link:
                style = style with { Link = !link.IsImage };
                if (link.IsImage)
                {
                    text.Append("[Image: ");
                    foreach (var child in link) Append(child, style, text, runs);
                    text.Append(']');
                    return;
                }
                break;
        }
        if (value is not null)
        {
            runs.Add(new Run(text.Length, value.Length, style));
            text.Append(value);
        }
        else if (inline is ContainerInline container)
        {
            foreach (var child in container) Append(child, style, text, runs);
        }
    }

    private static FormattedText PlainText(string text, double size, bool monospace = false) => new(
        string.IsNullOrEmpty(text) ? " " : text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
        new Typeface(monospace ? "Consolas" : "Segoe UI"), size, Ink, 1);

    private static void DrawAt(DrawingContext context, DrawingGroup drawing, double x, double y)
    {
        context.PushTransform(new TranslateTransform(x, y));
        context.DrawDrawing(drawing);
        context.Pop();
    }

    private static string? CodeLanguage(string? info) => info?.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .FirstOrDefault()?.ToLowerInvariant() switch
        {
            "dax" => TextLanguageIds.Dax, "sql" or "tsql" => TextLanguageIds.SqlServer,
            "kql" or "kusto" => TextLanguageIds.Kql, "python" or "py" => TextLanguageIds.Python,
            "csharp" or "cs" or "c#" => TextLanguageIds.CSharp, "javascript" or "js" => TextLanguageIds.JavaScript,
            "typescript" or "ts" => TextLanguageIds.TypeScript, "cpp" or "c++" => TextLanguageIds.Cpp,
            "c" => TextLanguageIds.C, "java" => TextLanguageIds.Java, "r" => TextLanguageIds.R,
            "rust" or "rs" => TextLanguageIds.Rust, "php" => TextLanguageIds.Php,
            "vb" or "vbnet" => TextLanguageIds.VbNet, _ => null,
        };

    private static Brush Brush(uint argb)
    {
        var brush = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        brush.Freeze();
        return brush;
    }
}
