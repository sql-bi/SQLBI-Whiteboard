using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using SQLBI.Whiteboard;
using SQLBI.Whiteboard.Core.Model;

// The languages Whiteboard colors from a syntax definition rather than a parser.
// What is checked is what a reader sees: which characters carry which color, that
// a construct that spans lines keeps its color to the end, that an unterminated
// one recovers where the language says it does, and that nothing here ever
// changes the source.

var registry = TextLanguageRegistry.All;
Assert(registry.Count == 15, "The selectors offer fifteen languages.");
Assert(
    registry.Count(language => language.CanDetect) == 4 &&
    registry.Where(language => language.CanDetect)
        .Select(language => language.Id)
        .SequenceEqual([TextLanguageIds.Plain, TextLanguageIds.Dax, TextLanguageIds.SqlServer, TextLanguageIds.Kql]),
    "Only the original four can claim a paste.");
Assert(
    registry.Where(language => !language.CanDetect)
        .All(language => !language.CanFormat &&
                         !language.TryAccept("anything at all") &&
                         language.FormattingRequestUri is { IsAbsoluteUri: true, Scheme: "https" }),
    "A language chosen by hand does not format, never claims a paste, and asks for a vote over HTTPS.");
Assert(
    registry.Where(language => language.CanDetect)
        .All(language => language.FormattingRequestUri is null),
    "The languages that format have nothing to vote for.");
Assert(
    TextLanguageRegistry.Resolve(TextLanguageIds.Rust).FormattingRequestUri?.AbsoluteUri ==
    "https://github.com/sql-bi/SQLBI-Whiteboard/issues/117",
    "A service hands back the issue its identifier is registered with.");

// F6 never reaches a formatter for these, and neither does anything else: the
// source that comes back is the source that went in.
foreach (ITextLanguageService language in registry.Where(language => !language.CanDetect))
{
    Assert(
        !language.TryFormat("some text\n\tand more", 65, out string formatted) &&
        formatted == "some text\n\tand more",
        $"{language.DisplayName} should refuse to format and hand the source back unchanged.");
    Assert(
        language.Analyze("x", "Imported title").Title == $"{language.DisplayName} Code",
        $"{language.DisplayName} should use its own generic title.");
}

const string CSharpSource = """"""
    #nullable enable
    [Obsolete("use Other", true)]
    public sealed record Point(int X, int Y)
    {
        // A comment
        private const string Json = """
            {"a": 1, "b": "x"}
            """;
        private static readonly string P = @"C:\temp\x"" still";
        public string Describe(int n) => $"total {n + 1:N0} and {{literal}}";
        public char Tab { get; } = '\t';
        public int Count = 1_000_000;
    }
    """""";

Colors(CSharpSource, TextLanguageIds.CSharp, "Comment", "// A comment");
Colors(CSharpSource, TextLanguageIds.CSharp, "Directive", "#nullable enable");
Colors(CSharpSource, TextLanguageIds.CSharp, "Directive", "[Obsolete(");
Colors(CSharpSource, TextLanguageIds.CSharp, "String", "\"use Other\"");
Colors(CSharpSource, TextLanguageIds.CSharp, "Keyword", "record");
Colors(CSharpSource, TextLanguageIds.CSharp, "Type", "string");
Colors(CSharpSource, TextLanguageIds.CSharp, "Function", "Describe");
Colors(CSharpSource, TextLanguageIds.CSharp, "Number", "1_000_000");
Colors(CSharpSource, TextLanguageIds.CSharp, "String", "'\\t'");
// A raw string runs over its lines, a verbatim string survives its doubled
// quote, and an interpolation hole is read as the code it is.
Colors(CSharpSource, TextLanguageIds.CSharp, "String", "        {\"a\": 1, \"b\": \"x\"}");
Colors(CSharpSource, TextLanguageIds.CSharp, "String", "@\"C:\\temp\\x\"\" still\"");
Colors(CSharpSource, TextLanguageIds.CSharp, "Variable", "{n ");
Colors(CSharpSource, TextLanguageIds.CSharp, "Number", "1");
Colors(CSharpSource, TextLanguageIds.CSharp, "String", " and {{literal}}\"");

const string CSource = """
    #include <stdio.h>
    #define LONG(a) \
        ((a) + 1)
    /* block
       comment */ static const char *s = "hi\n";
    int main(void) {
        char c = '\t';
        unsigned long v = 0xFF'00uL;
        return 0;
    }
    """;

Colors(CSource, TextLanguageIds.C, "Directive", "#include ");
Colors(CSource, TextLanguageIds.C, "String", "<stdio.h>");
// A directive continued by a backslash keeps its color onto the next line.
Colors(CSource, TextLanguageIds.C, "Directive", "#define LONG(a) \\");
Colors(CSource, TextLanguageIds.C, "Directive", "    ((a) + 1)");
Colors(CSource, TextLanguageIds.C, "Comment", "/* block");
Colors(CSource, TextLanguageIds.C, "Comment", "   comment */");
Colors(CSource, TextLanguageIds.C, "Type", "char");
Colors(CSource, TextLanguageIds.C, "Function", "main");
Colors(CSource, TextLanguageIds.C, "String", "'\\t'");
// The apostrophe in a digit separator belongs to the number, not to a
// character literal that would then run to the end of the line.
Colors(CSource, TextLanguageIds.C, "Number", "0xFF'00uL");

const string CppSource = """
    #include <vector>
    [[nodiscard]] constexpr auto make() noexcept -> std::vector<int> {
        auto s = R"(raw "quoted" text)";
        auto j = R"json({"a": 1})json";
        if (a < b && c > d) { return std::vector<int>{1'000, 0b1010}; }
    }
    """;

Colors(CppSource, TextLanguageIds.Cpp, "Directive", "[[nodiscard]]");
Colors(CppSource, TextLanguageIds.Cpp, "Keyword", "constexpr");
Colors(CppSource, TextLanguageIds.Cpp, "String", "R\"(raw \"quoted\" text)\"");
Colors(CppSource, TextLanguageIds.Cpp, "String", "R\"json({\"a\": 1})json\"");
Colors(CppSource, TextLanguageIds.Cpp, "Number", "1'000");
Colors(CppSource, TextLanguageIds.Cpp, "Number", "0b1010");
Colors(CppSource, TextLanguageIds.Cpp, "Function", "make");
// A comparison is not a template argument list, so neither name is a call.
Assert(
    !Spans(CppSource, TextLanguageIds.Cpp).Any(span => span.Category == "Function" && span.Text is "a" or "c"),
    "A plain comparison should not read as a call.");

const string JavaSource = """"""
    package demo;

    @FunctionalInterface
    public sealed interface Shape permits Circle {
        String BLOCK = """
            hello "world"
            """;
        default List<String> names(int n) {
            char c = '\n';
            var total = 1_000L;
            return List.of("a\tb", BLOCK); // done
        }
    }
    """""";

Colors(JavaSource, TextLanguageIds.Java, "Directive", "@FunctionalInterface");
Colors(JavaSource, TextLanguageIds.Java, "Keyword", "sealed");
Colors(JavaSource, TextLanguageIds.Java, "Keyword", "permits");
Colors(JavaSource, TextLanguageIds.Java, "String", "        hello \"world\"");
Colors(JavaSource, TextLanguageIds.Java, "Number", "1_000L");
Colors(JavaSource, TextLanguageIds.Java, "String", "'\\n'");
Colors(JavaSource, TextLanguageIds.Java, "Comment", "// done");

const string VbSource = """
    ' A comment
    <Serializable>
    Public Module M
        Public Sub Main() REM tail comment
            Dim s As String = "a""b"
            Dim d As Date = #1/1/2020#
            Dim h = &HFF_00UL
            Dim q = $"total {n + 1} done"
            Dim x = <root><item>text</item></root>
            If a < b AndAlso c > d Then _
                Console.WriteLine(s)
        End Sub
    End Module
    """;

Colors(VbSource, TextLanguageIds.VbNet, "Comment", "' A comment");
Colors(VbSource, TextLanguageIds.VbNet, "Comment", "REM tail comment");
Colors(VbSource, TextLanguageIds.VbNet, "Directive", "<Serializable>");
Colors(VbSource, TextLanguageIds.VbNet, "String", "\"a\"\"b\"");
Colors(VbSource, TextLanguageIds.VbNet, "String", "#1/1/2020#");
Colors(VbSource, TextLanguageIds.VbNet, "Number", "&HFF_00UL");
Colors(VbSource, TextLanguageIds.VbNet, "Variable", "{n ");
Colors(VbSource, TextLanguageIds.VbNet, "String", "<root><item>text</item></root>");
// Keywords are matched whatever their case, and a comparison stays one.
Colors(VbSource, TextLanguageIds.VbNet, "Keyword", "AndAlso");
Colors(VbSource.Replace("AndAlso", "andalso", StringComparison.Ordinal), TextLanguageIds.VbNet, "Keyword", "andalso");
Colors(VbSource, TextLanguageIds.VbNet, "Operator", "_");

const string JsSource = """
    const re = /ab+c/gi;
    let x = a / b, re2 = /x\/y/;
    foo(/lit/, 1 / 2);
    return /re/.test(s);
    /^start/.test(s);
    var y = 1 / 2 / 3;
    const s = `sum ${a + b} end`;
    const big = 1_000n, hex = 0xFF, f = .5e-3; // done
    """;

// A slash divides or opens a regular expression, and only what precedes it
// says which. Being wrong the harmless way — leaving a literal as operators —
// is preferred to coloring half a line as a string.
Colors(JsSource, TextLanguageIds.JavaScript, "String", "/ab+c/gi");
Colors(JsSource, TextLanguageIds.JavaScript, "String", @"/x\/y/");
Colors(JsSource, TextLanguageIds.JavaScript, "String", "/lit/");
Colors(JsSource, TextLanguageIds.JavaScript, "String", "/re/");
Colors(JsSource, TextLanguageIds.JavaScript, "String", "/^start/");
Assert(
    Spans(JsSource, TextLanguageIds.JavaScript)
        .Count(span => span.Category == "Operator" && span.Text == "/") == 4,
    "Every division should stay an operator.");
Colors(JsSource, TextLanguageIds.JavaScript, "Variable", "${a ");
Colors(JsSource, TextLanguageIds.JavaScript, "String", "`sum ");
Colors(JsSource, TextLanguageIds.JavaScript, "Number", "1_000n");
Colors(JsSource, TextLanguageIds.JavaScript, "Number", ".5e-3");
Colors(JsSource, TextLanguageIds.JavaScript, "Comment", "// done");

const string TsSource = """
    @Component({ selector: 'app' })
    export class Widget<T extends object> implements OnInit {
        async load(id?: number): Promise<Record<string, unknown>> {
            const re = /^id-\d+$/u;
            return { done: id as unknown satisfies number };
        }
    }
    """;

// TypeScript is JavaScript with types written into it, and its definition says
// so: the string, template and regular expression rules are imported.
Colors(TsSource, TextLanguageIds.TypeScript, "Directive", "@Component");
Colors(TsSource, TextLanguageIds.TypeScript, "Type", "unknown");
Colors(TsSource, TextLanguageIds.TypeScript, "Keyword", "satisfies");
Colors(TsSource, TextLanguageIds.TypeScript, "Keyword", "implements");
Colors(TsSource, TextLanguageIds.TypeScript, "String", @"/^id-\d+$/u");
Colors(TsSource, TextLanguageIds.TypeScript, "String", "'app'");

const string PythonSource = """"""
    @decorator
    class Model:
        """Doc "quoted" here.
        Second line."""
        path = r"C:\temp\x"
        def total(self, items: list[int]) -> str:
            # a comment
            n = 0x1F_00 + 1_000j
            return f"value {n!r} and {{literal}}"  # trailing
    """""";

Colors(PythonSource, TextLanguageIds.Python, "Directive", "@decorator");
Colors(PythonSource, TextLanguageIds.Python, "Comment", "# a comment");
// A docstring runs over its lines, and a raw string keeps its backslashes
// rather than escaping its own closing quote.
Colors(PythonSource, TextLanguageIds.Python, "String", "\"\"\"Doc \"quoted\" here.");
Colors(PythonSource, TextLanguageIds.Python, "String", "    Second line.\"\"\"");
Colors(PythonSource, TextLanguageIds.Python, "String", @"r""C:\temp\x""");
Colors(PythonSource, TextLanguageIds.Python, "Variable", "{n");
Colors(PythonSource, TextLanguageIds.Python, "String", " and {{literal}}\"");
Colors(PythonSource, TextLanguageIds.Python, "Number", "0x1F_00");
Colors(PythonSource, TextLanguageIds.Python, "Number", "1_000j");
Colors(PythonSource, TextLanguageIds.Python, "Type", "str");

const string RSource = """
    # A comment
    `my var` <- function(x, ...) {
      s <- r"(raw "quoted" text)"
      y <- x |> sum() %>% round(2L)
      f <- y ~ x + I(x^2)
      z %custom% w
      if (is.na(y)) return(NA_real_)
    }
    """;

Colors(RSource, TextLanguageIds.R, "Comment", "# A comment");
// A name that needed backticks is still a name, and an operator an author
// defines between percent signs is as much an operator as the built-in ones.
Colors(RSource, TextLanguageIds.R, "Variable", "`my var`");
Colors(RSource, TextLanguageIds.R, "String", "r\"(raw \"quoted\" text)\"");
Colors(RSource, TextLanguageIds.R, "Operator", "<-");
Colors(RSource, TextLanguageIds.R, "Operator", "|>");
Colors(RSource, TextLanguageIds.R, "Operator", "%>%");
Colors(RSource, TextLanguageIds.R, "Operator", "%custom%");
Colors(RSource, TextLanguageIds.R, "Operator", "~");
Colors(RSource, TextLanguageIds.R, "Number", "2L");
Colors(RSource, TextLanguageIds.R, "Keyword", "NA_real_");
Colors(RSource, TextLanguageIds.R, "Function", "is.na");

const string RustSource = """
    #![allow(dead_code)]
    /* outer /* inner */ still comment */
    #[derive(Debug)]
    pub fn make(s: &'static str) -> Result<Self, String> {
        let raw = r#"raw "quoted" text"#;
        let t = r##"has "# inside"##;
        let c = 'a'; let b = b'\n';
        println!("{}", 0xFF_u8 + 1_000i64);
    }
    """;

// A block comment nests, so the run ends where Rust ends it and not at the
// first close; an apostrophe is a lifetime or a character literal.
Colors(RustSource, TextLanguageIds.Rust, "Comment", "/* outer /* inner */ still comment */");
Colors(RustSource, TextLanguageIds.Rust, "Directive", "#![allow(dead_code)]");
Colors(RustSource, TextLanguageIds.Rust, "Directive", "#[derive(Debug)]");
Colors(RustSource, TextLanguageIds.Rust, "Variable", "'static");
Colors(RustSource, TextLanguageIds.Rust, "String", "'a'");
Colors(RustSource, TextLanguageIds.Rust, "String", @"b'\n'");
Colors(RustSource, TextLanguageIds.Rust, "String", "r#\"raw \"quoted\" text\"#");
Colors(RustSource, TextLanguageIds.Rust, "String", "r##\"has \"# inside\"##");
Colors(RustSource, TextLanguageIds.Rust, "Function", "println!");
Colors(RustSource, TextLanguageIds.Rust, "Number", "0xFF_u8");
Colors(RustSource, TextLanguageIds.Rust, "Number", "1_000i64");

const string PhpSource = """
    <?php
    #[Attribute]
    final class Greeter
    {
        public function greet(string $name): string
        {
            $text = <<<EOT
                Hello $name and {$this->title}
                EOT;
            $raw = <<<'EOT'
                No $interpolation here
                EOT;
            # hash comment
            return "hi $name" . '$literal';
        }
    }
    ?>
    """;

Colors(PhpSource, TextLanguageIds.Php, "Directive", "<?php");
Colors(PhpSource, TextLanguageIds.Php, "Directive", "?>");
Colors(PhpSource, TextLanguageIds.Php, "Directive", "#[Attribute]");
Colors(PhpSource, TextLanguageIds.Php, "Comment", "# hash comment");
Colors(PhpSource, TextLanguageIds.Php, "Variable", "$name");
// A heredoc reads its variables; a nowdoc and a single-quoted string keep
// theirs as the text they are.
Colors(PhpSource, TextLanguageIds.Php, "String", "<<<EOT");
Colors(PhpSource, TextLanguageIds.Php, "Variable", "{$this");
Colors(PhpSource, TextLanguageIds.Php, "String", "            No $interpolation here");
Colors(PhpSource, TextLanguageIds.Php, "String", "'$literal'");

// Incomplete input colors to where its language ends it and picks the next
// construct up again, and it is never rejected.
const string Incomplete = "var s = \"unterminated;\nint x = 1;\n/* open\nstill comment\n";
Colors(Incomplete, TextLanguageIds.CSharp, "String", "\"unterminated;");
Colors(Incomplete, TextLanguageIds.CSharp, "Number", "1");
Colors(Incomplete, TextLanguageIds.CSharp, "Comment", "/* open");
Colors(Incomplete, TextLanguageIds.CSharp, "Comment", "still comment");

// The same source with either line ending colors the same characters, and no
// span ever covers a line delimiter.
const string Lf = "// one\nint x = 1; /* two\nthree */ int y = 2;\n";
string crLf = Lf.Replace("\n", "\r\n", StringComparison.Ordinal);
Assert(
    Spans(Lf, TextLanguageIds.CSharp).Select(span => span.Text)
        .SequenceEqual(Spans(crLf, TextLanguageIds.CSharp).Select(span => span.Text), StringComparer.Ordinal),
    "CRLF and LF should color the same text.");
Assert(
    Spans(crLf, TextLanguageIds.CSharp).All(span => !span.Text.Contains('\r') && !span.Text.Contains('\n')),
    "No span should cover a line ending.");

// Offsets are UTF-16 offsets into the source, which is what the colorizer and
// the exporters index with, and they stay right past a surrogate pair.
const string Astral = "// caff\u00e8 \u2615\nvar e = \"gr\U0001F389ok\"; // ok";
Colors(Astral, TextLanguageIds.CSharp, "Comment", "// caff\u00e8 \u2615");
Colors(Astral, TextLanguageIds.CSharp, "String", "\"gr\U0001F389ok\"");
Colors(Astral, TextLanguageIds.CSharp, "Comment", "// ok");

foreach (string languageId in TextLanguageIds.All.Where(id => !TextLanguageIds.CanDetect(id)))
{
    Assert(
        Spans(string.Empty, languageId).Count == 0 &&
        Spans("\n\n", languageId).Count == 0,
        $"Empty input should color nothing in {languageId}.");
    Assert(
        Spans("plain words with no code in them", languageId)
            .All(span => span.Category is "Function" or "Punctuation" or "Keyword" or "Type" or "Operator" or "Number"),
        $"Ordinary prose should not be read as a string or a comment in {languageId}.");
}

// A snippet longer than the analyzer's limit is shown uncolored rather than
// slowly, and one under it stays quick.
string wide = "var x = " + string.Join(" + ", Enumerable.Range(0, 30000).Select(index => $"\"s{index}\"")) + ";";
Assert(wide.Length > 100_000 && Spans(wide, TextLanguageIds.CSharp).Count == 0, "A source past the limit is left uncolored.");
string tall = string.Concat(Enumerable.Repeat("    public int Value => 1_000; // note\n", 1200));
var watch = Stopwatch.StartNew();
int spanCount = Spans(tall, TextLanguageIds.CSharp).Count;
watch.Stop();
Assert(spanCount > 1200 && watch.ElapsedMilliseconds < 4000, $"A large snippet should still color, and did in {watch.ElapsedMilliseconds} ms.");

// Switching a container's language re-reads the source rather than handing
// back what the language before it found.
Assert(
    Spans("var x = 1;", TextLanguageIds.CSharp).Any(span => span.Category == "Type" && span.Text == "var") &&
    !Spans("var x = 1;", TextLanguageIds.Java).Any(span => span.Text == "var" && span.Category == "Keyword"),
    "Two languages should read the same source their own way.");

Console.WriteLine("SQLBI.Whiteboard smoke tests passed.");

// Every span is checked here rather than in each case: in order, not
// overlapping, inside the source, and covering only what the source already
// said. The gaps between them are the characters no rule claimed.
static IReadOnlyList<Span> Spans(string source, string languageId)
{
    TextLanguageAnalysis analysis = TextLanguageRegistry.Resolve(languageId).Analyze(source, "Snippet");
    var spans = new List<Span>();
    int previousEnd = 0;
    foreach (StyledTextSpan span in analysis.Spans)
    {
        Assert(span.Start >= previousEnd, $"Spans should not overlap in {languageId}.");
        Assert(
            span.Start >= 0 && span.Length > 0 && span.Start + span.Length <= source.Length,
            $"A span should stay inside the source in {languageId}.");
        previousEnd = span.Start + span.Length;
        spans.Add(new Span(
            span.Start,
            span.Length,
            CategoryOf(span.Style),
            source.Substring(span.Start, span.Length)));
    }

    return spans;
}

static void Colors(string source, string languageId, string category, string text)
{
    if (!Spans(source, languageId).Any(span => span.Category == category && span.Text == text))
    {
        throw new InvalidOperationException(
            $"{languageId} should color {Show(text)} as {category}.");
    }
}

static string Show(string text) =>
    "\"" + text.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal) + "\"";

// The palette the definitions share, named back from the color a reader sees,
// so that a change to it is a deliberate change to these tests too.
static string CategoryOf(TextRunStyle style) =>
    ((SolidColorBrush)style.Foreground).Color.ToString() switch
    {
        "#FF035ACA" => "Keyword",
        "#FF267F99" => "Type",
        "#FF795E26" => "Function",
        "#FFA31515" => "String",
        "#FFEE7F18" => "Number",
        "#FF268E26" => "Comment",
        "#FF6F42C1" => "Directive",
        // A hole in a string and a variable share the teal.
        "#FF168C8B" => "Variable",
        "#FF5E6470" => "Operator",
        "#FF808080" => "Punctuation",
        var color => throw new InvalidOperationException($"No category carries {color}."),
    };

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

internal readonly record struct Span(int Start, int Length, string Category, string Text);
