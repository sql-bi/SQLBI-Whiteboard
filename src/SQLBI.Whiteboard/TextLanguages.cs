using System.Windows;
using System.Windows.Media;
using SQLBI.Whiteboard.Core.Model;
using SQLBI.Whiteboard.Dax;
using SQLBI.Whiteboard.Kql;
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
    /// <summary>
    /// Whether the language may claim a paste. Separate from <see cref="CanFormat"/>:
    /// a language can be chosen by hand long before it recognizes anything.
    /// </summary>
    bool CanDetect { get; }
    bool ShowLineNumbers { get; }
    bool WordWrap { get; }
    bool UseBackgroundAnalysis { get; }
    /// <summary>
    /// Where F6 sends someone whose language cannot format yet. Null for the
    /// languages that format, and for plain text, which has nothing to ask for.
    /// </summary>
    Uri? FormattingRequestUri { get; }

    TextLanguageAnalysis Analyze(string source, string fallbackTitle);
    /// <summary>
    /// Formats to at most <paramref name="columns"/> characters a line where the
    /// language wraps by width; the others break by structure and ignore it.
    /// </summary>
    bool TryFormat(string source, int columns, out string formatted);
    bool TryAccept(string source);
}

internal static class TextLanguageRegistry
{
    private static readonly ITextLanguageService Plain = new PlainTextLanguageService();
    private static readonly ITextLanguageService Dax = new DaxTextLanguageService();
    private static readonly ITextLanguageService SqlServer = new SqlServerTextLanguageService();
    private static readonly ITextLanguageService Kql = new KqlTextLanguageService();

    /// <summary>
    /// Every language the selectors offer. The four that recognize a snippet
    /// come first, in the order they always had; the rest are chosen by hand,
    /// in the order the language inventory lists them.
    /// </summary>
    public static IReadOnlyList<ITextLanguageService> All { get; } =
    [
        Plain,
        Dax,
        SqlServer,
        Kql,
        new ManualTextLanguageService(TextLanguageIds.Python, "Python"),
        new ManualTextLanguageService(TextLanguageIds.C, "C"),
        new ManualTextLanguageService(TextLanguageIds.Cpp, "C++"),
        new ManualTextLanguageService(TextLanguageIds.Java, "Java"),
        new ManualTextLanguageService(TextLanguageIds.CSharp, "C#"),
        new ManualTextLanguageService(TextLanguageIds.JavaScript, "JavaScript"),
        new ManualTextLanguageService(TextLanguageIds.TypeScript, "TypeScript"),
        new ManualTextLanguageService(TextLanguageIds.VbNet, "Visual Basic .NET"),
        new ManualTextLanguageService(TextLanguageIds.R, "R"),
        new ManualTextLanguageService(TextLanguageIds.Rust, "Rust"),
        new ManualTextLanguageService(TextLanguageIds.Php, "PHP"),
    ];

    public static ITextLanguageService Resolve(string? languageId)
    {
        string normalized = TextLanguageIds.Normalize(languageId);
        return All.FirstOrDefault(language =>
                   language.Id.Equals(normalized, StringComparison.OrdinalIgnoreCase))
               ?? Plain;
    }

    public static string ResolveFromOrder(string source, IEnumerable<string>? languageIds)
    {
        foreach (string languageId in TextLanguageIds.NormalizeOrder(languageIds))
        {
            ITextLanguageService language = Resolve(languageId);
            if (language.CanDetect && language.TryAccept(source))
            {
                return language.Id;
            }
        }

        return TextLanguageIds.Plain;
    }

    private sealed class PlainTextLanguageService : ITextLanguageService
    {
        public string Id => TextLanguageIds.Plain;
        public string DisplayName => "Plain text";
        public string FontFamilyName => "Segoe UI";
        public bool CanFormat => false;
        public bool CanDetect => true;
        public bool ShowLineNumbers => false;
        public bool WordWrap => true;
        public bool UseBackgroundAnalysis => false;
        public Uri? FormattingRequestUri => null;

        public TextLanguageAnalysis Analyze(string source, string fallbackTitle) => new(
            string.IsNullOrWhiteSpace(fallbackTitle) ? "Text" : fallbackTitle,
            []);

        public bool TryFormat(string source, int columns, out string formatted)
        {
            formatted = source;
            return false;
        }

        public bool TryAccept(string source) => true;

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
        public bool CanDetect => true;
        public bool ShowLineNumbers => false;
        public bool WordWrap => true;
        public bool UseBackgroundAnalysis => false;
        public Uri? FormattingRequestUri => null;

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

        public bool TryFormat(string source, int columns, out string formatted) =>
            DaxLanguageEngine.TryFormat(
                source,
                Math.Clamp(columns, TextContainerVisual.MinimumColumns, TextContainerVisual.MaximumColumns),
                out formatted);

        // The parser alone is too welcoming: a bare name or a number parses in
        // more than one language. The engine says whether there is a real signal.
        public bool TryAccept(string source)
        {
            try
            {
                return DaxLanguageEngine.LooksLike(source) && TryFormat(source, DaxLanguageEngine.DefaultMaximumLineLength, out _);
            }
            catch (Exception)
            {
                return false;
            }
        }

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
        public bool CanDetect => true;
        public bool ShowLineNumbers => false;
        public bool WordWrap => true;
        public bool UseBackgroundAnalysis => true;
        public Uri? FormattingRequestUri => null;

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

        public bool TryFormat(string source, int columns, out string formatted) =>
            SqlServerLanguageEngine.TryFormat(source, out formatted);

        // The parser alone is too welcoming: a bare name or a number parses in
        // more than one language. The engine says whether there is a real signal.
        public bool TryAccept(string source)
        {
            try
            {
                return SqlServerLanguageEngine.LooksLike(source) && TryFormat(source, DaxLanguageEngine.DefaultMaximumLineLength, out _);
            }
            catch (Exception)
            {
                return false;
            }
        }

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

    private sealed class KqlTextLanguageService : ITextLanguageService
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
        private static readonly Brush QueryParameter = CreateBrush(0xFF6F42C1);
        private static readonly Brush Parenthesis = CreateBrush(0xFF808080);
        private static readonly Brush DefinitionName = CreateBrush(0xFF202020);
        private static readonly Brush Operator = CreateBrush(0xFF5E6470);

        public string Id => TextLanguageIds.Kql;
        public string DisplayName => "KQL";
        public string FontFamilyName => "Consolas";
        public bool CanFormat => true;
        public bool CanDetect => true;
        public bool ShowLineNumbers => false;
        public bool WordWrap => true;
        public bool UseBackgroundAnalysis => true;
        public Uri? FormattingRequestUri => null;

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

            KqlTextAnalysis analysis = KqlLanguageEngine.Analyze(source);
            string title = string.IsNullOrWhiteSpace(analysis.DefinedObjectName)
                ? "KQL Code"
                : $"KQL Code of {analysis.DefinedObjectName}";
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

        public bool TryFormat(string source, int columns, out string formatted) =>
            KqlLanguageEngine.TryFormat(source, out formatted);

        // The parser alone is too welcoming: a bare name or a number parses in
        // more than one language. The engine says whether there is a real signal.
        public bool TryAccept(string source)
        {
            try
            {
                return KqlLanguageEngine.LooksLike(source) && TryFormat(source, DaxLanguageEngine.DefaultMaximumLineLength, out _);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public override string ToString() => DisplayName;

        private static TextRunStyle StyleOf(KqlTextClassification classification) =>
            classification switch
            {
                KqlTextClassification.Keyword or KqlTextClassification.QueryOperator or
                    KqlTextClassification.Command =>
                    new TextRunStyle(Keyword, FontWeights.Bold, FontStyles.Normal),
                KqlTextClassification.Function =>
                    new TextRunStyle(Function, FontWeights.SemiBold, FontStyles.Normal),
                KqlTextClassification.StringLiteral =>
                    new TextRunStyle(StringLiteral, FontWeights.Normal, FontStyles.Normal),
                KqlTextClassification.Number =>
                    new TextRunStyle(Number, FontWeights.Normal, FontStyles.Normal),
                KqlTextClassification.Comment =>
                    new TextRunStyle(Comment, FontWeights.Normal, FontStyles.Italic),
                KqlTextClassification.Variable or KqlTextClassification.Parameter =>
                    new TextRunStyle(Variable, FontWeights.SemiBold, FontStyles.Normal),
                KqlTextClassification.DataType =>
                    new TextRunStyle(DataType, FontWeights.SemiBold, FontStyles.Normal),
                KqlTextClassification.TableName =>
                    new TextRunStyle(TableName, FontWeights.Normal, FontStyles.Normal),
                KqlTextClassification.QueryParameter =>
                    new TextRunStyle(QueryParameter, FontWeights.SemiBold, FontStyles.Normal),
                KqlTextClassification.Punctuation =>
                    new TextRunStyle(Parenthesis, FontWeights.Normal, FontStyles.Normal),
                KqlTextClassification.DefinitionName =>
                    new TextRunStyle(DefinitionName, FontWeights.Bold, FontStyles.Normal),
                KqlTextClassification.Operator =>
                    new TextRunStyle(Operator, FontWeights.SemiBold, FontStyles.Normal),
                _ => new TextRunStyle(DefaultText, FontWeights.Normal, FontStyles.Normal),
            };
    }

    /// <summary>
    /// A language a text container can be set to, which Whiteboard does not
    /// color or format yet. It keeps the source exactly as written, shows it in
    /// the code font, and sends F6 to the issue collecting votes for it.
    /// </summary>
    private sealed class ManualTextLanguageService(string id, string displayName) : ITextLanguageService
    {
        private readonly Uri? _formattingRequestUri =
            TextLanguageIds.FormattingRequestUrl(id) is { } url ? new Uri(url) : null;

        public string Id => id;
        public string DisplayName => displayName;
        public string FontFamilyName => "Consolas";
        public bool CanFormat => false;
        public bool CanDetect => false;
        public bool ShowLineNumbers => false;
        public bool WordWrap => true;
        public bool UseBackgroundAnalysis => false;
        public Uri? FormattingRequestUri => _formattingRequestUri;

        public TextLanguageAnalysis Analyze(string source, string fallbackTitle) =>
            new($"{displayName} Code", []);

        public bool TryFormat(string source, int columns, out string formatted)
        {
            formatted = source;
            return false;
        }

        public bool TryAccept(string source) => false;

        public override string ToString() => DisplayName;
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
