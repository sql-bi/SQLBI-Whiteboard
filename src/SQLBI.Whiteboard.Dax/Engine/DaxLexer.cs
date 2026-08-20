namespace SQLBI.Whiteboard.Dax.Engine;

internal enum DaxTokenKind
{
    EndOfFile,
    Identifier,        // Sales, MyVar, NORM.DIST
    QuotedTable,       // 'Sales Header'
    ColumnReference,   // [Net Price]
    QueryParameter,    // @Risk
    Number,            // 1, 1.5, 1.5E+10
    String,            // "text"
    DateTime,          // dt"2024-01-01"
    Operator,          // + - * / ^ & = == <> < > <= >= && || ! => :=
    OpenParenthesis,
    CloseParenthesis,
    OpenBrace,
    CloseBrace,
    Comma,
    Semicolon,
    Colon,
    Dot,
    Unknown
}

/// <summary>A comment. Block comments written on a single line can stay inline; the rest cannot.</summary>
/// <param name="Start">Offset in the source, so the code can be colored without a second scan.</param>
internal sealed record DaxComment(string Text, bool IsBlock, int Start);

internal sealed class DaxToken
{
    public DaxTokenKind Kind { get; init; }

    /// <summary>Text as it will be printed, after case normalization.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Comments written before this token, on lines of their own.</summary>
    public IReadOnlyList<DaxComment> LeadingComments { get; init; } = [];

    /// <summary>Comments written after this token on the same line, which is where they stay.</summary>
    public List<DaxComment> TrailingComments { get; } = [];

    /// <summary>Offset of the token in the source it was read from.</summary>
    public int Start { get; init; }

    /// <summary>Length of the token in the source, which may differ from Text after normalization.</summary>
    public int Length { get; init; }

    public bool IsKeyword(string keyword) =>
        Kind == DaxTokenKind.Identifier && Text.Equals(keyword, StringComparison.OrdinalIgnoreCase);

    public bool IsOperator(string text) =>
        Kind == DaxTokenKind.Operator && Text.Equals(text, StringComparison.Ordinal);

    public override string ToString() => $"{Kind}:{Text}";
}

/// <summary>
/// Converts DAX source into tokens. Comments are attached to the token that follows them, so the
/// printer can never move code onto a line that a single-line comment would swallow.
/// </summary>
internal static class DaxLexer
{
    /// <summary>
    /// The only words upper-cased on sight. Every other keyword is normalized by the printer, which
    /// knows whether the word sits in a keyword position: a variable called Total or Year is a name,
    /// not a keyword, and renaming it would be a change to the author's code.
    /// </summary>
    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "TRUE", "FALSE"
    };

    /// <summary>Two-character operators, checked before single characters.</summary>
    private static readonly string[] TwoCharacterOperators = ["=>", ":=", "==", "<>", ">=", "<=", "&&", "||"];

    public static List<DaxToken> Tokenize(string source)
    {
        var tokens = new List<DaxToken>();
        var comments = new List<DaxComment>();
        var index = 0;
        var sawTokenOnThisLine = false;

        while (index < source.Length)
        {
            var character = source[index];

            if (character is '\r' or '\n')
            {
                if (character == '\r' && index + 1 < source.Length && source[index + 1] == '\n') index++;
                index++;
                sawTokenOnThisLine = false;
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                index++;
                continue;
            }

            // Line comments: both -- and // are legal DAX.
            if (StartsWith(source, index, "--") || StartsWith(source, index, "//"))
            {
                var end = IndexOfLineBreak(source, index);
                Add(new DaxComment(source[index..end].TrimEnd(), false, index));
                index = end;
                continue;
            }

            if (StartsWith(source, index, "/*"))
            {
                var close = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
                var end = close < 0 ? source.Length : close + 2;
                var text = source[index..end];
                Add(new DaxComment(text, true, index));
                if (text.Contains('\n')) sawTokenOnThisLine = false;
                index = end;
                continue;
            }

            var start = index;
            var kind = DaxTokenKind.Unknown;

            // dt"2024-01-01" — the prefix belongs to the literal.
            if ((character is 'd' or 'D') && index + 2 < source.Length &&
                source[index + 1] is 't' or 'T' && source[index + 2] == '"')
            {
                index = ScanDelimited(source, index + 2, '"');
                kind = DaxTokenKind.DateTime;
            }
            else if (character == '"')
            {
                index = ScanDelimited(source, index, '"');
                kind = DaxTokenKind.String;
            }
            else if (character == '\'')
            {
                index = ScanDelimited(source, index, '\'');
                kind = DaxTokenKind.QuotedTable;
            }
            else if (character == '[')
            {
                index++;
                while (index < source.Length && source[index] != ']') index++;
                if (index < source.Length) index++;
                kind = DaxTokenKind.ColumnReference;
            }
            else if (character == '@')
            {
                index++;
                while (index < source.Length && IsIdentifierPart(source[index])) index++;
                kind = DaxTokenKind.QueryParameter;
            }
            else if (char.IsDigit(character) || (character == '.' && index + 1 < source.Length && char.IsDigit(source[index + 1])))
            {
                index = ScanNumber(source, index);
                kind = DaxTokenKind.Number;
            }
            else if (IsIdentifierStart(character))
            {
                index = ScanIdentifier(source, index);
                kind = DaxTokenKind.Identifier;
            }
            else
            {
                var two = index + 1 < source.Length ? source.Substring(index, 2) : string.Empty;
                if (Array.IndexOf(TwoCharacterOperators, two) >= 0)
                {
                    index += 2;
                    kind = DaxTokenKind.Operator;
                }
                else
                {
                    index++;
                    kind = character switch
                    {
                        '(' => DaxTokenKind.OpenParenthesis,
                        ')' => DaxTokenKind.CloseParenthesis,
                        '{' => DaxTokenKind.OpenBrace,
                        '}' => DaxTokenKind.CloseBrace,
                        ',' => DaxTokenKind.Comma,
                        ';' => DaxTokenKind.Semicolon,
                        ':' => DaxTokenKind.Colon,
                        '.' => DaxTokenKind.Dot,
                        '+' or '-' or '*' or '/' or '^' or '&' or '=' or '<' or '>' or '!' => DaxTokenKind.Operator,
                        _ => DaxTokenKind.Unknown
                    };
                }
            }

            tokens.Add(new DaxToken
            {
                Kind = kind,
                Text = Normalize(kind, source[start..index], source, index),
                LeadingComments = comments.Count == 0 ? [] : comments.ToArray(),
                Start = start,
                Length = index - start
            });
            comments.Clear();
            sawTokenOnThisLine = true;
        }

        tokens.Add(new DaxToken
        {
            Kind = DaxTokenKind.EndOfFile,
            LeadingComments = comments.Count == 0 ? [] : comments.ToArray(),
            Start = source.Length
        });
        return tokens;

        // A comment that follows code on the same line describes that code, so it belongs to the
        // token before it. Anything else introduces the code that follows.
        void Add(DaxComment comment)
        {
            if (sawTokenOnThisLine && tokens.Count > 0) tokens[^1].TrailingComments.Add(comment);
            else comments.Add(comment);
        }
    }

    /// <summary>
    /// Upper-cases structural keywords, and function names only when the identifier is immediately
    /// followed by "(" and is a documented DAX function. An identifier used as a name — a variable,
    /// a table, a user-defined function — is always left exactly as the author wrote it.
    /// </summary>
    private static string Normalize(DaxTokenKind kind, string text, string source, int positionAfterToken)
    {
        if (kind != DaxTokenKind.Identifier)
            return text;

        if (Keywords.Contains(text))
            return text.ToUpperInvariant();

        return IsFollowedByOpenParenthesis(source, positionAfterToken)
            ? DaxFunctions.Canonical(text) ?? text
            : text;
    }

    private static bool IsFollowedByOpenParenthesis(string source, int index)
    {
        while (index < source.Length && char.IsWhiteSpace(source[index])) index++;
        return index < source.Length && source[index] == '(';
    }

    /// <summary>Scans a quoted run, treating a doubled delimiter as an escaped delimiter.</summary>
    private static int ScanDelimited(string source, int index, char delimiter)
    {
        index++; // opening delimiter
        while (index < source.Length)
        {
            if (source[index] != delimiter)
            {
                index++;
                continue;
            }
            if (index + 1 < source.Length && source[index + 1] == delimiter)
            {
                index += 2;
                continue;
            }
            return index + 1;
        }
        return index; // unterminated literal; the parser reports it
    }

    private static int ScanNumber(string source, int index)
    {
        while (index < source.Length && (char.IsDigit(source[index]) || source[index] == '.')) index++;

        // Exponent: 1.5E+10, 2e-3. Only consume it when digits actually follow, so that a
        // column reference such as Sales[E] cannot be mistaken for an exponent.
        if (index < source.Length && source[index] is 'e' or 'E')
        {
            var lookahead = index + 1;
            if (lookahead < source.Length && source[lookahead] is '+' or '-') lookahead++;
            if (lookahead < source.Length && char.IsDigit(source[lookahead]))
            {
                index = lookahead;
                while (index < source.Length && char.IsDigit(source[index])) index++;
            }
        }
        return index;
    }

    /// <summary>
    /// Scans an identifier, absorbing the dots inside names such as NORM.DIST or CHISQ.INV.RT.
    /// A dot that is not followed by an identifier character is left as its own token, so
    /// 'Date'.[Date] still lexes as three tokens.
    /// </summary>
    private static int ScanIdentifier(string source, int index)
    {
        while (index < source.Length && IsIdentifierPart(source[index])) index++;
        while (index + 1 < source.Length && source[index] == '.' && IsIdentifierPart(source[index + 1]))
        {
            index++;
            while (index < source.Length && IsIdentifierPart(source[index])) index++;
        }
        return index;
    }

    private static bool IsIdentifierStart(char character) => char.IsLetter(character) || character == '_';
    private static bool IsIdentifierPart(char character) => char.IsLetterOrDigit(character) || character == '_';

    private static bool StartsWith(string source, int index, string value) =>
        index + value.Length <= source.Length && string.CompareOrdinal(source, index, value, 0, value.Length) == 0;

    private static int IndexOfLineBreak(string source, int index)
    {
        while (index < source.Length && source[index] is not ('\r' or '\n')) index++;
        return index;
    }
}
