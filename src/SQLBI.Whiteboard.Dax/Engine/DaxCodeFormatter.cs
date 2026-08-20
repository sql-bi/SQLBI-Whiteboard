using System.Text;
namespace SQLBI.Whiteboard.Dax.Engine;

/// <summary>
/// Formats DAX without a semantic model and without any parsing library. The source is tokenized,
/// parsed into a small syntax tree, and printed through a layout engine that keeps a construct on
/// one line when it fits and expands it fully when it does not.
/// </summary>
internal static class DaxCodeFormatter
{
    public const int DefaultMaximumLineLength = 65;

    public static string Format(string source, int maximumLineLength = DefaultMaximumLineLength)
    {
        TryFormat(source, maximumLineLength, out var formatted);
        return formatted;
    }

    /// <summary>
    /// Formats <paramref name="source"/> and reports whether the result is the formatted code.
    /// Formatting must never change the code, so when the tokens of the result do not match the
    /// tokens of the input the original text is returned unchanged and false is returned. Callers
    /// that only want the text can use <see cref="Format"/>.
    /// </summary>
    internal static bool TryFormat(string source, int maximumLineLength, out string formatted)
    {
        source = PrepareSource(source, maximumLineLength);

        var script = DaxParser.Parse(source);
        var printed = DaxPrinter.Print(script, maximumLineLength);

        if (!PreservesTokens(source, printed))
        {
            formatted = NormalizeLineEndings(source);
            return false;
        }

        formatted = NormalizeLineEndings(printed);
        return true;
    }

    /// <summary>
    /// Validates the line limit and returns the DAX the clipboard text actually contains, so that
    /// every formatter is given the same code.
    /// </summary>
    internal static string PrepareSource(string source, int maximumLineLength)
    {
        if (maximumLineLength is < 20 or > 500)
            throw new ArgumentOutOfRangeException(nameof(maximumLineLength), "The DAX line limit must be between 20 and 500 characters.");

        var dax = ExtractDaxCode(source).Trim();
        if (dax.Length == 0)
            throw new ArgumentException("The clipboard does not contain DAX code.", nameof(source));

        return dax;
    }

    /// <summary>
    /// Compares the token streams of the input and the output, comments included. Only whitespace,
    /// and the deliberate upper-casing of keywords and known function names, may differ.
    /// </summary>
    private static bool PreservesTokens(string source, string formatted) =>
        Signature(source).SequenceEqual(Signature(formatted), StringComparer.Ordinal) &&
        CommentSignature(source).SequenceEqual(CommentSignature(formatted), StringComparer.Ordinal);

    /// <summary>The comparable token sequence of some DAX. Exposed so tests can report a difference.</summary>
    internal static List<string> Signature(string code)
    {
        var signature = new List<string>();
        foreach (var token in DaxLexer.Tokenize(code))
        {
            if (token.Kind == DaxTokenKind.EndOfFile || token.Text.Length == 0) continue;

            // Identifiers and keywords are compared without case, because normalizing their case is
            // the one change the formatter is allowed to make. Literals are compared exactly.
            var text = token.Kind == DaxTokenKind.Identifier ? token.Text.ToUpperInvariant() : token.Text;
            signature.Add(token.Kind + "|" + text);
        }
        return signature;
    }

    /// <summary>
    /// The comments of some DAX, in the order they appear. They are compared separately from the
    /// code because a comment written after code moves to the end of whatever line it ends up on,
    /// so its position among the tokens can shift even though no comment is lost or altered.
    /// </summary>
    internal static List<string> CommentSignature(string code)
    {
        var comments = new List<string>();
        foreach (var token in DaxLexer.Tokenize(code))
        {
            foreach (var comment in token.LeadingComments) comments.Add(comment.Text.Trim());
            foreach (var comment in token.TrailingComments) comments.Add(comment.Text.Trim());
        }
        return comments;
    }

    private static string NormalizeLineEndings(string text) =>
        string.Join(Environment.NewLine, text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'));

    /// <summary>Takes the contents of a fenced dax block when the text came from a chat answer.</summary>
    private static string ExtractDaxCode(string source)
    {
        var normalized = source.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        var daxFence = normalized.IndexOf("```dax", StringComparison.OrdinalIgnoreCase);
        if (daxFence >= 0)
            normalized = normalized[daxFence..];
        else if (!normalized.StartsWith("```", StringComparison.Ordinal))
            return RemoveHeadingMarker(normalized);

        var firstLineEnd = normalized.IndexOf('\n');
        if (firstLineEnd < 0)
            return string.Empty;

        normalized = normalized[(firstLineEnd + 1)..];
        var closingFence = normalized.LastIndexOf("```", StringComparison.Ordinal);
        return RemoveHeadingMarker(closingFence >= 0 ? normalized[..closingFence] : normalized);
    }

    /// <summary>
    /// Drops a leading markdown heading marker, as in "# Sales Amount :=". DAX has no use for "#",
    /// so text copied out of a document keeps its measure on the first line instead of leaving the
    /// marker stranded as a statement of its own.
    /// </summary>
    private static string RemoveHeadingMarker(string code)
    {
        var start = 0;
        while (start < code.Length && (code[start] == ' ' || code[start] == '\t' || code[start] == '\n')) start++;

        var marker = start;
        while (marker < code.Length && code[marker] == '#') marker++;

        var hashes = marker - start;
        if (hashes is < 1 or > 6) return code;
        if (marker >= code.Length || code[marker] is not (' ' or '\t')) return code;

        return code[..start] + code[(marker + 1)..];
    }
}
