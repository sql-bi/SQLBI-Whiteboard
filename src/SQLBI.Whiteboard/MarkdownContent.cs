using System.Runtime.CompilerServices;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace SQLBI.Whiteboard;

internal sealed class MarkdownContent
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables().UseEmphasisExtras().Build();
    private static readonly ConditionalWeakTable<string, MarkdownContent> Cache = new();
    private readonly Dictionary<double, MarkdownTextLayout> _layouts = [];

    private MarkdownContent(string source) => Document = Markdown.Parse(source, Pipeline);

    public MarkdownDocument Document { get; }

    public static MarkdownContent Parse(string source) => Cache.GetValue(source, text => new(text));

    public static bool LooksLike(string source) => !string.IsNullOrWhiteSpace(source) &&
        Parse(source).Document.Descendants().Any(node => node is
            HeadingBlock or ListBlock or QuoteBlock or FencedCodeBlock or Table or
            EmphasisInline or CodeInline or LinkInline);

    public static int StructureScore(string source) => Parse(source).Document.Descendants().Sum(node => node switch
    {
        Table => 20,
        HeadingBlock or ListBlock or QuoteBlock or FencedCodeBlock => 5,
        EmphasisInline or CodeInline or LinkInline => 1,
        _ => 0,
    });

    public MarkdownTextLayout Layout(double width)
    {
        // Layout is in unscaled board units, never camera pixels. Moving, zooming,
        // and inking only replay a frozen drawing. Keep a few widths for resize/undo.
        width = Math.Max(1, Math.Round(width, 3));
        lock (_layouts)
        {
            if (!_layouts.TryGetValue(width, out var layout))
            {
                layout = new MarkdownTextLayout(Document, width);
                if (_layouts.Count == 4) _layouts.Clear();
                _layouts.Add(width, layout);
            }

            return layout;
        }
    }
}
