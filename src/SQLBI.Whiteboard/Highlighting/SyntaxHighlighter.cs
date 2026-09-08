using System.IO;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Xml;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace SQLBI.Whiteboard.Highlighting;

/// <summary>
/// Colors a snippet from one of the embedded syntax definitions. The languages
/// with a parser of their own classify their tokens themselves; these are the
/// languages Whiteboard reads lexically, and one definition file each is what
/// they cost.
/// </summary>
/// <remarks>
/// The definitions are Whiteboard's own, written against the XSHD schema of
/// AvalonEdit 6.3.1.120 (MIT), whose bundled definitions were the reference for
/// the schema's idioms rather than the source of these rules. Nothing is loaded
/// at run time from anywhere but this assembly.
/// </remarks>
internal static class SyntaxHighlighter
{
    /// <summary>
    /// Past this, a text container is no longer a snippet and the per-line rule
    /// scanning stops being worth its time - one 429 KB line measured 837 ms.
    /// A longer source is shown uncolored rather than slowly, which is also
    /// what a definition that throws gets.
    /// </summary>
    private const int MaximumAnalyzedLength = 100_000;

    private static readonly ConcurrentDictionary<string, IHighlightingDefinition?> Definitions = new(StringComparer.Ordinal);
    private static readonly DefinitionResolver Resolver = new();

    private static readonly Brush Keyword = CreateBrush(0xFF035ACA);
    private static readonly Brush TypeKeyword = CreateBrush(0xFF267F99);
    private static readonly Brush Function = CreateBrush(0xFF795E26);
    private static readonly Brush StringLiteral = CreateBrush(0xFFA31515);
    private static readonly Brush Number = CreateBrush(0xFFEE7F18);
    private static readonly Brush Comment = CreateBrush(0xFF268E26);
    private static readonly Brush Directive = CreateBrush(0xFF6F42C1);
    private static readonly Brush Variable = CreateBrush(0xFF168C8B);
    private static readonly Brush Operator = CreateBrush(0xFF5E6470);
    private static readonly Brush Punctuation = CreateBrush(0xFF808080);

    /// <summary>
    /// The colors a definition may ask for, shared by every language so that
    /// two snippets side by side read as one board. A definition naming
    /// anything else leaves those characters in the container's text color.
    /// </summary>
    private static readonly Dictionary<string, TextRunStyle> Styles = new(StringComparer.Ordinal)
    {
        ["Comment"] = new(Comment, FontWeights.Normal, FontStyles.Italic),
        ["String"] = new(StringLiteral, FontWeights.Normal, FontStyles.Normal),
        ["Character"] = new(StringLiteral, FontWeights.Normal, FontStyles.Normal),
        // A hole in a string and a variable are the same thing to a reader:
        // a value where text would otherwise be.
        ["Interpolation"] = new(Variable, FontWeights.SemiBold, FontStyles.Normal),
        ["Variable"] = new(Variable, FontWeights.SemiBold, FontStyles.Normal),
        ["Number"] = new(Number, FontWeights.Normal, FontStyles.Normal),
        ["Keyword"] = new(Keyword, FontWeights.Bold, FontStyles.Normal),
        ["TypeKeyword"] = new(TypeKeyword, FontWeights.SemiBold, FontStyles.Normal),
        ["Preprocessor"] = new(Directive, FontWeights.SemiBold, FontStyles.Normal),
        ["Attribute"] = new(Directive, FontWeights.SemiBold, FontStyles.Normal),
        ["Function"] = new(Function, FontWeights.SemiBold, FontStyles.Normal),
        ["Operator"] = new(Operator, FontWeights.SemiBold, FontStyles.Normal),
        ["Punctuation"] = new(Punctuation, FontWeights.Normal, FontStyles.Normal),
    };

    /// <summary>
    /// The whole document at once, in document order: nested grammar styles are
    /// flattened into runs, so the result is ordered, nonoverlapping and within
    /// the source, which is what the colorizer and the exporters read. Every
    /// character of the source is left alone; only what covers it is decided
    /// here, and an unterminated string or comment colors on to where its
    /// language says it ends.
    /// </summary>
    public static IReadOnlyList<StyledTextSpan> Analyze(string source, string definitionName)
    {
        if (string.IsNullOrEmpty(source) ||
            source.Length > MaximumAnalyzedLength ||
            Definition(definitionName) is not { } definition)
        {
            return [];
        }

        try
        {
            return Spans(source, definition);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[Highlighting] {definitionName} failed: {exception.Message}");
            return [];
        }
    }

    private static IReadOnlyList<StyledTextSpan> Spans(string source, IHighlightingDefinition definition)
    {
        // The document and the highlighter belong to this call: both carry the
        // span stack that makes a multiline construct work, and neither is safe
        // to share with the next analysis or the next thread.
        var document = new TextDocument(source);
        using var highlighter = new DocumentHighlighter(document, definition);
        var spans = new List<StyledTextSpan>();
        string?[] painted = [];
        for (int lineNumber = 1; lineNumber <= document.LineCount; lineNumber++)
        {
            DocumentLine line = document.GetLineByNumber(lineNumber);
            HighlightedLine highlighted = highlighter.HighlightLine(lineNumber);
            if (line.Length == 0)
            {
                continue;
            }

            if (painted.Length < line.Length)
            {
                painted = new string?[line.Length];
            }

            Array.Clear(painted, 0, line.Length);
            foreach (HighlightedSection section in highlighted.Sections)
            {
                // An enclosing span paints first and a nested one paints over
                // it, which is the order the highlighter reports them in. A
                // section with no color of its own - an escape sequence inside
                // a string - leaves the color it sits in alone.
                if (section.Color?.Name is not { } category)
                {
                    continue;
                }

                int from = Math.Max(0, section.Offset - line.Offset);
                int to = Math.Min(line.Length, section.Offset + section.Length - line.Offset);
                for (int index = from; index < to; index++)
                {
                    painted[index] = category;
                }
            }

            int start = 0;
            while (start < line.Length)
            {
                if (painted[start] is not { } category)
                {
                    start++;
                    continue;
                }

                int end = start;
                while (end < line.Length &&
                       string.Equals(painted[end], category, StringComparison.Ordinal))
                {
                    end++;
                }

                if (Styles.TryGetValue(category, out TextRunStyle style))
                {
                    spans.Add(new StyledTextSpan(line.Offset + start, end - start, style));
                }

                start = end;
            }
        }

        return spans;
    }

    private static IHighlightingDefinition? Definition(string definitionName) =>
        Definitions.GetOrAdd(definitionName, Load);

    private static IHighlightingDefinition? Load(string definitionName)
    {
        string resource = $"SQLBI.Whiteboard.Highlighting.{definitionName}.xshd";
        try
        {
            using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
            if (stream is null)
            {
                Debug.WriteLine($"[Highlighting] No embedded definition {resource}.");
                return null;
            }

            using XmlReader reader = XmlReader.Create(stream);
            return HighlightingLoader.Load(reader, Resolver);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[Highlighting] Loading {resource} failed: {exception.Message}");
            return null;
        }
    }

    /// <summary>
    /// Lets one definition build on another - TypeScript on JavaScript - and
    /// keeps that resolution inside this assembly, so the bundled definitions
    /// of the same name are never what a rule set reference reaches.
    /// </summary>
    private sealed class DefinitionResolver : IHighlightingDefinitionReferenceResolver
    {
        public IHighlightingDefinition? GetDefinition(string name) => Definition(name);
    }

    private static SolidColorBrush CreateBrush(uint argb)
    {
        var brush = new SolidColorBrush(Color.FromArgb(
            (byte)(argb >> 24),
            (byte)(argb >> 16),
            (byte)(argb >> 8),
            (byte)argb));
        brush.Freeze();
        return brush;
    }
}
