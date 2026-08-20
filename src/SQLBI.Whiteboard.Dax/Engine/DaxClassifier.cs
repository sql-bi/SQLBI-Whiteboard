namespace SQLBI.Whiteboard.Dax.Engine;

internal enum DaxClassification
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

    /// <summary>Brackets and braces, which the palette colors apart from the code inside them.</summary>
    Parenthesis,

    /// <summary>The name of the object being defined, on the left of := or =.</summary>
    DefinitionName,
    Operator,
    Punctuation
}

/// <summary>A run of source text and what it is. Spans are ordered and never overlap.</summary>
internal readonly record struct DaxSpan(int Start, int Length, DaxClassification Kind);

/// <summary>
/// Works out what every piece of DAX text is, so it can be colored. The answer comes from the same
/// parse the formatter uses, which is why a string containing "--", a comment spanning several
/// lines, or a variable named after a function are all classified correctly.
/// </summary>
internal static class DaxClassifier
{
    /// <summary>
    /// Bare words that are function arguments rather than table names: sort orders, ranking
    /// behaviors and the like. Without this they would read as tables, because that is what an
    /// unqualified identifier normally is.
    /// </summary>
    private static readonly HashSet<string> ArgumentWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "ASC", "DESC", "TRUE", "FALSE", "BLANK", "DENSE", "SKIP", "DEFAULT", "FIRST", "LAST",
        "NONE", "ONEWAY", "BOTH", "ALPHABETICAL", "KEEP", "REMOVE", "ABS", "REL",
        "BOOLEAN", "CURRENCY", "DATETIME", "DECIMAL", "DOUBLE", "INTEGER", "STRING",
        "YEAR", "QUARTER", "MONTH", "WEEK", "DAY", "HOUR", "MINUTE", "SECOND",
        "ROWS", "COLUMNS", "VAL", "EXPR", "ANYVAL", "SCALAR", "VARIANT", "INT64", "NUMERIC",
        "COLUMNREF", "TABLEREF", "CALENDARREF", "MEASUREREF", "ANYREF"
    };

    public static IReadOnlyList<DaxSpan> Classify(string dax)
    {
        var tokens = DaxLexer.Tokenize(dax);
        var script = DaxParser.Parse(dax);

        var roles = new Dictionary<int, DaxClassification>();
        var variables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Collect(script, roles, variables);

        var spans = new List<DaxSpan>();
        foreach (var token in tokens)
        {
            // Both kinds of comment are colored. Most comments are trailing ones, because a comment
            // written after code stays on the line of the code it describes.
            foreach (var comment in token.LeadingComments)
                spans.Add(new DaxSpan(comment.Start, comment.Text.Length, DaxClassification.Comment));

            if (token.Length > 0)
                spans.Add(new DaxSpan(token.Start, token.Length, KindOf(token, roles, variables)));

            foreach (var comment in token.TrailingComments)
                spans.Add(new DaxSpan(comment.Start, comment.Text.Length, DaxClassification.Comment));
        }

        spans.Sort((left, right) => left.Start.CompareTo(right.Start));
        return spans;
    }

    /// <summary>The declared variable names, which the renderer highlights wherever they appear.</summary>
    public static HashSet<string> VariableNames(string dax)
    {
        var variables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Collect(DaxParser.Parse(dax), new Dictionary<int, DaxClassification>(), variables);
        return variables;
    }

    /// <summary>Every table named in the code, whether quoted, qualifying a column, or bare.</summary>
    public static HashSet<string> TableNames(string dax)
    {
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var span in Classify(dax))
        {
            if (span.Kind != DaxClassification.TableName) continue;
            tables.Add(Unquote(dax.Substring(span.Start, span.Length)));
        }
        return tables;
    }

    /// <summary>The object a definition introduces, such as "Sales Amount EUR", or null.</summary>
    public static string? DefinedObjectName(string dax)
    {
        var script = DaxParser.Parse(dax);
        var first = script.Statements.FirstOrDefault();
        if (first is DaxStatement statement) first = statement.Body;

        var names = first switch
        {
            DaxDefinition definition => definition.NameTokens.Skip(definition.StartsWithKeyword ? 1 : 0),
            DaxFunctionDefinition definition => definition.NameTokens.Skip(1),
            _ => null
        };
        if (names is null) return null;

        var name = Join(names);
        // A measure written as [Name] = ... reads better without its brackets.
        if (name.StartsWith('[') && name.EndsWith(']') && name.Count(character => character == '[') == 1)
            name = name[1..^1];

        return name.Length == 0 ? null : name;
    }

    /// <summary>True when the code is a query rather than a definition.</summary>
    public static bool IsQuery(string dax)
    {
        foreach (var node in DaxParser.Parse(dax).Statements)
        {
            var statement = node is DaxStatement wrapped ? wrapped.Body : node;
            if (statement is DaxEvaluate or DaxDefine) return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- roles from the parse tree

    private static void Collect(DaxNode? node, Dictionary<int, DaxClassification> roles, HashSet<string> variables)
    {
        switch (node)
        {
            case DaxScript script:
                foreach (var statement in script.Statements) Collect(statement, roles, variables);
                break;

            case DaxStatement statement:
                Collect(statement.Body, roles, variables);
                break;

            case DaxDefine define:
                Mark(roles, define.Keyword, DaxClassification.Keyword);
                foreach (var definition in define.Definitions) Collect(definition, roles, variables);
                break;

            case DaxEvaluate evaluate:
                Mark(roles, evaluate.Keyword, DaxClassification.Keyword);
                Collect(evaluate.Expression, roles, variables);
                foreach (var clause in evaluate.Clauses)
                {
                    foreach (var keyword in clause.Keywords) Mark(roles, keyword, DaxClassification.Keyword);
                    foreach (var item in clause.Items) Collect(item, roles, variables);
                }
                break;

            case DaxDefinition definition:
                MarkNameTokens(roles, definition.NameTokens, definition.StartsWithKeyword);
                Collect(definition.Value, roles, variables);
                break;

            case DaxFunctionDefinition definition:
                MarkNameTokens(roles, definition.NameTokens, true);
                foreach (var parameter in definition.Parameters) Collect(parameter, roles, variables);
                Collect(definition.Body, roles, variables);
                break;

            case DaxParameter parameter:
                Mark(roles, parameter.Name, DaxClassification.Variable);
                variables.Add(parameter.Name.Text);
                foreach (var annotation in parameter.Annotations) Mark(roles, annotation, DaxClassification.Keyword);
                break;

            case DaxVarReturn varReturn:
                foreach (var variable in varReturn.Variables)
                {
                    Mark(roles, variable.Keyword, DaxClassification.Keyword);
                    Mark(roles, variable.Name, DaxClassification.Variable);
                    variables.Add(variable.Name.Text);
                    Collect(variable.Value, roles, variables);
                }
                if (varReturn.ReturnKeyword is not null)
                    Mark(roles, varReturn.ReturnKeyword, DaxClassification.Keyword);
                Collect(varReturn.Body, roles, variables);
                break;

            case DaxCall call:
                if (call.Callee is DaxLeaf name)
                    Mark(roles, name.Token, DaxClassification.Function);
                else
                    Collect(call.Callee, roles, variables);
                foreach (var argument in call.Arguments) Collect(argument, roles, variables);
                break;

            case DaxReference reference:
                MarkReference(roles, reference);
                break;

            case DaxBracketed bracketed:
                foreach (var item in bracketed.Items) Collect(item, roles, variables);
                break;

            case DaxBinary binary:
                Collect(binary.Left, roles, variables);
                if (binary.Operator.Kind == DaxTokenKind.Identifier)
                    Mark(roles, binary.Operator, DaxClassification.Keyword);
                Collect(binary.Right, roles, variables);
                break;

            case DaxUnary unary:
                if (unary.Operator.Kind == DaxTokenKind.Identifier)
                    Mark(roles, unary.Operator, DaxClassification.Keyword);
                Collect(unary.Operand, roles, variables);
                break;

            case DaxSuffixed suffixed:
                Collect(suffixed.Expression, roles, variables);
                Mark(roles, suffixed.Suffix, DaxClassification.Keyword);
                break;
        }
    }

    private static void MarkNameTokens(
        Dictionary<int, DaxClassification> roles,
        IReadOnlyList<DaxToken> tokens,
        bool startsWithKeyword)
    {
        for (var index = 0; index < tokens.Count; index++)
        {
            Mark(roles, tokens[index], index == 0 && startsWithKeyword
                ? DaxClassification.Keyword
                : DaxClassification.DefinitionName);
        }
    }

    /// <summary>In 'Sales'[Amount] the table qualifier and the column part are colored differently.</summary>
    private static void MarkReference(Dictionary<int, DaxClassification> roles, DaxReference reference)
    {
        foreach (var token in reference.Tokens)
        {
            Mark(roles, token, token.Kind == DaxTokenKind.ColumnReference
                ? DaxClassification.ColumnReference
                : DaxClassification.TableName);
        }
    }

    private static void Mark(Dictionary<int, DaxClassification> roles, DaxToken token, DaxClassification kind)
    {
        if (token.Length > 0) roles[token.Start] = kind;
    }

    // ---------------------------------------------------------------- roles from the token itself

    private static DaxClassification KindOf(
        DaxToken token,
        Dictionary<int, DaxClassification> roles,
        HashSet<string> variables)
    {
        if (roles.TryGetValue(token.Start, out var role))
        {
            // A function name the parser saw but DAX does not define is a user-defined function or a
            // table used with the implicit CALCULATE syntax; either way it is not a keyword.
            if (role == DaxClassification.Function && DaxFunctions.Canonical(token.Text) is null)
                return variables.Contains(token.Text) ? DaxClassification.Variable : DaxClassification.Text;
            return role;
        }

        return token.Kind switch
        {
            DaxTokenKind.String or DaxTokenKind.DateTime => DaxClassification.StringLiteral,
            DaxTokenKind.Number => DaxClassification.Number,
            DaxTokenKind.ColumnReference => DaxClassification.ColumnReference,
            DaxTokenKind.QuotedTable => DaxClassification.TableName,
            DaxTokenKind.QueryParameter => DaxClassification.QueryParameter,
            DaxTokenKind.Identifier => IdentifierKind(token, variables),
            DaxTokenKind.OpenParenthesis or DaxTokenKind.CloseParenthesis or
                DaxTokenKind.OpenBrace or DaxTokenKind.CloseBrace => DaxClassification.Parenthesis,
            DaxTokenKind.Operator => DaxClassification.Operator,
            DaxTokenKind.Comma or DaxTokenKind.Semicolon or DaxTokenKind.Colon or DaxTokenKind.Dot =>
                DaxClassification.Punctuation,
            _ => DaxClassification.Text
        };
    }

    /// <summary>
    /// An unqualified word is a variable when one was declared with that name, a keyword when DAX
    /// reserves it, an argument word such as DESC when it only makes sense inside a call, and a
    /// table name otherwise, since that is the only remaining thing it can be.
    /// </summary>
    private static DaxClassification IdentifierKind(DaxToken token, HashSet<string> variables)
    {
        if (variables.Contains(token.Text)) return DaxClassification.Variable;
        if (ArgumentWords.Contains(token.Text)) return DaxClassification.Keyword;
        return DaxClassification.TableName;
    }

    private static string Join(IEnumerable<DaxToken> tokens)
    {
        var text = string.Empty;
        foreach (var token in tokens)
        {
            if (text.Length > 0 && token.Kind != DaxTokenKind.ColumnReference) text += " ";
            text += token.Text;
        }
        return text;
    }

    private static string Unquote(string text) =>
        text.Length >= 2 && text[0] == '\'' && text[^1] == '\''
            ? text[1..^1].Replace("''", "'")
            : text;
}
