namespace SQLBI.Whiteboard.Core.Settings;

public enum SlidePictures
{
    /// <summary>
    /// SVG for each slide, and PNG for a slide whose SVG could not be captured or
    /// names a font Whiteboard cannot find.
    /// </summary>
    Auto = 0,

    Svg = 1,

    Png = 2,
}

public enum SlideArrangement
{
    /// <summary>
    /// One row per section, which is one row for a deck without sections.
    /// </summary>
    RowPerSection = 0,

    OneRow = 1,

    OneColumn = 2,
}

/// <summary>
/// What the PowerPoint import dialog remembers between uses. Like the export
/// settings, these are not in Preferences, because they are set in the dialog.
/// </summary>
public sealed class PowerPointImportSettings
{
    public static readonly int[] PngWidthChoices = [1920, 2560, 3840];

    public const int DefaultPngWidth = 2560;

    public SlidePictures Pictures { get; set; } = SlidePictures.Auto;

    /// <summary>
    /// Pixels across a slide exported as PNG. SVG does not use it.
    /// </summary>
    public int PngWidth { get; set; } = DefaultPngWidth;

    public SlideArrangement Arrangement { get; set; } = SlideArrangement.RowPerSection;

    public bool Frames { get; set; }

    public bool IncludeHidden { get; set; }

    public static PowerPointImportSettings Normalize(PowerPointImportSettings? settings)
    {
        var result = settings ?? new PowerPointImportSettings();
        if (!Enum.IsDefined(result.Pictures))
        {
            result.Pictures = SlidePictures.Auto;
        }

        if (!Enum.IsDefined(result.Arrangement))
        {
            result.Arrangement = SlideArrangement.RowPerSection;
        }

        if (!PngWidthChoices.Contains(result.PngWidth))
        {
            result.PngWidth = DefaultPngWidth;
        }

        return result;
    }
}
