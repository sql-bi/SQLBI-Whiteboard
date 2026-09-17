namespace SQLBI.Whiteboard.Core.Settings;

/// <summary>
/// What the next label is written with. It is last-used rather than configured,
/// like the pen's color and size, so it is not offered in Preferences.
/// </summary>
public sealed class LabelSettings
{
    public string FontFamily { get; set; } = LabelStyles.DefaultFontFamily;
    public double FontSize { get; set; } = LabelStyles.DefaultFontSize;
    public uint Argb { get; set; } = LabelStyles.DefaultArgb;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
}

/// <summary>
/// The fonts and sizes a label can be written in. The list is curated rather
/// than taken from the machine: a board is opened on other machines, and a
/// font nobody else has would come back as something else there anyway.
/// </summary>
public static class LabelStyles
{
    public const string DefaultFontFamily = "Segoe UI";
    public const double DefaultFontSize = 24;

    /// <summary>
    /// The near-black the pen writes in, so a label reads as part of the same
    /// hand as the ink around it.
    /// </summary>
    public const uint DefaultArgb = 0xFF1F2937;

    /// <summary>
    /// Segoe UI first, because it is the fallback as well as the default.
    /// Consolas and Cascadia Mono are the monospace entries.
    /// </summary>
    public static IReadOnlyList<string> Fonts { get; } =
    [
        "Segoe UI",
        "Calibri",
        "Arial",
        "Georgia",
        "Times New Roman",
        "Consolas",
        "Cascadia Mono",
        "Comic Sans MS",
        "Segoe Print",
    ];

    /// <summary>
    /// World pixels rather than points: a label is the size it is on the board,
    /// and the zoom decides how big that looks.
    /// </summary>
    public static IReadOnlyList<double> FontSizes { get; } = [12, 16, 20, 24, 32, 40, 48, 64, 96];

    public static double MinimumFontSize => FontSizes[0];

    public static double MaximumFontSize => FontSizes[^1];

    public static string NormalizeFont(string? fontFamily)
    {
        var name = fontFamily?.Trim();
        return Fonts.FirstOrDefault(font =>
            string.Equals(font, name, StringComparison.OrdinalIgnoreCase)) ?? DefaultFontFamily;
    }

    /// <summary>
    /// A size from a file, kept where a label can be read: the corner handle
    /// scales a label freely, so this is a range rather than the list itself.
    /// </summary>
    public static double ClampFontSize(double fontSize) =>
        double.IsFinite(fontSize) && fontSize > 0
            ? Math.Clamp(fontSize, MinimumFontSize, MaximumFontSize)
            : DefaultFontSize;

    /// <summary>
    /// A size for the settings, which remember what was chosen from the list.
    /// </summary>
    public static double NormalizeFontSize(double fontSize) =>
        FontSizes.Contains(fontSize) ? fontSize : DefaultFontSize;

    public static LabelSettings Normalize(LabelSettings? settings)
    {
        if (settings is null)
        {
            return new LabelSettings();
        }

        return new LabelSettings
        {
            FontFamily = NormalizeFont(settings.FontFamily),
            FontSize = NormalizeFontSize(settings.FontSize),
            Argb = InkPalettes.Pen.Any(swatch => swatch.Argb == settings.Argb)
                ? settings.Argb
                : DefaultArgb,
            Bold = settings.Bold,
            Italic = settings.Italic,
            Underline = settings.Underline,
        };
    }
}
