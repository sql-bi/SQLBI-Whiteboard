namespace SQLBI.Whiteboard.Dax.Engine;

/// <summary>
/// A recursive-descent parser with precedence climbing for expressions. It recognizes enough DAX
/// structure to lay code out well and treats anything it does not recognize as a plain token, so
/// unfamiliar syntax degrades to neutral formatting instead of being lost.
/// </summary>
internal sealed class DaxParser(IReadOnlyList<DaxToken> tokens, string source)
{
    private readonly IReadOnlyList<DaxToken> tokens = tokens;
    private readonly string source = source;
    private int position;

    /// <summary>Words that begin a definition inside a DEFINE block, and therefore end the previous one.</summary>
    private static readonly HashSet<string> DefinitionKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "MEASURE", "COLUMN", "TABLE", "FUNCTION", "VAR", "MPARAMETER", "CALCULATIONGROUP", "CALCULATIONITEM"
    };

    /// <summary>Words that terminate an expression because they open a new construct.</summary>
    private static readonly HashSet<string> StatementKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "DEFINE", "EVALUATE", "MEASURE", "COLUMN", "TABLE", "FUNCTION", "MPARAMETER",
        "CALCULATIONGROUP", "CALCULATIONITEM", "ORDER", "START", "RETURN", "DENSIFY", "WITH"
    };

    public static DaxScript Parse(string source)
    {
        var parser = new DaxParser(DaxLexer.Tokenize(source), source);
        return parser.ParseScript();
    }

    private DaxToken Current => tokens[position];
    private DaxToken Peek(int offset = 1) =>
        position + offset < tokens.Count ? tokens[position + offset] : tokens[^1];
    private bool AtEnd => Current.Kind == DaxTokenKind.EndOfFile;
    private DaxToken Advance() => tokens[position++];

    private DaxScript ParseScript()
    {
        var statements = new List<DaxNode>();
        while (!AtEnd)
        {
            var before = position;
            var body = ParseStatement();

            // A statement may be closed by a semicolon. It is optional, but it is the author's text,
            // so it is kept rather than quietly dropped.
            var terminator = Current.Kind == DaxTokenKind.Semicolon ? Advance() : null;
            statements.Add(new DaxStatement(body, terminator));

            if (position == before) statements.Add(new DaxLeaf(Advance())); // never spin
        }
        return new DaxScript(statements, Current);
    }

    private DaxNode ParseStatement()
    {
        if (Current.IsKeyword("DEFINE")) return ParseDefine();
        if (Current.IsKeyword("EVALUATE")) return ParseEvaluate();

        // VAR only introduces a definition inside DEFINE; on its own it opens a VAR ... RETURN block.
        if (Current.Kind == DaxTokenKind.Identifier && !Current.IsKeyword("VAR") &&
            DefinitionKeywords.Contains(Current.Text))
            return ParseDefinition();

        var standalone = TryParseStandaloneDefinition();
        return standalone ?? ParseExpression();
    }

    /// <summary>
    /// Recognizes a bare definition such as "Sales Amount % Year Total := ..." or
    /// "Product Rank = ...". A measure name may contain punctuation, so every token on the first
    /// line up to the assignment belongs to the name. Requiring the assignment on that same line
    /// prevents a later equality inside the expression from being mistaken for the definition.
    /// </summary>
    private DaxNode? TryParseStandaloneDefinition()
    {
        var scan = position;
        var names = new List<DaxToken>();
        while (scan < tokens.Count)
        {
            var token = tokens[scan];

            if (ContainsLineBreak(tokens[position].Start, token.Start))
                return null;

            if (token.IsOperator(":=") || token.IsOperator("="))
            {
                if (names.Count == 0)
                    return null;

                position = scan + 1;
                return new DaxDefinition(names, token, ParseExpression(), false);
            }

            if (token.Kind is DaxTokenKind.EndOfFile or DaxTokenKind.Semicolon)
                return null;

            // At the start of a statement these words introduce syntax rather than an object name.
            // Once a name has begun, the same words are legal parts of a measure name.
            if (names.Count == 0 && token.Kind == DaxTokenKind.Identifier &&
                (StatementKeywords.Contains(token.Text) || token.IsKeyword("VAR")))
                return null;

            names.Add(token);
            scan++;
        }

        return null;
    }

    private bool ContainsLineBreak(int start, int end)
    {
        for (var index = start; index < end; index++)
        {
            if (source[index] is '\r' or '\n')
                return true;
        }
        return false;
    }

    private DaxNode ParseDefine()
    {
        var keyword = Advance();
        var definitions = new List<DaxNode>();
        while (!AtEnd && Current.Kind == DaxTokenKind.Identifier && DefinitionKeywords.Contains(Current.Text))
        {
            var before = position;
            definitions.Add(ParseDefinition());
            if (position == before) break;
        }
        return new DaxDefine(keyword, definitions);
    }

    /// <summary>Parses one definition inside DEFINE: MEASURE/COLUMN/TABLE/VAR/MPARAMETER/FUNCTION.</summary>
    private DaxNode ParseDefinition()
    {
        var keyword = Advance();
        var names = new List<DaxToken> { keyword };

        while (!AtEnd &&
               Current.Kind is DaxTokenKind.Identifier or DaxTokenKind.QuotedTable or DaxTokenKind.ColumnReference &&
               !Current.IsOperator("="))
        {
            if (StatementKeywords.Contains(Current.Text)) break;
            names.Add(Advance());
        }

        if (!Current.IsOperator("=") && !Current.IsOperator(":="))
            return new DaxDefinition(names, Current, null, true);

        var assignment = Advance();

        if (keyword.IsKeyword("FUNCTION"))
            return ParseFunctionDefinitionBody(names, assignment);

        return new DaxDefinition(names, assignment, ParseExpression(), true);
    }

    private DaxNode ParseFunctionDefinitionBody(IReadOnlyList<DaxToken> names, DaxToken assignment)
    {
        var parameters = new List<DaxNode>();
        var parameterSeparators = new List<DaxToken>();
        if (Current.Kind == DaxTokenKind.OpenParenthesis)
        {
            Advance();
            while (!AtEnd && Current.Kind != DaxTokenKind.CloseParenthesis)
            {
                parameters.Add(ParseParameter());
                if (Current.Kind == DaxTokenKind.Comma) parameterSeparators.Add(Advance());
                else break;
            }
            if (Current.Kind == DaxTokenKind.CloseParenthesis) Advance();
        }

        DaxToken? arrow = null;
        DaxNode? body = null;
        if (Current.IsOperator("=>"))
        {
            arrow = Advance();
            body = ParseExpression();
        }
        return new DaxFunctionDefinition(names, assignment, parameters, parameterSeparators, arrow, body);
    }

    /// <summary>Parses "Name" or "Name : Type Subtype EvalKind".</summary>
    private DaxNode ParseParameter()
    {
        var name = Advance();
        DaxToken? colon = null;
        var annotations = new List<DaxToken>();
        if (Current.Kind == DaxTokenKind.Colon)
        {
            colon = Advance();
            while (Current.Kind == DaxTokenKind.Identifier) annotations.Add(Advance());
        }
        return new DaxParameter(name, colon, annotations);
    }

    private DaxNode ParseEvaluate()
    {
        var keyword = Advance();
        var expression = AtEnd ? null : ParseExpression();
        var clauses = new List<DaxClause>();

        while (!AtEnd)
        {
            if (Current.IsKeyword("ORDER") && Peek().IsKeyword("BY"))
                clauses.Add(ParseClause(2));
            else if (Current.IsKeyword("START") && Peek().IsKeyword("AT"))
                clauses.Add(ParseClause(2));
            else
                break;
        }
        return new DaxEvaluate(keyword, expression, clauses);
    }

    private DaxClause ParseClause(int keywordCount)
    {
        var keywords = new List<DaxToken>();
        for (var index = 0; index < keywordCount; index++) keywords.Add(Advance());

        var items = new List<DaxNode>();
        var separators = new List<DaxToken>();
        while (!AtEnd)
        {
            items.Add(ParseOrderMember());
            if (Current.Kind == DaxTokenKind.Comma) separators.Add(Advance());
            else break;
        }
        return new DaxClause(keywords, items, separators);
    }

    /// <summary>An ORDER BY member is an expression optionally followed by ASC or DESC.</summary>
    private DaxNode ParseOrderMember()
    {
        var expression = ParseExpression();
        if (Current.IsKeyword("ASC") || Current.IsKeyword("DESC"))
            return new DaxSuffixed(expression, Advance());
        return expression;
    }

    private static readonly DaxToken EmptyToken = new() { Kind = DaxTokenKind.Unknown, Text = "" };

    // ---------------------------------------------------------------- expressions

    public DaxNode ParseExpression() => ParseBinary(DaxPrecedence.Lowest);

    private DaxNode ParseBinary(int minimumPrecedence)
    {
        var left = ParseUnary();

        while (!AtEnd)
        {
            var precedence = DaxPrecedence.Of(Current);
            if (precedence < minimumPrecedence || precedence == DaxPrecedence.None) break;

            var op = Advance();
            // ^ is right associative; everything else is left associative.
            var next = precedence == DaxPrecedence.Power ? precedence : precedence + 1;
            var right = ParseBinary(next);
            left = new DaxBinary(left, op, right, precedence);
        }
        return left;
    }

    private DaxNode ParseUnary()
    {
        if (Current.IsOperator("-") || Current.IsOperator("+") || Current.IsOperator("!") || Current.IsKeyword("NOT"))
        {
            var op = Advance();
            return new DaxUnary(op, ParseUnary());
        }
        return ParsePostfix();
    }

    /// <summary>
    /// Attaches hierarchy levels such as Product[Category].[Subcategory] to a reference, and turns
    /// the implicit CALCULATE form [Measure] ( filters ) into a call.
    /// </summary>
    private DaxNode ParsePostfix()
    {
        var node = ParsePrimary();
        while (!AtEnd)
        {
            if (Current.Kind == DaxTokenKind.Dot && Peek().Kind == DaxTokenKind.ColumnReference &&
                node is DaxReference reference)
            {
                var parts = new List<DaxToken>(reference.Tokens) { Advance(), Advance() };
                node = new DaxReference(parts);
                continue;
            }

            if (Current.Kind == DaxTokenKind.OpenParenthesis && node is DaxReference)
            {
                var open = Advance();
                var arguments = ParseItems(DaxTokenKind.CloseParenthesis, out var close, out var separators);
                node = new DaxCall(node, string.Empty, open, arguments, close, separators);
                continue;
            }
            break;
        }
        return node;
    }

    private DaxNode ParsePrimary()
    {
        if (Current.IsKeyword("VAR")) return ParseVarReturn();

        switch (Current.Kind)
        {
            case DaxTokenKind.OpenParenthesis:
            case DaxTokenKind.OpenBrace:
                return ParseBracketed();

            case DaxTokenKind.QuotedTable:
            {
                var parts = new List<DaxToken> { Advance() };
                if (Current.Kind == DaxTokenKind.ColumnReference) parts.Add(Advance());
                return new DaxReference(parts);
            }

            case DaxTokenKind.ColumnReference:
                return new DaxReference([Advance()]);

            case DaxTokenKind.Identifier:
            {
                if (Peek().Kind == DaxTokenKind.OpenParenthesis)
                    return ParseCall();
                var parts = new List<DaxToken> { Advance() };
                if (Current.Kind == DaxTokenKind.ColumnReference) parts.Add(Advance());
                return parts.Count > 1 ? new DaxReference(parts) : new DaxLeaf(parts[0]);
            }

            default:
                return new DaxLeaf(Advance());
        }
    }

    private DaxNode ParseCall()
    {
        var name = Advance();
        var open = Advance();
        var arguments = ParseItems(DaxTokenKind.CloseParenthesis, out var close, out var separators);
        return new DaxCall(new DaxLeaf(name), name.Text, open, arguments, close, separators);
    }

    private DaxNode ParseBracketed()
    {
        var open = Advance();
        var closingKind = open.Kind == DaxTokenKind.OpenParenthesis
            ? DaxTokenKind.CloseParenthesis
            : DaxTokenKind.CloseBrace;
        var items = ParseItems(closingKind, out var close, out var separators);
        return new DaxBracketed(open, items, close, separators);
    }

    /// <summary>Parses a comma separated list up to the matching closing token, which is consumed.</summary>
    private List<DaxNode> ParseItems(DaxTokenKind closing, out DaxToken? closingToken, out List<DaxToken> separators)
    {
        var items = new List<DaxNode>();
        separators = [];
        closingToken = null;
        if (Current.Kind == closing)
        {
            closingToken = Advance();
            return items;
        }

        while (!AtEnd)
        {
            // DAX accepts omitted arguments, as in TOPN ( 1, Product, Product[Weight], , [Price] ).
            // Recording them as empty items keeps the commas intact instead of consuming a bracket.
            if (Current.Kind == closing)
            {
                items.Add(new DaxLeaf(EmptyToken));
                break;
            }
            if (Current.Kind == DaxTokenKind.Comma)
            {
                items.Add(new DaxLeaf(EmptyToken));
                separators.Add(Advance());
                continue;
            }

            var before = position;
            items.Add(ParseExpression());

            // An expression the parser could not advance past would loop forever; take the token
            // as a leaf instead so that no input is ever dropped.
            if (position == before) items.Add(new DaxLeaf(Advance()));

            if (Current.Kind == DaxTokenKind.Comma)
            {
                separators.Add(Advance());
                continue;
            }
            break;
        }

        if (Current.Kind == closing) closingToken = Advance();
        return items;
    }

    private DaxNode ParseVarReturn()
    {
        var variables = new List<DaxVariable>();
        while (Current.IsKeyword("VAR"))
        {
            var keyword = Advance();
            var name = AtEnd ? EmptyToken : Advance();
            if (!Current.IsOperator("=") && !Current.IsOperator(":="))
            {
                variables.Add(new DaxVariable(keyword, name, EmptyToken, new DaxLeaf(EmptyToken)));
                break;
            }
            var assignment = Advance();
            variables.Add(new DaxVariable(keyword, name, assignment, ParseExpression()));
        }

        if (!Current.IsKeyword("RETURN"))
            return new DaxVarReturn(variables, null, null);

        var returnKeyword = Advance();
        return new DaxVarReturn(variables, returnKeyword, ParseExpression());
    }
}

internal static class DaxPrecedence
{
    public const int None = -1;
    public const int Lowest = 0;

    private const int Or = 1;          // ||
    private const int And = 2;         // &&
    private const int In = 3;          // IN
    private const int Comparison = 4;  // = == <> < > <= >=
    private const int Concatenation = 5; // &
    private const int Additive = 6;    // + -
    private const int Multiplicative = 7; // * /
    public const int Power = 8;        // ^

    public static int Of(DaxToken token)
    {
        if (token.IsKeyword("IN")) return In;
        if (token.Kind != DaxTokenKind.Operator) return None;
        return token.Text switch
        {
            "||" => Or,
            "&&" => And,
            "=" or "==" or "<>" or "<" or ">" or "<=" or ">=" => Comparison,
            "&" => Concatenation,
            "+" or "-" => Additive,
            "*" or "/" => Multiplicative,
            "^" => Power,
            _ => None
        };
    }
}
