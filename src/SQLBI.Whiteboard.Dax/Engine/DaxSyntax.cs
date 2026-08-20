namespace SQLBI.Whiteboard.Dax.Engine;

/// <summary>
/// Nodes carry only the structure the printer needs. An in-order walk of any tree emits exactly
/// the tokens that were read, which is what keeps formatting from ever altering the code.
/// </summary>
internal abstract class DaxNode;

/// <summary>A single token: a literal, a bare identifier, or a keyword the parser did not consume.</summary>
internal sealed class DaxLeaf(DaxToken token) : DaxNode
{
    public DaxToken Token { get; } = token;
}

/// <summary>A reference printed without internal spaces: 'Sales'[Amount], Sales[Amount], [Measure].</summary>
internal sealed class DaxReference(IReadOnlyList<DaxToken> tokens) : DaxNode
{
    public IReadOnlyList<DaxToken> Tokens { get; } = tokens;
}

/// <summary>A function call: NAME ( arguments ).</summary>
internal sealed class DaxCall(
    DaxNode callee,
    string functionName,
    DaxToken openParenthesis,
    IReadOnlyList<DaxNode> arguments,
    DaxToken? closeParenthesis,
    IReadOnlyList<DaxToken> separators) : DaxNode
{
    /// <summary>Usually a function name, but DAX also allows [Measure] ( filters ).</summary>
    public DaxNode Callee { get; } = callee;

    /// <summary>The function name used to look up an argument shape; empty when there is none.</summary>
    public string FunctionName { get; } = functionName;

    public DaxToken OpenParenthesis { get; } = openParenthesis;
    public IReadOnlyList<DaxNode> Arguments { get; } = arguments;

    /// <summary>Kept so that comments written just before the closing bracket survive formatting.</summary>
    public DaxToken? CloseParenthesis { get; } = closeParenthesis;

    /// <summary>The commas, kept because a comment can follow one on the same line.</summary>
    public IReadOnlyList<DaxToken> Separators { get; } = separators;
}

/// <summary>A parenthesized subexpression, a tuple ( a, b ), or a table constructor { a, b }.</summary>
internal sealed class DaxBracketed(
    DaxToken open,
    IReadOnlyList<DaxNode> items,
    DaxToken? close,
    IReadOnlyList<DaxToken> separators) : DaxNode
{
    public DaxToken Open { get; } = open;
    public IReadOnlyList<DaxNode> Items { get; } = items;
    public DaxToken? Close { get; } = close;
    public IReadOnlyList<DaxToken> Separators { get; } = separators;
    public bool IsBrace => Open.Kind == DaxTokenKind.OpenBrace;
}

internal sealed class DaxBinary(DaxNode left, DaxToken op, DaxNode right, int precedence) : DaxNode
{
    public DaxNode Left { get; } = left;
    public DaxToken Operator { get; } = op;
    public DaxNode Right { get; } = right;
    public int Precedence { get; } = precedence;
}

internal sealed class DaxUnary(DaxToken op, DaxNode operand) : DaxNode
{
    public DaxToken Operator { get; } = op;
    public DaxNode Operand { get; } = operand;

    /// <summary>NOT needs a separating space; the symbolic operators bind directly to the operand.</summary>
    public bool NeedsSpace => Operator.Kind == DaxTokenKind.Identifier;
}

/// <summary>An expression followed by a trailing word, such as an ORDER BY member with ASC or DESC.</summary>
internal sealed class DaxSuffixed(DaxNode expression, DaxToken suffix) : DaxNode
{
    public DaxNode Expression { get; } = expression;
    public DaxToken Suffix { get; } = suffix;
}

internal sealed record DaxVariable(DaxToken Keyword, DaxToken Name, DaxToken Assignment, DaxNode Value);

/// <summary>A VAR ... RETURN block used as an expression.</summary>
internal sealed class DaxVarReturn(IReadOnlyList<DaxVariable> variables, DaxToken? returnKeyword, DaxNode? body) : DaxNode
{
    public IReadOnlyList<DaxVariable> Variables { get; } = variables;
    public DaxToken? ReturnKeyword { get; } = returnKeyword;
    public DaxNode? Body { get; } = body;
}

/// <summary>
/// A named definition: "Sales Amount := expr", "MEASURE Sales[M] = expr", "VAR x = expr",
/// "COLUMN/TABLE/MPARAMETER name = expr".
/// </summary>
internal sealed class DaxDefinition(
    IReadOnlyList<DaxToken> nameTokens,
    DaxToken assignment,
    DaxNode? value,
    bool startsWithKeyword) : DaxNode
{
    public IReadOnlyList<DaxToken> NameTokens { get; } = nameTokens;
    public DaxToken Assignment { get; } = assignment;
    public DaxNode? Value { get; } = value;

    /// <summary>True when the first name token is MEASURE, COLUMN, TABLE, VAR or MPARAMETER.</summary>
    public bool StartsWithKeyword { get; } = startsWithKeyword;
}

/// <summary>FUNCTION name = ( parameters ) =&gt; body.</summary>
internal sealed class DaxFunctionDefinition(
    IReadOnlyList<DaxToken> nameTokens,
    DaxToken assignment,
    IReadOnlyList<DaxNode> parameters,
    IReadOnlyList<DaxToken> separators,
    DaxToken? arrow,
    DaxNode? body) : DaxNode
{
    public IReadOnlyList<DaxToken> NameTokens { get; } = nameTokens;
    public DaxToken Assignment { get; } = assignment;
    public IReadOnlyList<DaxNode> Parameters { get; } = parameters;
    public IReadOnlyList<DaxToken> Separators { get; } = separators;
    public DaxToken? Arrow { get; } = arrow;
    public DaxNode? Body { get; } = body;
}

/// <summary>A typed function parameter: name, or name : Type Subtype EvalKind.</summary>
internal sealed class DaxParameter(DaxToken name, DaxToken? colon, IReadOnlyList<DaxToken> annotations) : DaxNode
{
    public DaxToken Name { get; } = name;
    public DaxToken? Colon { get; } = colon;
    public IReadOnlyList<DaxToken> Annotations { get; } = annotations;
}

/// <summary>DEFINE followed by its definitions.</summary>
internal sealed class DaxDefine(DaxToken keyword, IReadOnlyList<DaxNode> definitions) : DaxNode
{
    public DaxToken Keyword { get; } = keyword;
    public IReadOnlyList<DaxNode> Definitions { get; } = definitions;
}

/// <summary>A trailing clause of EVALUATE, such as ORDER BY or START AT.</summary>
internal sealed record DaxClause(
    IReadOnlyList<DaxToken> Keywords,
    IReadOnlyList<DaxNode> Items,
    IReadOnlyList<DaxToken> Separators);

internal sealed class DaxEvaluate(DaxToken keyword, DaxNode? expression, IReadOnlyList<DaxClause> clauses) : DaxNode
{
    public DaxToken Keyword { get; } = keyword;
    public DaxNode? Expression { get; } = expression;
    public IReadOnlyList<DaxClause> Clauses { get; } = clauses;
}

/// <summary>A top-level statement together with the optional semicolon that closed it.</summary>
internal sealed class DaxStatement(DaxNode body, DaxToken? terminator) : DaxNode
{
    public DaxNode Body { get; } = body;
    public DaxToken? Terminator { get; } = terminator;
}

/// <summary>The whole document.</summary>
internal sealed class DaxScript(IReadOnlyList<DaxNode> statements, DaxToken endOfFile) : DaxNode
{
    public IReadOnlyList<DaxNode> Statements { get; } = statements;

    /// <summary>Carries any comments that trailed the last token so they are not dropped.</summary>
    public DaxToken EndOfFile { get; } = endOfFile;
}
