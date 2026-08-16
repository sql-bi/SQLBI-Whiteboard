using System.Windows;
using System.Windows.Media;
using SQLBI.Whiteboard.Core.Model;
using SQLBI.Whiteboard.Dax;
using SQLBI.Whiteboard.SqlServer;

namespace SQLBI.Whiteboard;

internal readonly record struct TextRunStyle(
    Brush Foreground,
    FontWeight FontWeight,
    FontStyle FontStyle);

internal readonly record struct StyledTextSpan(
    int Start,
    int Length,
    TextRunStyle Style);

internal sealed record TextLanguageAnalysis(
    string Title,
    IReadOnlyList<StyledTextSpan> Spans);

internal interface ITextLanguageService
{
    string Id { get; }
    string DisplayName { get; }
    string FontFamilyName { get; }
    bool CanFormat { get; }
    bool ShowLineNumbers { get; }
    bool WordWrap { get; }
    bool UseBackgroundAnalysis { get; }

    TextLanguageAnalysis Analyze(string source, string fallbackTitle);
    bool TryFormat(string source, out string formatted);
}

internal static class TextLanguageRegistry
{
    private static readonly ITextLanguageService Plain = new PlainTextLanguageService();
    private static readonly ITextLanguageService Dax = new DaxTextLanguageService();
    private static readonly ITextLanguageService SqlServer = new SqlServerTextLanguageService();

    public static IReadOnlyList<ITextLanguageService> All { get; } = [Plain, Dax, SqlServer];

    public static ITextLanguageService Resolve(string? languageId)
    {
        string normalized = TextLanguageIds.Normalize(languageId);
        return All.FirstOrDefault(language =>
                   language.Id.Equals(normalized, StringComparison.OrdinalIgnoreCase))
               ?? Plain;
    }

    private sealed class PlainTextLanguageService : ITextLanguageService
    {
        public string Id => TextLanguageIds.Plain;
        public string DisplayName => "Plain text";
        public string FontFamilyName => "Segoe UI";
        public bool CanFormat => false;
        public bool ShowLineNumbers => false;
        public bool WordWrap => true;
        public bool UseBackgroundAnalysis => false;

        public TextLanguageAnalysis Analyze(string source, string fallbackTitle) => new(
            string.IsNullOrWhiteSpace(fallbackTitle) ? "Text" : fallbackTitle,
            []);

        public bool TryFormat(string source, out string formatted)
        {
            formatted = source;
            return false;
        }

        public override string ToString() => DisplayName;
    }

    private sealed class DaxTextLanguageService : ITextLanguageService
    {
        private static readonly Brush DefaultText = CreateBrush(0xFF333333);
        private static readonly Brush Keyword = CreateBrush(0xFF035ACA);
        private static readonly Brush StringLiteral = CreateBrush(0xFFD93124);
        private static readonly Brush Number = CreateBrush(0xFFEE7F18);
        private static readonly Brush Parenthesis = CreateBrush(0xFF808080);
        private static readonly Brush Comment = CreateBrush(0xFF39A03B);
        private static readonly Brush Variable = CreateBrush(0xFF168C8B);
        private static readonly Brush QueryParameter = CreateBrush(0xFFDC419D);
        private static readonly Brush DefinitionName = CreateBrush(0xFF202020);
        private static readonly Brush Operator = CreateBrush(0xFF5E6470);

        public string Id => TextLanguageIds.Dax;
        public string DisplayName => "DAX";
        public string FontFamilyName => "Consolas";
        public bool CanFormat => true;
        public bool ShowLineNumbers => false;
        public bool WordWrap => true;
        public bool UseBackgroundAnalysis => false;

        public TextLanguageAnalysis Analyze(string source, string fallbackTitle)
        {
            string? objectName = DaxLanguageEngine.DefinedObjectName(source);
            string title = string.IsNullOrWhiteSpace(objectName)
                ? "DAX Code"
                : $"DAX Code of {objectName}";
            StyledTextSpan[] spans = DaxLanguageEngine.Classify(source)
                .Select(span => new StyledTextSpan(
                    span.Start,
                    span.Length,
                    StyleOf(span.Classification)))
                .ToArray();
            return new TextLanguageAnalysis(title, spans);
        }

        public bool TryFormat(string source, out string formatted) =>
            DaxLanguageEngine.TryFormat(
                source,
                DaxLanguageEngine.DefaultMaximumLineLength,
                out formatted);

        public override string ToString() => DisplayName;

        private static TextRunStyle StyleOf(DaxTextClassification classification) =>
            classification switch
            {
                DaxTextClassification.Keyword or DaxTextClassification.Function =>
                    new TextRunStyle(Keyword, FontWeights.Bold, FontStyles.Normal),
                DaxTextClassification.StringLiteral =>
                    new TextRunStyle(StringLiteral, FontWeights.Normal, FontStyles.Normal),
                DaxTextClassification.Number =>
                    new TextRunStyle(Number, FontWeights.Normal, FontStyles.Normal),
                DaxTextClassification.Comment =>
                    new TextRunStyle(Comment, FontWeights.Normal, FontStyles.Italic),
                DaxTextClassification.Variable =>
                    new TextRunStyle(Variable, FontWeights.SemiBold, FontStyles.Normal),
                DaxTextClassification.QueryParameter =>
                    new TextRunStyle(QueryParameter, FontWeights.SemiBold, FontStyles.Normal),
                DaxTextClassification.Parenthesis or DaxTextClassification.Punctuation =>
                    new TextRunStyle(Parenthesis, FontWeights.Normal, FontStyles.Normal),
                DaxTextClassification.DefinitionName =>
                    new TextRunStyle(DefinitionName, FontWeights.Bold, FontStyles.Normal),
                DaxTextClassification.Operator =>
                    new TextRunStyle(Operator, FontWeights.SemiBold, FontStyles.Normal),
                _ => new TextRunStyle(DefaultText, FontWeights.Normal, FontStyles.Normal),
            };
    }

    private sealed class SqlServerTextLanguageService : ITextLanguageService
    {
        private readonly object _cacheLock = new();
        private string? _cachedSource;
        private TextLanguageAnalysis? _cachedAnalysis;
        private static readonly Brush DefaultText = CreateBrush(0xFF333333);
        private static readonly Brush Keyword = CreateBrush(0xFF035ACA);
        private static readonly Brush Function = CreateBrush(0xFF795E26);
        private static readonly Brush StringLiteral = CreateBrush(0xFFA31515);
        private static readonly Brush Number = CreateBrush(0xFFEE7F18);
        private static readonly Brush Comment = CreateBrush(0xFF268E26);
        private static readonly Brush Variable = CreateBrush(0xFF168C8B);
        private static readonly Brush DataType = CreateBrush(0xFF267F99);
        private static readonly Brush TableName = CreateBrush(0xFF005A70);
        private static readonly Brush Alias = CreateBrush(0xFF6F42C1);
        private static readonly Brush Parenthesis = CreateBrush(0xFF808080);
        private static readonly Brush DefinitionName = CreateBrush(0xFF202020);
        private static readonly Brush Operator = CreateBrush(0xFF5E6470);

        public string Id => TextLanguageIds.SqlServer;
        public string DisplayName => "SQL Server";
        public string FontFamilyName => "Consolas";
        public bool CanFormat => true;
        public bool ShowLineNumbers => false;
        public bool WordWrap => true;
        public bool UseBackgroundAnalysis => true;

        public TextLanguageAnalysis Analyze(string source, string fallbackTitle)
        {
            lock (_cacheLock)
            {
                if (_cachedAnalysis is not null &&
                    string.Equals(_cachedSource, source, StringComparison.Ordinal))
                {
                    return _cachedAnalysis;
                }
            }

            SqlServerTextAnalysis analysis = SqlServerLanguageEngine.Analyze(source);
            string title = string.IsNullOrWhiteSpace(analysis.DefinedObjectName)
                ? "SQL Code"
                : $"SQL Code of {analysis.DefinedObjectName}";
            StyledTextSpan[] spans = analysis.Spans
                .Select(span => new StyledTextSpan(
                    span.Start,
                    span.Length,
                    StyleOf(span.Classification)))
                .ToArray();
            var result = new TextLanguageAnalysis(title, spans);
            lock (_cacheLock)
            {
                _cachedSource = source;
                _cachedAnalysis = result;
            }

            return result;
        }

        public bool TryFormat(string source, out string formatted) =>
            SqlServerLanguageEngine.TryFormat(source, out formatted);

        public override string ToString() => DisplayName;

        private static TextRunStyle StyleOf(SqlServerTextClassification classification) =>
            classification switch
            {
                SqlServerTextClassification.Keyword =>
                    new TextRunStyle(Keyword, FontWeights.Bold, FontStyles.Normal),
                SqlServerTextClassification.Function =>
                    new TextRunStyle(Function, FontWeights.SemiBold, FontStyles.Normal),
                SqlServerTextClassification.StringLiteral =>
                    new TextRunStyle(StringLiteral, FontWeights.Normal, FontStyles.Normal),
                SqlServerTextClassification.Number =>
                    new TextRunStyle(Number, FontWeights.Normal, FontStyles.Normal),
                SqlServerTextClassification.Comment =>
                    new TextRunStyle(Comment, FontWeights.Normal, FontStyles.Italic),
                SqlServerTextClassification.Variable or SqlServerTextClassification.Parameter =>
                    new TextRunStyle(Variable, FontWeights.SemiBold, FontStyles.Normal),
                SqlServerTextClassification.DataType =>
                    new TextRunStyle(DataType, FontWeights.SemiBold, FontStyles.Normal),
                SqlServerTextClassification.TableName =>
                    new TextRunStyle(TableName, FontWeights.Normal, FontStyles.Normal),
                SqlServerTextClassification.Alias =>
                    new TextRunStyle(Alias, FontWeights.Normal, FontStyles.Normal),
                SqlServerTextClassification.Parenthesis or
                    SqlServerTextClassification.Punctuation =>
                    new TextRunStyle(Parenthesis, FontWeights.Normal, FontStyles.Normal),
                SqlServerTextClassification.DefinitionName =>
                    new TextRunStyle(DefinitionName, FontWeights.Bold, FontStyles.Normal),
                SqlServerTextClassification.Operator =>
                    new TextRunStyle(Operator, FontWeights.SemiBold, FontStyles.Normal),
                _ => new TextRunStyle(DefaultText, FontWeights.Normal, FontStyles.Normal),
            };
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
