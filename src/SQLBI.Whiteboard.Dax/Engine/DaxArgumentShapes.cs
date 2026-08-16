namespace SQLBI.Whiteboard.Dax.Engine;

/// <summary>One printed unit of a function call: the arguments that belong on the same line.</summary>
/// <param name="Arguments">Indexes into the call's argument list.</param>
/// <param name="ExtraIndent">Additional indentation levels, used by SWITCH for its result values.</param>
internal sealed record DaxArgumentGroup(IReadOnlyList<int> Arguments, int ExtraIndent = 0);

/// <summary>
/// Knows which DAX functions take their arguments in pairs, so that "Name", value stays together
/// on one line when the call is expanded. This is the only per-function knowledge the formatter
/// needs: every other call is laid out generically, including functions that do not exist yet.
/// </summary>
internal static class DaxArgumentShapes
{
    /// <summary>
    /// Functions whose trailing arguments are "Name", expression pairs. The pairs begin at the
    /// first argument that is a plain string literal, which is where the name list starts in every
    /// one of these signatures, no matter how many grouping columns precede it.
    /// </summary>
    private static readonly HashSet<string> NameValueFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "ADDCOLUMNS", "SELECTCOLUMNS", "SUMMARIZE", "SUMMARIZECOLUMNS", "GROUPBY", "ROW"
    };

    /// <summary>Functions that pair their arguments after a fixed number of leading ones.</summary>
    private static readonly Dictionary<string, int> PairsAfterLeadingArguments = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ORDERBY"] = 0,
        ["CONTAINS"] = 1,
        ["LOOKUPVALUE"] = 1,
        ["LOOKUP"] = 1,
        ["TOPN"] = 2,
        ["SAMPLE"] = 2
    };

    /// <summary>DATATABLE pairs its column name and type, then takes the row block on its own.</summary>
    private const string DataTable = "DATATABLE";

    /// <summary>SWITCH is always expanded, with each result indented under its condition.</summary>
    public static bool AlwaysExpands(string functionName) =>
        functionName.Equals("SWITCH", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<DaxArgumentGroup> Group(string functionName, IReadOnlyList<DaxNode> arguments)
    {
        if (arguments.Count == 0) return [];

        if (AlwaysExpands(functionName))
            return SwitchGroups(arguments.Count);

        // DATATABLE pairs "Name", TYPE up to the final row block, which stands on its own.
        var pairEnd = functionName.Equals(DataTable, StringComparison.OrdinalIgnoreCase)
            ? arguments.Count - 1
            : arguments.Count;

        var pairStart = FindPairStart(functionName, arguments);
        if (pairStart < 0) return Singles(0, arguments.Count);

        var groups = Singles(0, pairStart);
        for (var index = pairStart; index < pairEnd; index += 2)
        {
            groups.Add(index + 1 < pairEnd
                ? new DaxArgumentGroup([index, index + 1])
                : new DaxArgumentGroup([index]));
        }
        groups.AddRange(Singles(pairEnd, arguments.Count));
        return groups;
    }

    private static List<DaxArgumentGroup> Singles(int start, int end)
    {
        var groups = new List<DaxArgumentGroup>(Math.Max(0, end - start));
        for (var index = start; index < end; index++) groups.Add(new DaxArgumentGroup([index]));
        return groups;
    }

    /// <summary>
    /// SWITCH ( expression, condition, result, condition, result, else ): the expression and the
    /// conditions sit at argument level, and each result is indented one level further, which makes
    /// the condition/result structure readable at a glance.
    /// </summary>
    private static IReadOnlyList<DaxArgumentGroup> SwitchGroups(int count)
    {
        var groups = new List<DaxArgumentGroup>(count);
        for (var index = 0; index < count; index++)
        {
            var isResult = index >= 2 && index % 2 == 0;
            groups.Add(new DaxArgumentGroup([index], isResult ? 1 : 0));
        }
        return groups;
    }

    /// <summary>Returns the argument index where pairing begins, or -1 when the call does not pair.</summary>
    private static int FindPairStart(string functionName, IReadOnlyList<DaxNode> arguments)
    {
        if (functionName.Equals(DataTable, StringComparison.OrdinalIgnoreCase))
            return arguments.Count > 2 ? 0 : -1;

        if (PairsAfterLeadingArguments.TryGetValue(functionName, out var leading))
            return leading < arguments.Count ? leading : -1;

        if (!NameValueFunctions.Contains(functionName)) return -1;

        for (var index = 0; index < arguments.Count; index++)
        {
            if (IsStringLiteral(arguments[index]))
                return index;
        }
        return -1;
    }

    private static bool IsStringLiteral(DaxNode node) =>
        node is DaxLeaf { Token.Kind: DaxTokenKind.String };
}
