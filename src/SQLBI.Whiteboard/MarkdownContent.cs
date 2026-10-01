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
    private readonly Dictionary<string, MermaidDiagram> _diagrams = new(StringComparer.Ordinal);
    private Task? _preparation;

    private MarkdownContent(string source)
    {
        Document = Markdown.Parse(source, Pipeline);
        DiagramSources = Document.Descendants().OfType<FencedCodeBlock>()
            .Where(MermaidSource.IsDiagram).Select(block => block.Lines.ToString()).Distinct().ToArray();
    }

    public MarkdownDocument Document { get; }
    public IReadOnlyList<string> DiagramSources { get; }
    public bool PreparationStarted => _preparation is not null;

    public Task PrepareDiagramsAsync(IMermaidRenderer renderer) => _preparation ??= PrepareCoreAsync(renderer);

    private async Task PrepareCoreAsync(IMermaidRenderer renderer)
    {
        for (int index = 0; index < DiagramSources.Count; index++)
        {
            string source = DiagramSources[index];
            MermaidDiagram result;
            try
            {
                result = index < MermaidSource.MaximumDiagrams
                    ? await renderer.RenderAsync(source)
                    : MermaidDiagram.Failure("This prototype renders up to 16 diagrams per Markdown container.");
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                result = MermaidDiagram.Failure("The diagram renderer is unavailable.");
            }
            lock (_layouts)
            {
                _diagrams[source] = result;
                _layouts.Clear();
            }
        }
    }

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
                layout = new MarkdownTextLayout(Document, width, _diagrams);
                if (_layouts.Count == 4) _layouts.Clear();
                _layouts.Add(width, layout);
            }

            return layout;
        }
    }
}
