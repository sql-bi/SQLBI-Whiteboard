using System.Text.Json;
using System.Text.Json.Serialization;
using SQLBI.Whiteboard.Core.Model;

namespace SQLBI.Whiteboard.Core.Settings;

public enum ToolbarPlacement
{
    /// <summary>
    /// Default for SQLBI recording: the toolbar sits under a typical
    /// presenter picture-in-picture in the top-right corner.
    /// </summary>
    TopRight = 0,
    TopLeft = 1,
    BottomRight = 2,
    BottomLeft = 3,
    BottomCenter = 4,
}

public enum CalligraphyAccess
{
    /// <summary>
    /// A chevron on the Pen button opens Pen vs Calligraphy.
    /// Keeps the ink flyout compact and a stable width.
    /// </summary>
    Chevron = 0,

    /// <summary>
    /// Trailing nib icons on the size row of the ink flyout.
    /// </summary>
    SizeRow = 1,

    /// <summary>
    /// Default: both pen and highlighter colors and sizes stay visible.
    /// </summary>
    DualPalette = 2,
}

public enum StartupMonitorKind
{
    /// <summary>
    /// Current default: a Wacom/Cintiq if one is attached, otherwise
    /// the monitor Windows selected for the window.
    /// </summary>
    WacomIfPresent = 0,
    Primary = 1,
    Named = 2,
}

public sealed class AppSettings
{
    public int Version { get; set; } = AppSettingsSerializer.CurrentVersion;

    public ToolbarPlacement ToolbarPlacement { get; set; } = ToolbarPlacement.TopRight;

    public CalligraphyAccess CalligraphyAccess { get; set; } = CalligraphyAccess.DualPalette;

    public StartupMonitorKind StartupMonitor { get; set; } = StartupMonitorKind.WacomIfPresent;

    public string? StartupMonitorName { get; set; }

    public bool StartFullScreen { get; set; }

    public InkToolSettings Pen { get; set; } = InkToolSettings.From(InkPalettes.DefaultPen);

    public InkToolSettings Highlighter { get; set; } =
        InkToolSettings.From(InkPalettes.DefaultHighlighter);

    public InkToolSettings Calligraphy { get; set; } =
        InkToolSettings.From(InkPalettes.DefaultCalligraphy);

    public LaserSettings Laser { get; set; } = new();
}

public static class AppSettingsSerializer
{
    public const int CurrentVersion = 7;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppSettings Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new AppSettings();
        }

        try
        {
            return Normalize(JsonSerializer.Deserialize<AppSettings>(json, JsonOptions));
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }

    public static string Format(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return JsonSerializer.Serialize(Normalize(settings), JsonOptions);
    }

    public static AppSettings Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new StreamReader(stream, leaveOpen: true);
        return Parse(reader.ReadToEnd());
    }

    public static void Save(Stream stream, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var text = Format(settings);
        using var writer = new StreamWriter(stream, leaveOpen: true);
        writer.Write(text);
        writer.Flush();
    }

    private static AppSettings Normalize(AppSettings? settings)
    {
        if (settings is null)
        {
            return new AppSettings();
        }

        if (!Enum.IsDefined(settings.ToolbarPlacement))
        {
            settings.ToolbarPlacement = ToolbarPlacement.TopRight;
        }

        if (!Enum.IsDefined(settings.CalligraphyAccess))
        {
            settings.CalligraphyAccess = CalligraphyAccess.DualPalette;
        }

        if (!Enum.IsDefined(settings.StartupMonitor))
        {
            settings.StartupMonitor = StartupMonitorKind.WacomIfPresent;
        }

        if (settings.StartupMonitor == StartupMonitorKind.Named)
        {
            if (string.IsNullOrWhiteSpace(settings.StartupMonitorName))
            {
                settings.StartupMonitor = StartupMonitorKind.WacomIfPresent;
                settings.StartupMonitorName = null;
            }
            else
            {
                settings.StartupMonitorName = settings.StartupMonitorName.Trim();
            }
        }
        else
        {
            settings.StartupMonitorName = null;
        }

        settings.Pen = InkPalettes.Normalize(settings.Pen, PenKind.Pen);
        settings.Highlighter = InkPalettes.Normalize(settings.Highlighter, PenKind.Highlighter);
        settings.Calligraphy = InkPalettes.Normalize(settings.Calligraphy, PenKind.Calligraphy);
        settings.Laser = LaserSettings.Normalize(settings.Laser);
        settings.Version = CurrentVersion;
        return settings;
    }
}
