using System.IO;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SQLBI.Whiteboard.SqlServer;

public enum SqlServerTextClassification
{
    Text,
    Keyword,
    Function,
    StringLiteral,
    Number,
    Comment,
    Identifier,
    QuotedIdentifier,
    Variable,
    TableName,
    ColumnName,
    Alias,
    Parameter,
    DataType,
    Parenthesis,
    Operator,
    Punctuation,
    DefinitionName,
}

public readonly record struct SqlServerClassifiedSpan(
    int Start,
    int Length,
    SqlServerTextClassification Classification);

public readonly record struct SqlServerParseDiagnostic(
    int Offset,
    int Line,
    int Column,
    string Message);

public sealed record SqlServerTextAnalysis(
    IReadOnlyList<SqlServerClassifiedSpan> Spans,
    string? DefinedObjectName,
    IReadOnlyList<SqlServerParseDiagnostic> Diagnostics);

public static class SqlServerLanguageEngine
{
    public static SqlServerTextAnalysis Analyze(string source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return new SqlServerTextAnalysis([], null, []);
        }

        try
        {
            var parser = CreateParser();
            IList<ParseError> tokenErrors;
            IList<TSqlParserToken> tokens;
            using (var reader = new StringReader(source))
            {
                tokens = parser.GetTokenStream(reader, out tokenErrors);
            }

            TSqlFragment fragment = parser.Parse(tokens, out IList<ParseError> parseErrors);
            var roles = new Dictionary<int, SqlServerTextClassification>();
            var visitor = new ClassificationVisitor(tokens, roles);
            fragment.Accept(visitor);

            var spans = new List<SqlServerClassifiedSpan>(tokens.Count);
            for (int index = 0; index < tokens.Count; index++)
            {
                TSqlParserToken token = tokens[index];
                SqlServerTextClassification? classification = roles.TryGetValue(
                    index,
                    out SqlServerTextClassification role)
                    ? role
                    : ClassifyToken(token);
                int length = Math.Min(token.Text?.Length ?? 0, source.Length - token.Offset);
                if (classification is not null && token.Offset >= 0 && length > 0)
                {
                    spans.Add(new SqlServerClassifiedSpan(
                        token.Offset,
                        length,
                        classification.Value));
                }
            }

            SqlServerParseDiagnostic[] diagnostics = tokenErrors
                .Concat(parseErrors)
                .DistinctBy(error => (error.Offset, error.Number, error.Message))
                .Select(error => new SqlServerParseDiagnostic(
                    error.Offset,
                    error.Line,
                    error.Column,
                    error.Message))
                .ToArray();
            return new SqlServerTextAnalysis(
                spans,
                visitor.DefinedObjectName,
                diagnostics);
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            return new SqlServerTextAnalysis(
                [],
                null,
                [new SqlServerParseDiagnostic(0, 1, 1, exception.Message)]);
        }
    }

    public static IReadOnlyList<SqlServerClassifiedSpan> Classify(string source) =>
        Analyze(source).Spans;

    public static string? DefinedObjectName(string source) =>
        Analyze(source).DefinedObjectName;

    public static bool TryFormat(string source, out string formatted)
    {
        formatted = source;
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        try
        {
            var parser = CreateParser();
            if (!TryGenerateWithBatchSeparators(source, parser, out string generated))
            {
                return false;
            }

            if (generated.Length == 0 || !HasSameTokenSignature(source, generated))
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

    private static TSql170Parser CreateParser() =>
        new(initialQuotedIdentifiers: true, SqlEngineType.Standalone);

    private static SqlScriptGeneratorOptions CreateGeneratorOptions() => new()
    {
        SqlVersion = SqlVersion.Sql170,
        SqlEngineType = SqlEngineType.Standalone,
        KeywordCasing = KeywordCasing.Uppercase,
        IdentifierCasing = IdentifierCasing.Preserve,
        IdentifierBracketing = IdentifierBracketing.Preserve,
        IndentationMode = IndentationMode.Spaces,
        IndentationSize = 4,
        IncludeSemicolons = false,
        PreserveComments = true,
        NumNewlinesAfterStatement = 1,
        AsKeywordOnOwnLine = false,
        NewLineBeforeFromClause = true,
        NewLineBeforeWhereClause = true,
        NewLineBeforeGroupByClause = true,
        NewLineBeforeHavingClause = true,
        NewLineBeforeOrderByClause = true,
        NewLineBeforeJoinClause = true,
        NewLineBeforeWindowClause = true,
        MultilineSelectElementsList = true,
        MultilineInsertSourcesList = true,
        MultilineInsertTargetsList = true,
        MultilineSetClauseItems = true,
        MultilineWherePredicatesList = false,
    };

    private static bool TryGenerateWithBatchSeparators(
        string source,
        TSql170Parser parser,
        out string generated)
    {
        generated = string.Empty;
        IList<ParseError> tokenErrors;
        IList<TSqlParserToken> tokens;
        using (var reader = new StringReader(source))
        {
            tokens = parser.GetTokenStream(reader, out tokenErrors);
        }

        if (tokenErrors.Count > 0)
        {
            return false;
        }

        TSqlParserToken[] separators = tokens
            .Where(token => token.TokenType == TSqlTokenType.Go)
            .ToArray();
        if (separators.Length == 0)
        {
            return TryGenerateBatch(source, parser, out generated);
        }

        var result = new List<string>();
        int segmentStart = 0;
        foreach (TSqlParserToken separator in separators)
        {
            string batch = source[segmentStart..separator.Offset];
            if (!TryGenerateBatch(batch, parser, out string formattedBatch))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(formattedBatch))
            {
                result.Add(formattedBatch.TrimEnd());
            }

            int lineEnd = separator.Offset;
            while (lineEnd < source.Length && source[lineEnd] is not '\r' and not '\n')
            {
                lineEnd++;
            }

            result.Add(source[separator.Offset..lineEnd].TrimEnd());
            segmentStart = lineEnd;
            if (segmentStart < source.Length && source[segmentStart] == '\r')
            {
                segmentStart++;
            }

            if (segmentStart < source.Length && source[segmentStart] == '\n')
            {
                segmentStart++;
            }
        }

        string finalBatch = source[segmentStart..];
        if (!TryGenerateBatch(finalBatch, parser, out string formattedFinalBatch))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(formattedFinalBatch))
        {
            result.Add(formattedFinalBatch.TrimEnd());
        }

        generated = string.Join(Environment.NewLine, result).TrimEnd();
        return true;
    }

    private static bool TryGenerateBatch(
        string source,
        TSql170Parser parser,
        out string generated)
    {
        generated = string.Empty;
        if (string.IsNullOrWhiteSpace(source))
        {
            return true;
        }

        TSqlFragment fragment;
        IList<ParseError> errors;
        using (var reader = new StringReader(source))
        {
            fragment = parser.Parse(reader, out errors);
        }

        if (errors.Count > 0)
        {
            return false;
        }

        var generator = new Sql170ScriptGenerator(CreateGeneratorOptions());
        generator.GenerateScript(fragment, out generated);
        generated = generated.TrimEnd();
        return true;
    }

    private static bool HasSameTokenSignature(string before, string after) =>
        TokenSignature(before).SequenceEqual(TokenSignature(after));

    private static IReadOnlyList<TokenSignaturePart> TokenSignature(string source)
    {
        var parser = CreateParser();
        IList<ParseError> errors;
        IList<TSqlParserToken> tokens;
        using (var reader = new StringReader(source))
        {
            tokens = parser.GetTokenStream(reader, out errors);
        }

        if (errors.Count > 0)
        {
            return [];
        }

        return tokens
            .Where(token => token.TokenType is not TSqlTokenType.WhiteSpace and
                not TSqlTokenType.EndOfFile and
                not TSqlTokenType.Semicolon)
            .Select(token => new TokenSignaturePart(
                token.TokenType,
                NormalizeSignatureText(token)))
            .ToArray();
    }

    private static string NormalizeSignatureText(TSqlParserToken token)
    {
        if (ClassifyToken(token) == SqlServerTextClassification.Keyword)
        {
            return token.TokenType.ToString();
        }

        string text = token.Text ?? string.Empty;
        return token.TokenType == TSqlTokenType.Identifier
            ? text.ToUpperInvariant()
            : text;
    }

    private static SqlServerTextClassification? ClassifyToken(TSqlParserToken token) =>
        token.TokenType switch
        {
            TSqlTokenType.None or TSqlTokenType.EndOfFile or TSqlTokenType.WhiteSpace => null,
            TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment =>
                SqlServerTextClassification.Comment,
            TSqlTokenType.AsciiStringLiteral or TSqlTokenType.UnicodeStringLiteral =>
                SqlServerTextClassification.StringLiteral,
            TSqlTokenType.AsciiStringOrQuotedIdentifier =>
                token.Text?.StartsWith('"') == true
                    ? SqlServerTextClassification.QuotedIdentifier
                    : SqlServerTextClassification.StringLiteral,
            TSqlTokenType.Integer or TSqlTokenType.Numeric or TSqlTokenType.Real or
                TSqlTokenType.HexLiteral or TSqlTokenType.Money =>
                SqlServerTextClassification.Number,
            TSqlTokenType.Identifier => SqlServerTextClassification.Identifier,
            TSqlTokenType.QuotedIdentifier => SqlServerTextClassification.QuotedIdentifier,
            TSqlTokenType.Variable or TSqlTokenType.SqlCommandIdentifier =>
                SqlServerTextClassification.Variable,
            TSqlTokenType.LeftParenthesis or TSqlTokenType.RightParenthesis or
                TSqlTokenType.LeftCurly or TSqlTokenType.RightCurly =>
                SqlServerTextClassification.Parenthesis,
            TSqlTokenType.Bang or TSqlTokenType.PercentSign or TSqlTokenType.Ampersand or
                TSqlTokenType.Star or TSqlTokenType.MultiplyEquals or TSqlTokenType.Plus or
                TSqlTokenType.Minus or TSqlTokenType.Divide or TSqlTokenType.LessThan or
                TSqlTokenType.EqualsSign or TSqlTokenType.RightOuterJoin or
                TSqlTokenType.GreaterThan or TSqlTokenType.Circumflex or
                TSqlTokenType.VerticalLine or TSqlTokenType.Tilde or
                TSqlTokenType.AddEquals or TSqlTokenType.SubtractEquals or
                TSqlTokenType.DivideEquals or TSqlTokenType.ModEquals or
                TSqlTokenType.BitwiseAndEquals or TSqlTokenType.BitwiseOrEquals or
                TSqlTokenType.BitwiseXorEquals or TSqlTokenType.LeftShift or
                TSqlTokenType.RightShift or TSqlTokenType.Concat or
                TSqlTokenType.ConcatEquals => SqlServerTextClassification.Operator,
            TSqlTokenType.Comma or TSqlTokenType.Dot or TSqlTokenType.Colon or
                TSqlTokenType.DoubleColon or TSqlTokenType.Semicolon =>
                SqlServerTextClassification.Punctuation,
            _ => SqlServerTextClassification.Keyword,
        };

    private static bool IsRecoverable(Exception exception) =>
        exception is ArgumentException or FormatException or InvalidOperationException or
            IndexOutOfRangeException;

    private readonly record struct TokenSignaturePart(TSqlTokenType Type, string Text);

    private sealed class ClassificationVisitor(
        IList<TSqlParserToken> tokens,
        IDictionary<int, SqlServerTextClassification> roles) : TSqlFragmentVisitor
    {
        public string? DefinedObjectName { get; private set; }

        public override void ExplicitVisit(NamedTableReference node)
        {
            Mark(node.SchemaObject, SqlServerTextClassification.TableName);
            Mark(node.Alias, SqlServerTextClassification.Alias);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(SchemaObjectFunctionTableReference node)
        {
            Mark(node.SchemaObject, SqlServerTextClassification.Function);
            Mark(node.Alias, SqlServerTextClassification.Alias);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(ColumnReferenceExpression node)
        {
            Mark(
                node.MultiPartIdentifier?.Identifiers.LastOrDefault(),
                SqlServerTextClassification.ColumnName);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(FunctionCall node)
        {
            Mark(node.FunctionName, SqlServerTextClassification.Function);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(SqlDataTypeReference node)
        {
            Mark(node.Name, SqlServerTextClassification.DataType);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(UserDataTypeReference node)
        {
            Mark(node.Name, SqlServerTextClassification.DataType);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(ProcedureParameter node)
        {
            Mark(node.VariableName, SqlServerTextClassification.Parameter);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(DeclareVariableElement node)
        {
            Mark(node.VariableName, SqlServerTextClassification.Variable);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(SelectScalarExpression node)
        {
            Mark(node.ColumnName?.Identifier, SqlServerTextClassification.Alias);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(CreateProcedureStatement node)
        {
            base.ExplicitVisit(node);
            CaptureDefinition(node.ProcedureReference?.Name);
        }

        public override void ExplicitVisit(AlterProcedureStatement node)
        {
            base.ExplicitVisit(node);
            CaptureDefinition(node.ProcedureReference?.Name);
        }

        public override void ExplicitVisit(CreateOrAlterProcedureStatement node)
        {
            base.ExplicitVisit(node);
            CaptureDefinition(node.ProcedureReference?.Name);
        }

        public override void ExplicitVisit(CreateViewStatement node)
        {
            base.ExplicitVisit(node);
            CaptureDefinition(node.SchemaObjectName);
        }

        public override void ExplicitVisit(AlterViewStatement node)
        {
            base.ExplicitVisit(node);
            CaptureDefinition(node.SchemaObjectName);
        }

        public override void ExplicitVisit(CreateOrAlterViewStatement node)
        {
            base.ExplicitVisit(node);
            CaptureDefinition(node.SchemaObjectName);
        }

        public override void ExplicitVisit(CreateFunctionStatement node)
        {
            base.ExplicitVisit(node);
            CaptureDefinition(node.Name);
        }

        public override void ExplicitVisit(AlterFunctionStatement node)
        {
            base.ExplicitVisit(node);
            CaptureDefinition(node.Name);
        }

        public override void ExplicitVisit(CreateOrAlterFunctionStatement node)
        {
            base.ExplicitVisit(node);
            CaptureDefinition(node.Name);
        }

        public override void ExplicitVisit(CreateTableStatement node)
        {
            base.ExplicitVisit(node);
            CaptureDefinition(node.SchemaObjectName);
        }

        public override void ExplicitVisit(AlterTableStatement node)
        {
            base.ExplicitVisit(node);
            CaptureDefinition(node.SchemaObjectName);
        }

        public override void ExplicitVisit(CreateTriggerStatement node)
        {
            base.ExplicitVisit(node);
            CaptureDefinition(node.Name);
        }

        public override void ExplicitVisit(AlterTriggerStatement node)
        {
            base.ExplicitVisit(node);
            CaptureDefinition(node.Name);
        }

        public override void ExplicitVisit(CreateOrAlterTriggerStatement node)
        {
            base.ExplicitVisit(node);
            CaptureDefinition(node.Name);
        }

        public override void ExplicitVisit(CreateSequenceStatement node)
        {
            base.ExplicitVisit(node);
            CaptureDefinition(node.Name);
        }

        public override void ExplicitVisit(CreateSchemaStatement node)
        {
            base.ExplicitVisit(node);
            CaptureDefinition(node.Name);
        }

        public override void ExplicitVisit(CreateTypeTableStatement node)
        {
            base.ExplicitVisit(node);
            CaptureDefinition(node.Name);
        }

        public override void ExplicitVisit(CreateTypeUddtStatement node)
        {
            base.ExplicitVisit(node);
            CaptureDefinition(node.Name);
        }

        public override void ExplicitVisit(CreateSynonymStatement node)
        {
            base.ExplicitVisit(node);
            CaptureDefinition(node.Name);
        }

        private void CaptureDefinition(SchemaObjectName? name)
        {
            if (name is null)
            {
                return;
            }

            DefinedObjectName ??= JoinName(name.Identifiers);
            Mark(name, SqlServerTextClassification.DefinitionName);
        }

        private void CaptureDefinition(Identifier? name)
        {
            if (name is null)
            {
                return;
            }

            DefinedObjectName ??= name.Value;
            Mark(name, SqlServerTextClassification.DefinitionName);
        }

        private void Mark(TSqlFragment? fragment, SqlServerTextClassification classification)
        {
            if (fragment is null || fragment.FirstTokenIndex < 0 || fragment.LastTokenIndex < 0)
            {
                return;
            }

            int last = Math.Min(fragment.LastTokenIndex, tokens.Count - 1);
            for (int index = Math.Max(0, fragment.FirstTokenIndex); index <= last; index++)
            {
                if (tokens[index].TokenType is TSqlTokenType.Identifier or
                    TSqlTokenType.QuotedIdentifier or
                    TSqlTokenType.AsciiStringOrQuotedIdentifier or
                    TSqlTokenType.Variable)
                {
                    roles[index] = classification;
                }
            }
        }

        private static string JoinName(IEnumerable<Identifier> identifiers) =>
            string.Join('.', identifiers.Select(identifier => identifier.Value));
    }
}
