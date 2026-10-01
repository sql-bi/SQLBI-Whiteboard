using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using SQLBI.Whiteboard.Core.Model;

namespace SQLBI.Whiteboard;

internal sealed class MarkdownContent
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables().UseEmphasisExtras().Build();
    private static readonly ConditionalWeakTable<string, MarkdownContent> Cache = new();
    private readonly Dictionary<double, MarkdownTextLayout> _layouts = [];
    private readonly Dictionary<string, MermaidDiagram> _diagrams = new(StringComparer.Ordinal);
    private Task? _preparation;
    private Task? _restoration;

    private MarkdownContent(string source)
    {
        Document = Markdown.Parse(source, Pipeline);
        DiagramSources = Document.Descendants().OfType<FencedCodeBlock>()
            .Where(MermaidSource.IsDiagram).Select(block => block.Lines.ToString()).Distinct().ToArray();
    }

    public MarkdownDocument Document { get; }
    public IReadOnlyList<string> DiagramSources { get; }
    public bool PreparationStarted => _preparation is not null;
    public bool DiagramsReady { get; private set; }

    public Task PrepareDiagramsAsync(IMermaidRenderer renderer, ImmutableArray<MermaidSnapshot> snapshots = default) =>
        _preparation ??= PrepareCoreAsync(renderer, snapshots);

    public Task RestoreSnapshotsAsync(ImmutableArray<MermaidSnapshot> snapshots) =>
        snapshots.IsDefaultOrEmpty || DiagramsReady
            ? Task.CompletedTask
            : _restoration ??= RestoreCoreAsync(snapshots);

    private async Task RestoreCoreAsync(ImmutableArray<MermaidSnapshot> snapshots)
    {
        var restored = await Task.Run(() =>
        {
            var results = new Dictionary<string, MermaidDiagram>(StringComparer.Ordinal);
            var sources = DiagramSources.Take(MermaidSource.MaximumDiagrams).ToHashSet(StringComparer.Ordinal);
            foreach (var snapshot in snapshots.Take(MermaidSource.MaximumDiagrams))
            {
                if (!snapshot.IsWithinLimits() || !sources.Contains(snapshot.Source) ||
                    MermaidSource.Validate(snapshot.Source) is not null || results.ContainsKey(snapshot.Source)) continue;
                try { results.Add(snapshot.Source, new(MermaidSvg.Decode(snapshot.Svg), null, snapshot.Svg)); }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    Debug.WriteLine($"[Mermaid] ignoring invalid snapshot: {exception.Message}");
                }
            }
            return results;
        });
        lock (_layouts)
        {
            foreach (var (source, result) in restored) _diagrams.TryAdd(source, result);
            _layouts.Clear();
            DiagramsReady = _diagrams.Count == DiagramSources.Count;
        }
    }

    public ImmutableArray<MermaidSnapshot> Snapshots()
    {
        lock (_layouts)
        {
            return DiagramSources.Take(MermaidSource.MaximumDiagrams)
                .Where(source => _diagrams.TryGetValue(source, out var result) && result.Image is not null && result.Svg is not null)
                .Select(source => new MermaidSnapshot(source, _diagrams[source].Svg!))
                .Where(snapshot => snapshot.IsWithinLimits()).ToImmutableArray();
        }
    }

    private async Task PrepareCoreAsync(IMermaidRenderer renderer, ImmutableArray<MermaidSnapshot> snapshots)
    {
        await RestoreSnapshotsAsync(snapshots);
        var results = new Dictionary<string, MermaidDiagram>(StringComparer.Ordinal);
        for (int index = 0; index < DiagramSources.Count; index++)
        {
            string source = DiagramSources[index];
            if (_diagrams.TryGetValue(source, out var saved) && saved.Image is not null)
            {
                results[source] = saved;
                continue;
            }
            MermaidDiagram result;
            try
            {
                result = index < MermaidSource.MaximumDiagrams
                    ? await renderer.RenderAsync(source)
                    : MermaidDiagram.Failure("This prototype renders up to 16 diagrams per Markdown container.");
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                result = MermaidDiagram.Failure("The local diagram renderer is unavailable. Check the WebView2 Runtime and restart Whiteboard.");
            }
            results[source] = result;
        }
        // Publish one layout change per source, so several diagrams do not
        // repeatedly move the text that follows them as each one finishes.
        lock (_layouts)
        {
            foreach (var (source, result) in results) _diagrams[source] = result;
            _layouts.Clear();
            DiagramsReady = true;
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
