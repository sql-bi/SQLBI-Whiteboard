using SQLBI.Whiteboard.Core.Geometry;
using SQLBI.Whiteboard.Core.Model;

namespace SQLBI.Whiteboard.Core.Export;

public enum AreaOrder
{
    /// <summary>
    /// Areas in the order the author started them. Z-index is creation order,
    /// so the smallest z-index in an area says when it was begun.
    /// </summary>
    Drawing = 0,

    /// <summary>
    /// Top-left to bottom-right, as the cut tree is walked.
    /// </summary>
    Reading = 1,
}

/// <summary>
/// What decides where one area ends and the next begins. Both numbers are in
/// world units, which are screen pixels at zoom 1.
/// </summary>
public sealed record ExportLayoutOptions(
    double GapThreshold = ExportLayoutOptions.DefaultGapThreshold,
    double MaximumAreaWidth = ExportLayoutOptions.DefaultMaximumAreaWidth,
    double MaximumAreaHeight = ExportLayoutOptions.DefaultMaximumAreaHeight,
    AreaOrder Order = AreaOrder.Drawing)
{
    /// <summary>
    /// About two lines of handwriting at zoom 1.
    /// </summary>
    public const double DefaultGapThreshold = 80;
    public const double MinimumGapThreshold = 20;
    public const double MaximumGapThreshold = 400;

    /// <summary>
    /// The body of a text container at its default scale, which is what
    /// "smallest text" is measured against.
    /// </summary>
    public const double TextBodyFontSize = 18;

    /// <summary>
    /// A 16:9 slide is 10 by 5.625 inches: 960 by 540 points.
    /// </summary>
    public const double SlideWidthPoints = 960;
    public const double SlideHeightPoints = 540;

    public const double DefaultSmallestTextPoints = 12;

    /// <summary>
    /// <see cref="MaximumAreaWidthFor"/> at the default smallest text, spelled
    /// out because a default parameter has to be a constant.
    /// </summary>
    public const double DefaultMaximumAreaWidth =
        TextBodyFontSize * SlideWidthPoints / DefaultSmallestTextPoints;

    public const double DefaultMaximumAreaHeight =
        TextBodyFontSize * SlideHeightPoints / DefaultSmallestTextPoints;

    public static ExportLayoutOptions Default { get; } = new();

    /// <summary>
    /// The widest area that still renders body text at the requested size: a
    /// region W world units wide puts 18-unit text at 18 × 960 / W points on a
    /// slide, so 12 points allows 1440 units and 9 points allows 1920.
    /// </summary>
    public static double MaximumAreaWidthFor(double smallestTextPoints) =>
        TextBodyFontSize * SlideWidthPoints / ClampSmallestText(smallestTextPoints);

    public static double MaximumAreaHeightFor(double smallestTextPoints) =>
        TextBodyFontSize * SlideHeightPoints / ClampSmallestText(smallestTextPoints);

    public static ExportLayoutOptions ForSmallestText(
        double smallestTextPoints,
        double gapThreshold = DefaultGapThreshold,
        AreaOrder order = AreaOrder.Drawing) => new(
            Math.Clamp(gapThreshold, MinimumGapThreshold, MaximumGapThreshold),
            MaximumAreaWidthFor(smallestTextPoints),
            MaximumAreaHeightFor(smallestTextPoints),
            order);

    private static double ClampSmallestText(double points) =>
        double.IsFinite(points) && points > 0 ? Math.Clamp(points, 4, 72) : DefaultSmallestTextPoints;
}

/// <summary>
/// One page or slide: the objects it holds and the world rectangle around them.
/// </summary>
public sealed record ExportArea(
    int Number,
    RectD Bounds,
    IReadOnlyList<BoardObject> Objects,
    string? Title,
    double TextScale)
{
    /// <summary>
    /// True when the area had to be scaled below the requested smallest text
    /// because nothing in it could be separated.
    /// </summary>
    public bool IsScaledDown => TextScale < 0.999;

    public int TextScalePercent => (int)Math.Round(TextScale * 100);

    /// <summary>
    /// The text containers in the area, top to bottom then left to right, which
    /// is the order the notes list them in.
    /// </summary>
    public IEnumerable<TextBoardObject> Texts => Objects
        .OfType<TextBoardObject>()
        .OrderBy(text => text.Bounds.Top)
        .ThenBy(text => text.Bounds.Left);
}
