using SQLBI.Whiteboard.Core.Import;

namespace SQLBI.Whiteboard.Core.SmokeTests;

/// <summary>
/// Font answers the tests can predict: a few families exist, Aptos kerns ordinary text
/// by one percent and a run of AV pairs by far more, and nothing else can be measured.
/// </summary>
internal sealed class FixedFontMetrics : ISvgFontMetrics
{
    private static readonly HashSet<string> Families = new(StringComparer.OrdinalIgnoreCase)
    {
        "Aptos",
        "Segoe Sans Small",
        "Segoe UI Semibold",
    };

    public bool IsAvailable(string family) => Families.Contains(family);

    public double? KernedOverPlain(SvgTextRun run) =>
        run.Families[0] != "Aptos" ? null
        : run.Text.StartsWith("AV", StringComparison.Ordinal) ? 0.8
        : 0.99;
}
