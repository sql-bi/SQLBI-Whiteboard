using System.Text.Json;
using System.Text.Json.Serialization;
using SQLBI.Whiteboard.Core.Model;
using SQLBI.Whiteboard.Core.Updates;

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

public enum FingerMode
{
    /// <summary>
    /// One finger pans, two fingers pinch-zoom. Ink stays on the pen.
    /// </summary>
    Off = 0,

    /// <summary>
    /// One finger uses the current tool. Two fingers still pan and pinch-zoom.
    /// </summary>
    On = 1,

    /// <summary>
    /// Default for a new setup: treat as On only when Windows reports no
    /// stylus digitizer. A Surface often reports one even when the pen is
    /// elsewhere.
    /// </summary>
    WhenNoPen = 2,
}

/// <summary>
/// Whether the mouse draws. It is a setting rather than pure detection because
/// the digitizer list is unreliable in both directions, and because a mouse
/// gets the tools, not the gestures: turning this on changes what the left
/// button means, and that is the kind of change a person should be able to
/// refuse.
/// </summary>
public enum MouseMode
{
    /// <summary>
    /// The left button borrows Select for one gesture and hands the tool back.
    /// The mouse pans, zooms, and moves containers, and does not draw.
    /// </summary>
    Off = 0,

    /// <summary>
    /// The left button does what the active tool does. The pen path is
    /// untouched, so this is safe to turn on beside a pen.
    /// </summary>
    On = 1,

    /// <summary>
    /// Default for a new setup: treat as On only when Windows reports neither a
    /// stylus nor a touchscreen. Deliberately stricter than
    /// <see cref="FingerMode.WhenNoPen"/> - a touchscreen with no pen already
    /// has something to draw with, so mouse drawing is the last resort rather
    /// than the second choice.
    /// </summary>
    WhenNoDigitizer = 2,
}

public sealed class AppSettings
{
    public int Version { get; set; } = AppSettingsSerializer.CurrentVersion;

    public ToolbarPlacement ToolbarPlacement { get; set; } = ToolbarPlacement.TopRight;

    public CalligraphyAccess CalligraphyAccess { get; set; } = CalligraphyAccess.DualPalette;

    public StartupMonitorKind StartupMonitor { get; set; } = StartupMonitorKind.WacomIfPresent;

    public string? StartupMonitorName { get; set; }

    public bool StartFullScreen { get; set; }

    public FingerMode FingerMode { get; set; } = FingerMode.WhenNoPen;

    public MouseMode MouseMode { get; set; } = MouseMode.WhenNoDigitizer;

    /// <summary>
    /// Whether to offer Mouse drawing the first time in a session that someone
    /// picks a tool with the mouse while it is off. Reaching for the toolbar
    /// with a mouse is the one moment the application can be sure the question
    /// is worth asking, and the setting is what makes the offer refusable for
    /// good.
    /// </summary>
    public bool SuggestMouseMode { get; set; } = true;

    /// <summary>
    /// Whether the Eraser button stays on the toolbar when nothing else puts it
    /// there. Off by default because the pen's reverse end already erases and
    /// the row costs the toolbar its height; on for the pens that have no
    /// reverse end, which otherwise cannot reach the Eraser at all.
    /// </summary>
    public bool ShowEraserButton { get; set; }

    public List<string> SnippetFormatOrder { get; set; } = [.. TextLanguageIds.All];

    public InkToolSettings Pen { get; set; } = InkToolSettings.From(InkPalettes.DefaultPen);

    public InkToolSettings Highlighter { get; set; } =
        InkToolSettings.From(InkPalettes.DefaultHighlighter);

    public InkToolSettings Calligraphy { get; set; } =
        InkToolSettings.From(InkPalettes.DefaultCalligraphy);

    public LaserSettings Laser { get; set; } = new();

    public PenButtonSettings PenButtons { get; set; } = new();

    public ExportSettings Export { get; set; } = new();

    /// <summary>
    /// Whether to say at startup that Windows reports nothing to draw with. The
    /// tablet list this reads is a list of digitizers rather than an answer
    /// about what is plugged in, so a pen that has never been brought into
    /// range can be missing from it - which is the other reason this can be
    /// turned off.
    /// </summary>
    public bool WarnWhenNoDigitizer { get; set; } = true;

    public bool CheckForUpdates { get; set; } = true;

    public DateTimeOffset? LastUpdateCheckUtc { get; set; }

    public string? LatestKnownVersion { get; set; }

    public string? LastDismissedVersion { get; set; }

    public string? UpdateCheckETag { get; set; }
}

public static class AppSettingsSerializer
{
    public const int CurrentVersion = 15;

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

        if (!Enum.IsDefined(settings.FingerMode))
        {
            settings.FingerMode = FingerMode.WhenNoPen;
        }

        if (!Enum.IsDefined(settings.MouseMode))
        {
            settings.MouseMode = MouseMode.WhenNoDigitizer;
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
        settings.PenButtons = PenButtonSettings.Normalize(settings.PenButtons);
        settings.Export = ExportSettings.Normalize(settings.Export);
        settings.SnippetFormatOrder = [.. TextLanguageIds.NormalizeOrder(settings.SnippetFormatOrder)];
        settings.LatestKnownVersion = NormalizeVersionId(settings.LatestKnownVersion);
        settings.LastDismissedVersion = NormalizeVersionId(settings.LastDismissedVersion);
        if (string.IsNullOrWhiteSpace(settings.UpdateCheckETag))
        {
            settings.UpdateCheckETag = null;
        }

        settings.Version = CurrentVersion;
        return settings;
    }

    private static string? NormalizeVersionId(string? value)
    {
        if (!UpdateVersion.TryParse(value, out var version))
        {
            return null;
        }

        return UpdateVersion.Format(version);
    }
}
