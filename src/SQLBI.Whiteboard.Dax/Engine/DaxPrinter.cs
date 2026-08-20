namespace SQLBI.Whiteboard.Dax.Engine;

/// <summary>Turns a parsed script into a layout document.</summary>
internal static class DaxPrinter
{
    public static string Print(DaxScript script, int maximumLineLength) =>
        DocRenderer.Render(BuildScript(script), maximumLineLength);

    private static Doc BuildScript(DaxScript script)
    {
        var statements = script.Statements.Select(Build).ToList();
        var body = Doc.Join(Doc.Hard(1), statements);

        // Comments that trailed the final token would otherwise be lost.
        var trailing = Comments(script.EndOfFile);
        return trailing == Doc.Empty
            ? body
            : Doc.Concat(body, Doc.Hard(), trailing);
    }

    private static Doc Build(DaxNode node) => node switch
    {
        DaxLeaf leaf => Token(leaf.Token),
        DaxReference reference => BuildReference(reference),
        DaxCall call => BuildCall(call),
        DaxBracketed bracketed => BuildBracketed(bracketed),
        DaxBinary binary => BuildBinary(binary),
        DaxUnary unary => BuildUnary(unary),
        DaxSuffixed suffixed => Doc.Concat(Build(suffixed.Expression), Doc.Text(" "), Keyword(suffixed.Suffix)),
        DaxParameter parameter => BuildParameter(parameter),
        DaxVarReturn varReturn => BuildVarReturn(varReturn),
        DaxDefinition definition => BuildDefinition(definition, 0),
        DaxFunctionDefinition definition => BuildFunctionDefinition(definition),
        DaxStatement statement => Doc.Concat(
            Build(statement.Body),
            statement.Terminator is null ? Doc.Empty : Token(statement.Terminator)),
        DaxDefine define => BuildDefine(define),
        DaxEvaluate evaluate => BuildEvaluate(evaluate),
        DaxScript script => BuildScript(script),
        _ => Doc.Empty
    };

    // ---------------------------------------------------------------- tokens and comments

    /// <summary>Emits a token with the comments written before it and after it on its own line.</summary>
    private static Doc Token(DaxToken token) =>
        Doc.Concat(Comments(token), Doc.Text(token.Text), Trailing(token));

    /// <summary>
    /// Emits a word the parser identified as a keyword, upper-cased. Case is only normalized here,
    /// where the position is known, so an identifier that happens to spell a keyword is left alone.
    /// </summary>
    private static Doc Keyword(DaxToken token) =>
        Doc.Concat(Comments(token), Doc.Text(token.Text.ToUpperInvariant()), Trailing(token));

    /// <summary>
    /// Renders leading comments. A single-line comment always forces a break after it, otherwise the
    /// code that follows would end up commented out. A block comment with no line break stays inline.
    /// </summary>
    private static Doc Comments(DaxToken token)
    {
        if (token.LeadingComments.Count == 0) return Doc.Empty;

        var parts = new List<Doc>();
        foreach (var comment in token.LeadingComments)
        {
            if (comment.IsBlock && !comment.Text.Contains('\n'))
            {
                parts.Add(Doc.Text(comment.Text));
                parts.Add(Doc.Text(" "));
                continue;
            }
            parts.Add(Doc.Text(comment.Text));
            parts.Add(Doc.Hard());
        }
        return Doc.Concat(parts);
    }

    /// <summary>
    /// Renders a comment that follows its token on the same line. It stays there, and stops the
    /// enclosing group from collapsing, because everything after a line comment would be commented
    /// out if the code were folded onto one line.
    /// </summary>
    private static Doc Trailing(DaxToken token)
    {
        if (token.TrailingComments.Count == 0) return Doc.Empty;

        var parts = new List<Doc>();
        foreach (var comment in token.TrailingComments)
        {
            // A block comment on one line can stay exactly where it was written. Anything else has
            // to end the line, so it waits until the line ends, wherever the layout puts that.
            if (comment.IsBlock && !comment.Text.Contains('\n'))
            {
                parts.Add(Doc.Text(" "));
                parts.Add(Doc.Text(comment.Text));
                continue;
            }
            parts.Add(Doc.LineSuffix(" " + comment.Text));
            parts.Add(Doc.BreakParent);
        }
        return Doc.Concat(parts);
    }

    /// <summary>
    /// Renders comments written just before a closing bracket. They start their own line and the
    /// bracket's own separator ends it, so the comment never runs into the code around it.
    /// </summary>
    private static Doc CommentsBeforeClose(DaxToken? token)
    {
        if (token is null || token.LeadingComments.Count == 0) return Doc.Empty;

        var parts = new List<Doc>();
        foreach (var comment in token.LeadingComments)
        {
            parts.Add(Doc.Hard());
            parts.Add(Doc.Text(comment.Text));
        }
        return Doc.Concat(parts);
    }

    /// <summary>Joins tokens with no separator, for references such as 'Sales'[Amount].</summary>
    private static Doc BuildReference(DaxReference reference) =>
        Doc.Concat(reference.Tokens.Select(Token).ToArray());

    private static Doc Words(IReadOnlyList<DaxToken> tokens, bool firstIsKeyword = false)
    {
        var parts = new List<Doc>();
        for (var index = 0; index < tokens.Count; index++)
        {
            // A table name and the column reference that follows it are printed as one word.
            if (index > 0 && tokens[index].Kind != DaxTokenKind.ColumnReference)
                parts.Add(Doc.Text(" "));
            parts.Add(index == 0 && firstIsKeyword ? Keyword(tokens[index]) : Token(tokens[index]));
        }
        return Doc.Concat(parts);
    }

    /// <summary>Every word of a clause such as ORDER BY or START AT is a keyword.</summary>
    private static Doc Keywords(IReadOnlyList<DaxToken> tokens) =>
        Doc.Join(Doc.Text(" "), tokens.Select(Keyword));

    // ---------------------------------------------------------------- expressions

    private static Doc BuildCall(DaxCall call)
    {
        // A comment written before the call belongs to the line above it, not inside the argument
        // list, so it is lifted out of the group. Otherwise it would expand a call that fits.
        var leading = call.Callee is DaxLeaf named ? Comments(named.Token) : Doc.Empty;
        var callee = call.Callee is DaxLeaf leaf ? Doc.Text(leaf.Token.Text) : Build(call.Callee);
        var beforeOpen = Comments(call.OpenParenthesis);
        var beforeClose = CommentsBeforeClose(call.CloseParenthesis);

        var afterClose = call.CloseParenthesis is null ? Doc.Empty : Trailing(call.CloseParenthesis);

        if (call.Arguments.Count == 0)
            return Doc.Concat(
                leading, callee, Doc.Text(" "), beforeOpen, Doc.Text("("),
                Trailing(call.OpenParenthesis), beforeClose, Doc.Text(")"), afterClose);

        var groups = DaxArgumentShapes.Group(call.FunctionName, call.Arguments);
        var parts = new List<Doc> { callee, Doc.Text(" "), beforeOpen, Doc.Text("("), Trailing(call.OpenParenthesis) };
        var separator = 0;

        for (var index = 0; index < groups.Count; index++)
        {
            var group = groups[index];
            var rendered = BuildArgumentGroup(call, group, ref separator);
            var last = index == groups.Count - 1;

            // An omitted argument takes its own position, so its commas end up on their own line
            // when the call is expanded rather than running together as ",,".
            if (rendered == Doc.Empty)
                parts.Add(last ? Doc.Empty : Doc.Indent(1 + group.ExtraIndent, Doc.Line(string.Empty)));
            else
                parts.Add(Doc.Indent(1 + group.ExtraIndent, Doc.Concat(Doc.Line(), rendered)));

            if (!last) parts.Add(Comma(call.Separators, ref separator));
        }

        parts.Add(Doc.Indent(1, beforeClose));
        parts.Add(Doc.Line());
        parts.Add(Doc.Text(")"));

        // SWITCH is always expanded so that its condition and result pairs stay readable.
        if (DaxArgumentShapes.AlwaysExpands(call.FunctionName)) parts.Add(Doc.BreakParent);

        return Doc.Concat(leading, Doc.Group(Doc.Concat(parts)), afterClose);
    }

    /// <summary>
    /// Renders the arguments that share a line, such as the "Name", value pairs of ADDCOLUMNS. When
    /// the pair does not fit, the name keeps its own line and the value is indented under it, rather
    /// than leaving an opening bracket dangling after the name.
    /// </summary>
    private static Doc BuildArgumentGroup(DaxCall call, DaxArgumentGroup group, ref int separator)
    {
        var arguments = group.Arguments.Select(argument => Build(call.Arguments[argument])).ToList();
        if (arguments.Count < 2)
            return arguments.Count == 0 ? Doc.Empty : arguments[0];

        var parts = new List<Doc> { arguments[0] };
        for (var index = 1; index < arguments.Count; index++)
        {
            parts.Add(Comma(call.Separators, ref separator));
            if (arguments[index] != Doc.Empty)
                parts.Add(Doc.Indent(1, Doc.Concat(Doc.Line(), arguments[index])));
        }
        return Doc.Group(Doc.Concat(parts));
    }

    /// <summary>
    /// The next comma of a list. The token itself is printed so that a comment written after it
    /// stays on the line it was written on.
    /// </summary>
    private static Doc Comma(IReadOnlyList<DaxToken> separators, ref int index) =>
        index < separators.Count ? Token(separators[index++]) : Doc.Text(",");

    private static Doc BuildBracketed(DaxBracketed bracketed)
    {
        var open = bracketed.IsBrace ? "{" : "(";
        var close = bracketed.IsBrace ? "}" : ")";
        var opening = Doc.Concat(Comments(bracketed.Open), Doc.Text(open), Trailing(bracketed.Open));
        var beforeClose = CommentsBeforeClose(bracketed.Close);

        var afterClose = bracketed.Close is null ? Doc.Empty : Trailing(bracketed.Close);

        if (bracketed.Items.Count == 0)
            return Doc.Concat(opening, beforeClose, Doc.Text(close), afterClose);

        var separator = 0;
        var pieces = new List<Doc>();
        foreach (var item in bracketed.Items)
        {
            if (pieces.Count > 0)
            {
                pieces.Add(Comma(bracketed.Separators, ref separator));
                pieces.Add(Doc.Line());
            }
            pieces.Add(Build(item));
        }
        var items = Doc.Concat(pieces);

        return Doc.Concat(
            Doc.Group(Doc.Concat(
                opening,
                Doc.Indent(1, Doc.Concat(Doc.Line(), items, beforeClose)),
                Doc.Line(),
                Doc.Text(close))),
            afterClose);
    }

    /// <summary>
    /// Prints a chain of operators of equal precedence. When the chain does not fit, every operand
    /// after the first starts a new line led by its operator, which keeps the structure obvious.
    /// </summary>
    private static Doc BuildBinary(DaxBinary binary)
    {
        var operands = new List<Doc>();
        var operators = new List<DaxToken>();
        Flatten(binary, binary.Precedence, operands, operators);

        var symbols = operators
            .Select(op => op.Kind == DaxTokenKind.Identifier ? Keyword(op) : Token(op))
            .ToList();

        // Operators kept inline, so a long operand breaks inside itself and the operator stays
        // attached to it. This is the arrangement that always works.
        var inline = new List<Doc> { operands[0] };
        for (var index = 0; index < symbols.Count; index++)
        {
            inline.Add(Doc.Text(" "));
            inline.Add(symbols[index]);
            inline.Add(Doc.Text(" "));
            inline.Add(operands[index + 1]);
        }
        var operatorsInline = Doc.Concat(inline);

        // A comment inside the expression rules out both of the one-line-per-operand arrangements.
        if (operatorsInline.ForcesBreak)
            return operatorsInline;

        // One operand per line, each led by its operator. Operands are held flat so that this is
        // only chosen when every one of them fits, and not as a way to break a long operand twice.
        var stacked = new List<Doc> { Doc.Flat(operands[0]) };
        var continuation = new List<Doc>();
        for (var index = 0; index < symbols.Count; index++)
        {
            continuation.Add(Doc.Line());
            continuation.Add(symbols[index]);
            continuation.Add(Doc.Text(" "));
            continuation.Add(Doc.Flat(operands[index + 1]));
        }
        stacked.Add(Doc.Indent(1, Doc.Concat(continuation)));

        return Doc.Choose(Doc.Flat(operatorsInline), Doc.Concat(stacked), operatorsInline);
    }

    private static void Flatten(DaxNode node, int precedence, List<Doc> operands, List<DaxToken> operators)
    {
        if (node is DaxBinary binary && binary.Precedence == precedence)
        {
            Flatten(binary.Left, precedence, operands, operators);
            operators.Add(binary.Operator);
            Flatten(binary.Right, precedence, operands, operators);
            return;
        }
        operands.Add(Build(node));
    }

    /// <summary>
    /// NOT always takes a space. A symbol binds directly to its operand, except before a bracketed
    /// expression, where "- ( x )" reads as negation rather than as part of the bracket.
    /// </summary>
    /// <summary>
    /// A sign binds directly to a number, as in -1, and is separated from anything else, so that
    /// "- Sales[Amount]" and "- ( BLANK () )" read as negation rather than as part of what follows.
    /// </summary>
    private static Doc BuildUnary(DaxUnary unary)
    {
        var spaced = unary.NeedsSpace || unary.Operand is not DaxLeaf { Token.Kind: DaxTokenKind.Number };
        return Doc.Concat(
            unary.NeedsSpace ? Keyword(unary.Operator) : Token(unary.Operator),
            spaced ? Doc.Text(" ") : Doc.Empty,
            Build(unary.Operand));
    }

    private static Doc BuildParameter(DaxParameter parameter)
    {
        if (parameter.Colon is null) return Token(parameter.Name);
        var parts = new List<Doc> { Token(parameter.Name), Doc.Text(": ") };
        parts.Add(Doc.Join(Doc.Text(" "), parameter.Annotations.Select(Token)));
        return Doc.Concat(parts);
    }

    // ---------------------------------------------------------------- statements

    /// <summary>
    /// VAR blocks always expand: the name and its expression are easier to scan on separate lines,
    /// and RETURN lines up with the VAR keywords it belongs to.
    /// </summary>
    private static Doc BuildVarReturn(DaxVarReturn varReturn)
    {
        var parts = new List<Doc>();
        foreach (var variable in varReturn.Variables)
        {
            if (parts.Count > 0) parts.Add(Doc.Hard());
            parts.Add(Doc.Concat(
                Keyword(variable.Keyword),
                Doc.Text(" "),
                Token(variable.Name),
                Doc.Text(" "),
                Token(variable.Assignment),
                Doc.Indent(1, Doc.Concat(Doc.Hard(), Build(variable.Value)))));
        }

        if (varReturn.ReturnKeyword is not null)
        {
            if (parts.Count > 0) parts.Add(Doc.Hard());
            parts.Add(Keyword(varReturn.ReturnKeyword));
            if (varReturn.Body is not null)
                parts.Add(Doc.Indent(1, Doc.Concat(Doc.Hard(), Build(varReturn.Body))));
        }
        return Doc.Concat(parts);
    }

    /// <summary>
    /// Prints "MEASURE Sales[M] =" or "Sales Amount :=" with the expression on the following line.
    /// A definition inside DEFINE indents its expression; one written on its own does not, so a
    /// pasted measure starts at the left margin.
    /// </summary>
    private static Doc BuildDefinition(DaxDefinition definition, int valueIndent)
    {
        var head = Doc.Concat(
            Words(definition.NameTokens, definition.StartsWithKeyword),
            Doc.Text(" "),
            Token(definition.Assignment));

        return definition.Value is null
            ? head
            : Doc.Concat(head, Doc.Indent(valueIndent, Doc.Concat(Doc.Hard(), Build(definition.Value))));
    }

    private static Doc BuildFunctionDefinition(DaxFunctionDefinition definition)
    {
        var parts = new List<Doc>
        {
            Words(definition.NameTokens, firstIsKeyword: true),
            Doc.Text(" "),
            Token(definition.Assignment),
            Doc.Text(" ")
        };

        parts.Add(definition.Parameters.Count == 0
            ? Doc.Text("()")
            : Doc.Group(Doc.Concat(
                Doc.Text("("),
                Doc.Indent(1, Doc.Concat(Doc.Line(), Parameters(definition))),
                Doc.Line(),
                Doc.Text(")"))));

        if (definition.Arrow is not null)
        {
            parts.Add(Doc.Text(" "));
            parts.Add(Token(definition.Arrow));
        }
        if (definition.Body is not null)
            parts.Add(Doc.Indent(1, Doc.Concat(Doc.Hard(), Build(definition.Body))));

        return Doc.Concat(parts);
    }

    private static Doc Parameters(DaxFunctionDefinition definition)
    {
        var separator = 0;
        var parts = new List<Doc>();
        foreach (var parameter in definition.Parameters)
        {
            if (parts.Count > 0)
            {
                parts.Add(Comma(definition.Separators, ref separator));
                parts.Add(Doc.Line());
            }
            parts.Add(Build(parameter));
        }
        return Doc.Concat(parts);
    }

    private static Doc BuildDefine(DaxDefine define)
    {
        var parts = new List<Doc> { Keyword(define.Keyword) };
        foreach (var definition in define.Definitions)
        {
            var body = definition is DaxDefinition typed ? BuildDefinition(typed, 1) : Build(definition);
            parts.Add(Doc.Indent(1, Doc.Concat(Doc.Hard(), body)));
        }
        return Doc.Concat(parts);
    }

    private static Doc BuildEvaluate(DaxEvaluate evaluate)
    {
        var parts = new List<Doc> { Keyword(evaluate.Keyword) };
        if (evaluate.Expression is not null)
            parts.Add(Doc.Concat(Doc.Hard(), Build(evaluate.Expression)));

        foreach (var clause in evaluate.Clauses)
        {
            parts.Add(Doc.Hard());
            parts.Add(Keywords(clause.Keywords));

            // A single sort key follows ORDER BY on the same line. Several are easier to read one
            // per line, so the keywords keep a line of their own and every key is indented under it.
            // START AT lists boundary values rather than keys, and stays inline.
            var separator = 0;
            var keys = new List<Doc>();
            foreach (var item in clause.Items)
            {
                if (keys.Count > 0)
                {
                    keys.Add(Comma(clause.Separators, ref separator));
                    keys.Add(Doc.Line());
                }
                keys.Add(Build(item));
            }
            var items = Doc.Concat(keys);
            var body = Doc.Concat(Doc.Line(), items);
            var expand = clause.Items.Count > 1 && clause.Keywords[0].IsKeyword("ORDER");
            parts.Add(Doc.Indent(1, expand ? Doc.Concat(body, Doc.BreakParent) : Doc.Group(body)));
        }
        return Doc.Concat(parts);
    }
}
