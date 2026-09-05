using Kusto.Language;
using Kusto.Language.Editor;
using Kusto.Language.Parsing;
using Kusto.Language.Syntax;

namespace SQLBI.Whiteboard.Kql;

public enum KqlTextClassification
{
    Text,
    Keyword,
    QueryOperator,
    Command,
    Function,
    StringLiteral,
    Number,
    Comment,
    Identifier,
    Variable,
    Parameter,
    TableName,
    ColumnName,
    DataType,
    QueryParameter,
    Punctuation,
    Operator,
    DefinitionName,
}

public readonly record struct KqlClassifiedSpan(
    int Start,
    int Length,
    KqlTextClassification Classification);

public readonly record struct KqlParseDiagnostic(
    int Offset,
    int Line,
    int Column,
    string Message);

public sealed record KqlTextAnalysis(
    IReadOnlyList<KqlClassifiedSpan> Spans,
    string? DefinedObjectName,
    IReadOnlyList<KqlParseDiagnostic> Diagnostics);

/// <summary>
/// Highlighting and formatting for Kusto Query Language, over Microsoft's own parser and
/// formatter. Only the syntax is judged: a board carries a snippet rather than a connection,
/// so the tables and columns it names cannot be resolved and the semantic diagnostics that
/// reports would all be false alarms.
/// </summary>
public static class KqlLanguageEngine
{
    /// <summary>
    /// The author's spacing is kept around the assignment sign, so that a join written as
    /// kind=inner and the hints beside it survive formatting the way they were typed. Every
    /// other rule is the library default.
    /// </summary>
    private static readonly FormattingOptions Options =
        FormattingOptions.Default.WithAssignmentSpacing(DualSpacingStyle.AsIs);

    public static KqlTextAnalysis Analyze(string source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return new KqlTextAnalysis([], null, []);
        }

        try
        {
            KustoCode code = KustoCode.Parse(source);
            NameNode? definition = DefinitionNode(code);
            var spans = new List<KqlClassifiedSpan>();
            foreach (ClassifiedRange range in new KustoCodeService(source)
                         .GetClassifications(0, source.Length)
                         .Classifications)
            {
                int length = Math.Min(range.Length, source.Length - range.Start);
                if (range.Start < 0 || length <= 0)
                {
                    continue;
                }

                KqlTextClassification classification =
                    definition is not null && range.Start == definition.Start
                        ? KqlTextClassification.DefinitionName
                        : Map(range.Kind);
                spans.Add(new KqlClassifiedSpan(range.Start, length, classification));
            }

            return new KqlTextAnalysis(spans, definition?.Name, Diagnostics(code, source));
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            return new KqlTextAnalysis(
                [],
                null,
                [new KqlParseDiagnostic(0, 1, 1, exception.Message)]);
        }
    }

    public static IReadOnlyList<KqlClassifiedSpan> Classify(string source) =>
        Analyze(source).Spans;

    public static string? DefinedObjectName(string source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return null;
        }

        try
        {
            return DefinitionNode(KustoCode.Parse(source))?.Name;
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            return null;
        }
    }

    /// <summary>
    /// Formats <paramref name="source"/> and reports whether the result is the formatted code.
    /// Formatting must never change the code, so the tokens and the comments of the result are
    /// compared with those of the input and the original text is returned when they differ.
    /// </summary>
    public static bool TryFormat(string source, out string formatted)
    {
        formatted = source;
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        try
        {
            // The formatter returns a mixture of line endings whatever it is given, so the
            // text handed to it is settled first and the result normalized afterwards. That
            // keeps the comparison below between like and like however the caller stored it.
            string prepared = ToLineFeeds(source);
            if (KustoCode.Parse(prepared).GetSyntaxDiagnostics().Count > 0)
            {
                return false;
            }

            string generated = NormalizeLineEndings(
                new KustoCodeService(prepared).GetFormattedText(Options).Text).TrimEnd();
            if (generated.Length == 0 || !PreservesTokens(prepared, generated))
            {
                return false;
            }

            formatted = generated;
            return true;
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            return false;
        }
    }

    /// <summary>
    /// The name of the object a management command defines, and where it sits. The name is the
    /// first one the command itself carries: the properties of a with-clause are nested deeper,
    /// so they are passed over rather than mistaken for the name.
    /// </summary>
    private static NameNode? DefinitionNode(KustoCode code)
    {
        if (code.Syntax.GetFirstDescendant<CustomCommand>() is null)
        {
            return null;
        }

        foreach (SyntaxNode node in code.Syntax.GetDescendants<SyntaxNode>())
        {
            string? name = node switch
            {
                NameDeclaration declaration => declaration.SimpleName,
                NameReference reference => reference.SimpleName,
                _ => null,
            };

            if (name is { Length: > 0 } &&
                node.Parent is CustomNode { Parent: CustomCommand })
            {
                return new NameNode(name, node.TextStart);
            }
        }

        return null;
    }

    private static IReadOnlyList<KqlParseDiagnostic> Diagnostics(KustoCode code, string source)
    {
        IReadOnlyList<Diagnostic> diagnostics = code.GetSyntaxDiagnostics();
        if (diagnostics.Count == 0)
        {
            return [];
        }

        var result = new List<KqlParseDiagnostic>(diagnostics.Count);
        foreach (Diagnostic diagnostic in diagnostics)
        {
            int offset = Math.Clamp(diagnostic.Start, 0, source.Length);
            (int line, int column) = LineAndColumn(source, offset);
            result.Add(new KqlParseDiagnostic(offset, line, column, diagnostic.Message));
        }

        return result;
    }

    private static (int Line, int Column) LineAndColumn(string source, int offset)
    {
        int line = 1;
        int lineStart = 0;
        for (int index = 0; index < offset; index++)
        {
            if (source[index] != '\n')
            {
                continue;
            }

            line++;
            lineStart = index + 1;
        }

        return (line, offset - lineStart + 1);
    }

    private static bool PreservesTokens(string before, string after) =>
        TokenSignature(before).SequenceEqual(TokenSignature(after), StringComparer.Ordinal) &&
        CommentSignature(before).SequenceEqual(CommentSignature(after), StringComparer.Ordinal);

    private static IReadOnlyList<string> TokenSignature(string source) =>
        TokenParser.ParseTokens(source)
            .Where(token => token.Kind != SyntaxKind.EndOfTextToken && token.Text.Length > 0)
            .Select(token => token.Kind + "|" + token.Text)
            .ToArray();

    /// <summary>
    /// The comments of some KQL, in the order they appear. They are compared separately from
    /// the code because a comment lives in the trivia ahead of a token rather than among the
    /// tokens, and formatting can move it to the end of the line the code ends up on.
    /// </summary>
    private static IReadOnlyList<string> CommentSignature(string source)
    {
        var comments = new List<string>();
        foreach (LexicalToken token in TokenParser.ParseTokens(source))
        {
            string trivia = token.Trivia;
            int index = trivia.IndexOf("//", StringComparison.Ordinal);
            while (index >= 0)
            {
                int end = trivia.IndexOfAny(NewLineCharacters, index);
                int stop = end < 0 ? trivia.Length : end;
                comments.Add(trivia[index..stop].TrimEnd());
                if (end < 0)
                {
                    break;
                }

                index = trivia.IndexOf("//", stop, StringComparison.Ordinal);
            }
        }

        return comments;
    }

    private static readonly char[] NewLineCharacters = ['\r', '\n'];

    private static string ToLineFeeds(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n');

    private static string NormalizeLineEndings(string text) =>
        string.Join(Environment.NewLine, ToLineFeeds(text).Split('\n'));

    private static bool IsRecoverable(Exception exception) =>
        exception is ArgumentException or FormatException or InvalidOperationException or
            IndexOutOfRangeException or NullReferenceException;

    private static KqlTextClassification Map(ClassificationKind kind) =>
        kind switch
        {
            ClassificationKind.Comment => KqlTextClassification.Comment,
            ClassificationKind.Punctuation => KqlTextClassification.Punctuation,
            ClassificationKind.Literal => KqlTextClassification.Number,
            ClassificationKind.StringLiteral => KqlTextClassification.StringLiteral,
            ClassificationKind.Type => KqlTextClassification.DataType,
            ClassificationKind.Column or ClassificationKind.SchemaMember =>
                KqlTextClassification.ColumnName,
            ClassificationKind.Table or ClassificationKind.Database or
                ClassificationKind.MaterializedView => KqlTextClassification.TableName,
            ClassificationKind.Function => KqlTextClassification.Function,
            ClassificationKind.Parameter or ClassificationKind.SignatureParameter =>
                KqlTextClassification.Parameter,
            ClassificationKind.Variable => KqlTextClassification.Variable,
            ClassificationKind.Identifier => KqlTextClassification.Identifier,
            ClassificationKind.QueryParameter or ClassificationKind.ClientParameter or
                ClassificationKind.Option => KqlTextClassification.QueryParameter,
            ClassificationKind.ScalarOperator or ClassificationKind.MathOperator =>
                KqlTextClassification.Operator,
            ClassificationKind.QueryOperator => KqlTextClassification.QueryOperator,
            ClassificationKind.Command or ClassificationKind.Directive =>
                KqlTextClassification.Command,
            ClassificationKind.Keyword => KqlTextClassification.Keyword,
            _ => KqlTextClassification.Text,
        };

    private sealed record NameNode(string Name, int Start);
}
