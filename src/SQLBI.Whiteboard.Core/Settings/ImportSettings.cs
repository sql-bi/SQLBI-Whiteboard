using SQLBI.Whiteboard.Core.Import;

namespace SQLBI.Whiteboard.Core.Settings;

public sealed class ImportSettings
{
    public const double DefaultSpacing = ImportLayout.Gap;
    public const double MinimumSpacing = 0;
    public const double MaximumSpacing = 500;

    // Board pixels at 100% zoom, independent of the view used to import the file.
    public double HorizontalSpacing { get; set; } = DefaultSpacing;

    public double VerticalSpacing { get; set; } = DefaultSpacing;

    public static ImportSettings Normalize(ImportSettings? settings)
    {
        var result = settings ?? new ImportSettings();
        result.HorizontalSpacing = Clamp(result.HorizontalSpacing);
        result.VerticalSpacing = Clamp(result.VerticalSpacing);
        return result;
    }

    private static double Clamp(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, MinimumSpacing, MaximumSpacing) : DefaultSpacing;
}
