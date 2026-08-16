using System.Text;

namespace SQLBI.Whiteboard.Dax.Engine;

/// <summary>
/// A layout document. A group is printed on one line when it fits within the line limit, and
/// otherwise every separator inside it becomes a line break. This is what produces the
/// "all arguments on one line, or one argument per line" behaviour DAX code is normally written in.
/// </summary>
internal abstract class Doc
{
    /// <summary>True when the document contains something that can never be printed flat.</summary>
    public bool ForcesBreak { get; protected init; }

    public static readonly Doc Empty = new TextDoc(string.Empty);

    /// <summary>Prints nothing but stops every enclosing group from collapsing onto one line.</summary>
    public static readonly Doc BreakParent = new BreakParentDoc();

    public static Doc Text(string text) => text.Length == 0 ? Empty : new TextDoc(text);

    /// <summary>A separator that prints as <paramref name="inline"/> on one line, or a line break.</summary>
    public static Doc Line(string inline = " ") => new LineDoc(inline);

    /// <summary>A line break that is always taken, optionally preceded by blank lines.</summary>
    public static Doc Hard(int blankLines = 0) => new HardLineDoc(blankLines);

    public static Doc Concat(params Doc[] parts) => Concat((IReadOnlyList<Doc>)parts);

    public static Doc Concat(IReadOnlyList<Doc> parts)
    {
        var kept = parts.Where(part => part is not TextDoc { Content.Length: 0 }).ToArray();
        return kept.Length switch
        {
            0 => Empty,
            1 => kept[0],
            _ => new ConcatDoc(kept)
        };
    }

    public static Doc Indent(int levels, Doc content) => levels == 0 ? content : new IndentDoc(levels, content);

    public static Doc Group(Doc content) => new GroupDoc(content);

    /// <summary>Content that is always printed on one line, whatever the width.</summary>
    public static Doc Flat(Doc content) => new FlatDoc(content);

    /// <summary>
    /// Content held back until the end of the current line. A comment written after code belongs
    /// there whatever the layout does with the code that follows it.
    /// </summary>
    public static Doc LineSuffix(string text) => new LineSuffixDoc(text);

    /// <summary>
    /// Alternative layouts for the same code, in order of preference. The first whose every line
    /// fits is printed; if none do, the last is used as the arrangement that always works.
    /// </summary>
    public static Doc Choose(params Doc[] alternatives) => new ChoiceDoc(alternatives);

    public static Doc Join(Doc separator, IEnumerable<Doc> items)
    {
        var parts = new List<Doc>();
        foreach (var item in items)
        {
            if (parts.Count > 0) parts.Add(separator);
            parts.Add(item);
        }
        return Concat(parts);
    }

    internal sealed class TextDoc : Doc
    {
        public TextDoc(string content) => Content = content;
        public string Content { get; }
    }

    internal sealed class BreakParentDoc : Doc
    {
        public BreakParentDoc() => ForcesBreak = true;
    }

    internal sealed class LineDoc : Doc
    {
        public LineDoc(string inline) => Inline = inline;
        public string Inline { get; }
    }

    internal sealed class HardLineDoc : Doc
    {
        public HardLineDoc(int blankLines)
        {
            BlankLines = blankLines;
            ForcesBreak = true;
        }
        public int BlankLines { get; }
    }

    internal sealed class ConcatDoc : Doc
    {
        public ConcatDoc(IReadOnlyList<Doc> parts)
        {
            Parts = parts;
            ForcesBreak = parts.Any(part => part.ForcesBreak);
        }
        public IReadOnlyList<Doc> Parts { get; }
    }

    internal sealed class IndentDoc : Doc
    {
        public IndentDoc(int levels, Doc content)
        {
            Levels = levels;
            Content = content;
            ForcesBreak = content.ForcesBreak;
        }
        public int Levels { get; }
        public Doc Content { get; }
    }

    internal sealed class GroupDoc : Doc
    {
        public GroupDoc(Doc content)
        {
            Content = content;
            ForcesBreak = content.ForcesBreak;
        }
        public Doc Content { get; }
    }

    internal sealed class FlatDoc : Doc
    {
        public FlatDoc(Doc content) => Content = content;
        public Doc Content { get; }
    }

    internal sealed class LineSuffixDoc : Doc
    {
        public LineSuffixDoc(string content) => Content = content;
        public string Content { get; }
    }

    internal sealed class ChoiceDoc : Doc
    {
        public ChoiceDoc(IReadOnlyList<Doc> alternatives)
        {
            Alternatives = alternatives;
            // Every alternative holds the same code, so a comment in one forces a break in all.
            ForcesBreak = alternatives.Count > 0 && alternatives[^1].ForcesBreak;
        }
        public IReadOnlyList<Doc> Alternatives { get; }
    }
}

internal static class DocRenderer
{
    private const int SpacesPerLevel = 4;

    public static string Render(Doc document, int maximumLineLength)
    {
        var output = new StringBuilder();
        var column = 0;

        // Comments written after code wait here until the line they belong to ends.
        var suffixes = new List<string>();

        // A list is used as the stack so that Fits can walk the queued documents without copying.
        var pending = new List<(int Indent, bool Flat, Doc Doc)> { (0, false, document) };

        while (pending.Count > 0)
        {
            var (indent, flat, doc) = pending[^1];
            pending.RemoveAt(pending.Count - 1);
            switch (doc)
            {
                case Doc.TextDoc text:
                    output.Append(text.Content);
                    column = ColumnAfter(column, text.Content);
                    break;

                case Doc.ConcatDoc concat:
                    for (var index = concat.Parts.Count - 1; index >= 0; index--)
                        pending.Add((indent, flat, concat.Parts[index]));
                    break;

                case Doc.IndentDoc nested:
                    pending.Add((indent + nested.Levels, flat, nested.Content));
                    break;

                case Doc.GroupDoc group:
                {
                    // A comment is waiting for the end of the line, so this group has to provide
                    // one; laid out flat it would run the comment into the code that follows.
                    var printFlat = !group.ForcesBreak &&
                                    suffixes.Count == 0 &&
                                    Fits(group.Content, maximumLineLength - column, pending);
                    pending.Add((indent, printFlat, group.Content));
                    break;
                }

                case Doc.FlatDoc forced:
                    pending.Add((indent, true, forced.Content));
                    break;

                case Doc.LineSuffixDoc suffix:
                    suffixes.Add(suffix.Content);
                    break;

                case Doc.ChoiceDoc choice:
                    pending.Add((indent, flat, Select(choice, column, indent, maximumLineLength, pending)));
                    break;

                case Doc.LineDoc line when flat && suffixes.Count == 0:
                    output.Append(line.Inline);
                    column += line.Inline.Length;
                    break;

                case Doc.LineDoc:
                    column = BreakLine(output, indent, 0, suffixes);
                    break;

                case Doc.HardLineDoc hard:
                    column = BreakLine(output, indent, hard.BlankLines, suffixes);
                    break;
            }
        }

        foreach (var suffix in suffixes) output.Append(suffix);
        return TrimTrailingSpaces(output.ToString());
    }

    /// <summary>
    /// Picks the first alternative that fits on every line it produces, falling back to the last.
    /// A binary expression offers its whole chain on one line, then the chain broken at its
    /// operators, then the operators kept inline with the operands free to break, which is how
    /// "CALCULATE ( ... ) &gt; 800000" keeps its comparison attached to the closing bracket.
    /// </summary>
    private static Doc Select(
        Doc.ChoiceDoc choice,
        int column,
        int indent,
        int maximumLineLength,
        List<(int Indent, bool Flat, Doc Doc)> rest)
    {
        for (var index = 0; index < choice.Alternatives.Count - 1; index++)
        {
            if (EveryLineFits(choice.Alternatives[index], column, indent, maximumLineLength, rest))
                return choice.Alternatives[index];
        }
        return choice.Alternatives[^1];
    }

    /// <summary>
    /// Lays an alternative out without emitting it, and reports whether every line stays within the
    /// limit. Measuring continues into the documents queued after it, so the closing bracket and the
    /// comma that follow are taken into account.
    /// </summary>
    private static bool EveryLineFits(
        Doc content,
        int column,
        int indent,
        int maximumLineLength,
        List<(int Indent, bool Flat, Doc Doc)> rest)
    {
        var queue = new Stack<(int Indent, bool Flat, Doc Doc)>();
        queue.Push((indent, false, content));
        var restIndex = rest.Count - 1;

        while (true)
        {
            if (queue.Count == 0)
            {
                if (restIndex < 0) return column <= maximumLineLength;
                var item = rest[restIndex--];
                queue.Push(item);
                continue;
            }

            var (currentIndent, flat, doc) = queue.Pop();
            switch (doc)
            {
                case Doc.TextDoc text:
                    column = ColumnAfter(column, text.Content);
                    if (column > maximumLineLength) return false;
                    break;

                case Doc.ConcatDoc concat:
                    for (var index = concat.Parts.Count - 1; index >= 0; index--)
                        queue.Push((currentIndent, flat, concat.Parts[index]));
                    break;

                case Doc.IndentDoc nested:
                    queue.Push((currentIndent + nested.Levels, flat, nested.Content));
                    break;

                case Doc.FlatDoc forced:
                    queue.Push((currentIndent, true, forced.Content));
                    break;

                case Doc.LineSuffixDoc:
                    break;

                case Doc.ChoiceDoc nestedChoice:
                    queue.Push((currentIndent, flat, flat ? nestedChoice.Alternatives[0] : nestedChoice.Alternatives[^1]));
                    break;

                case Doc.GroupDoc group:
                    queue.Push((currentIndent, flat || (!group.ForcesBreak && Fits(group.Content, maximumLineLength - column, [])), group.Content));
                    break;

                case Doc.LineDoc line when flat:
                    column += line.Inline.Length;
                    if (column > maximumLineLength) return false;
                    break;

                case Doc.LineDoc:
                case Doc.HardLineDoc:
                    // The line so far is within the limit, so measuring restarts at the indentation.
                    column = currentIndent * SpacesPerLevel;
                    break;
            }
        }
    }

    private static int BreakLine(StringBuilder output, int indent, int blankLines, List<string> suffixes)
    {
        while (output.Length > 0 && output[^1] == ' ') output.Length--;
        if (suffixes.Count > 0)
        {
            foreach (var suffix in suffixes) output.Append(suffix);
            suffixes.Clear();
        }
        for (var index = 0; index <= blankLines; index++) output.Append('\n');
        var width = indent * SpacesPerLevel;
        output.Append(' ', width);
        return width;
    }

    /// <summary>
    /// Decides whether a group can stay on the current line. The content is measured flat, and the
    /// documents already queued after it are measured too, so a closing bracket or a trailing comma
    /// cannot silently push the line past the limit.
    /// </summary>
    private static bool Fits(Doc content, int remaining, List<(int Indent, bool Flat, Doc Doc)> rest)
    {
        if (remaining < 0) return false;

        var queue = new Stack<(bool Flat, Doc Doc)>();
        queue.Push((true, content));
        var restIndex = rest.Count - 1;

        while (true)
        {
            if (queue.Count == 0)
            {
                if (restIndex < 0) return true;
                var item = rest[restIndex--];
                queue.Push((item.Flat, item.Doc));
                continue;
            }

            var (flat, doc) = queue.Pop();
            switch (doc)
            {
                case Doc.TextDoc text:
                    remaining -= text.Content.Length;
                    if (remaining < 0) return false;
                    break;

                case Doc.ConcatDoc concat:
                    for (var index = concat.Parts.Count - 1; index >= 0; index--)
                        queue.Push((flat, concat.Parts[index]));
                    break;

                case Doc.IndentDoc nested:
                    queue.Push((flat, nested.Content));
                    break;

                case Doc.GroupDoc group:
                    queue.Push((flat && !group.ForcesBreak, group.Content));
                    break;

                case Doc.FlatDoc forced:
                    queue.Push((true, forced.Content));
                    break;

                case Doc.LineSuffixDoc:
                    break;

                // Measured flat, a choice is the one-line arrangement it offers first.
                case Doc.ChoiceDoc choice:
                    queue.Push((flat, flat ? choice.Alternatives[0] : choice.Alternatives[^1]));
                    break;

                case Doc.LineDoc line:
                    if (!flat) return true;   // the enclosing document breaks here anyway
                    remaining -= line.Inline.Length;
                    if (remaining < 0) return false;
                    break;

                case Doc.HardLineDoc:
                    return !flat;             // a forced break inside means the group cannot be flat
            }
        }
    }

    private static int ColumnAfter(int column, string text)
    {
        var lastBreak = text.LastIndexOf('\n');
        return lastBreak < 0 ? column + text.Length : text.Length - lastBreak - 1;
    }

    private static string TrimTrailingSpaces(string text)
    {
        var lines = text.Split('\n');
        for (var index = 0; index < lines.Length; index++) lines[index] = lines[index].TrimEnd();
        return string.Join('\n', lines).Trim('\n');
    }
}
