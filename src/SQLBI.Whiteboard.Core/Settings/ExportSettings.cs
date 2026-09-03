using SQLBI.Whiteboard.Core.Export;

namespace SQLBI.Whiteboard.Core.Settings;

public enum ExportFormat
{
    PowerPoint = 0,
    Pdf = 1,
}

public enum ExportPageSize
{
    A4 = 0,
    Letter = 1,
}

public enum ExportPageModel
{
    /// <summary>
    /// One slide or page per area, as the partitioner cuts them.
    /// </summary>
    OnePerArea = 0,

    /// <summary>
    /// Everything on one slide or page, however large the board is.
    /// </summary>
    WholeBoard = 1,
}

public enum ExportSlideAspect
{
    Wide = 0,
    Standard = 1,
}

public enum ExportPageContent
{
    /// <summary>
    /// One picture of the area per page, exactly as the screen draws it.
    /// </summary>
    Picture = 0,

    /// <summary>
    /// Ink as paths, text as text, images as images: selectable, and sharp
    /// at any zoom.
    /// </summary>
    Vector = 1,
}

public enum ExportSlideContent
{
    /// <summary>
    /// One picture of the area, exactly as the screen draws it.
    /// </summary>
    Picture = 0,

    /// <summary>
    /// Images and text containers as PowerPoint objects, with the ink as one
    /// transparent picture over them.
    /// </summary>
    Editable = 1,
}

/// <summary>
/// What the export dialog remembers between uses. These are not in Preferences:
/// they belong to the dialog that shows their effect.
/// </summary>
public sealed class ExportSettings
{
    public static readonly double[] SmallestTextChoices = [9, 12, 14];

    public ExportFormat Format { get; set; } = ExportFormat.PowerPoint;

    public ExportPageModel PageModel { get; set; } = ExportPageModel.OnePerArea;

    public AreaOrder Order { get; set; } = AreaOrder.Drawing;

    public double GapThreshold { get; set; } = ExportLayoutOptions.DefaultGapThreshold;

    public double SmallestTextPoints { get; set; } = ExportLayoutOptions.DefaultSmallestTextPoints;

    public bool IncludeOverview { get; set; } = true;

    public bool IncludeNotes { get; set; } = true;

    public ExportSlideAspect SlideAspect { get; set; } = ExportSlideAspect.Wide;

    public ExportSlideContent SlideContent { get; set; } = ExportSlideContent.Picture;

    public ExportPageSize PageSize { get; set; } = ExportPageSize.A4;

    public ExportPageContent PageContent { get; set; } = ExportPageContent.Picture;

    /// <summary>
    /// The board name, the date, and "n of m" at the foot of every PDF page.
    /// </summary>
    public bool IncludeFooter { get; set; } = true;

    public ExportLayoutOptions LayoutOptions => ExportLayoutOptions.ForSmallestText(
        SmallestTextPoints,
        GapThreshold,
        Order);

    public static ExportSettings Normalize(ExportSettings? settings)
    {
        var result = settings ?? new ExportSettings();
        if (!Enum.IsDefined(result.Format))
        {
            result.Format = ExportFormat.PowerPoint;
        }

        if (!Enum.IsDefined(result.PageModel))
        {
            result.PageModel = ExportPageModel.OnePerArea;
        }

        if (!Enum.IsDefined(result.Order))
        {
            result.Order = AreaOrder.Drawing;
        }

        if (!Enum.IsDefined(result.SlideAspect))
        {
            result.SlideAspect = ExportSlideAspect.Wide;
        }

        if (!Enum.IsDefined(result.PageSize))
        {
            result.PageSize = ExportPageSize.A4;
        }

        if (!Enum.IsDefined(result.SlideContent))
        {
            result.SlideContent = ExportSlideContent.Picture;
        }

        if (!Enum.IsDefined(result.PageContent))
        {
            result.PageContent = ExportPageContent.Picture;
        }

        result.GapThreshold = double.IsFinite(result.GapThreshold)
            ? Math.Clamp(
                result.GapThreshold,
                ExportLayoutOptions.MinimumGapThreshold,
                ExportLayoutOptions.MaximumGapThreshold)
            : ExportLayoutOptions.DefaultGapThreshold;
        if (!SmallestTextChoices.Contains(result.SmallestTextPoints))
        {
            result.SmallestTextPoints = ExportLayoutOptions.DefaultSmallestTextPoints;
        }

        return result;
    }
}
