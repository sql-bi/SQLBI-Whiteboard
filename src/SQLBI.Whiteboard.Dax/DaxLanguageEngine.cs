using SQLBI.Whiteboard.Dax.Engine;

namespace SQLBI.Whiteboard.Dax;

public enum DaxTextClassification
{
    Text,
    Keyword,
    Function,
    StringLiteral,
    Number,
    Comment,
    TableName,
    ColumnReference,
    Variable,
    QueryParameter,
    Parenthesis,
    DefinitionName,
    Operator,
    Punctuation,
}

public readonly record struct DaxClassifiedSpan(
    int Start,
    int Length,
    DaxTextClassification Classification);

public static class DaxLanguageEngine
{
    public const int DefaultMaximumLineLength = DaxCodeFormatter.DefaultMaximumLineLength;

    public static string Format(
        string source,
        int maximumLineLength = DefaultMaximumLineLength) =>
        DaxCodeFormatter.Format(source, maximumLineLength);

    public static bool TryFormat(
        string source,
        int maximumLineLength,
        out string formatted)
    {
        try
        {
            return DaxCodeFormatter.TryFormat(source, maximumLineLength, out formatted);
        }
        catch (Exception exception) when (
            exception is ArgumentException or FormatException or InvalidOperationException)
        {
            formatted = source;
            return false;
        }
    }

    public static IReadOnlyList<DaxClassifiedSpan> Classify(string source) =>
        DaxClassifier.Classify(source)
            .Select(span => new DaxClassifiedSpan(
                span.Start,
                span.Length,
                Map(span.Kind)))
            .ToArray();

    public static string? DefinedObjectName(string source) =>
        DaxClassifier.DefinedObjectName(source);

    public static bool IsQuery(string source) => DaxClassifier.IsQuery(source);

    private static DaxTextClassification Map(DaxClassification classification) =>
        classification switch
        {
            DaxClassification.Keyword => DaxTextClassification.Keyword,
            DaxClassification.Function => DaxTextClassification.Function,
            DaxClassification.StringLiteral => DaxTextClassification.StringLiteral,
            DaxClassification.Number => DaxTextClassification.Number,
            DaxClassification.Comment => DaxTextClassification.Comment,
            DaxClassification.TableName => DaxTextClassification.TableName,
            DaxClassification.ColumnReference => DaxTextClassification.ColumnReference,
            DaxClassification.Variable => DaxTextClassification.Variable,
            DaxClassification.QueryParameter => DaxTextClassification.QueryParameter,
            DaxClassification.Parenthesis => DaxTextClassification.Parenthesis,
            DaxClassification.DefinitionName => DaxTextClassification.DefinitionName,
            DaxClassification.Operator => DaxTextClassification.Operator,
            DaxClassification.Punctuation => DaxTextClassification.Punctuation,
            _ => DaxTextClassification.Text,
        };
}
