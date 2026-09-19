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

/// <summary>
/// The shape a select gesture on empty canvas draws. It is remembered rather
/// than chosen each time, because the Edit row's Lasso toggle is what switches
/// it and a toggle has to come back where it was left.
/// </summary>
public enum AreaSelectionTool
{
    Rectangle = 0,
    Lasso = 1,
}

/// <summary>
/// The faint grid behind the board. It is an application preference rather than
/// something a board carries: it says how one person likes to work, and it is
/// drawn on screen only, never in an export or a preview.
/// </summary>
public enum GridStyle
{
    Off = 0,
    Lines = 1,
    Dots = 2,
}

/// <summary>
/// Which of the design-era controls exist. A mode says nothing about what a
/// board contains: a board made in Design opens in Teaching with every shape,
/// label, and connector still drawn and still exported, and only the tools to
/// make or restyle them are absent.
/// </summary>
public enum BoardMode
{
    /// <summary>
    /// The 1.5.2 board: ink, laser, eraser, pan, containers, frames, export,
    /// the grid, and the selection everything else already had.
    /// </summary>
    Teaching = 0,

    /// <summary>
    /// Everything. The default, because a release is judged on what it does
    /// rather than on what it withholds.
    /// </summary>
    Design = 1,

    /// <summary>
    /// Teaching plus whichever feature groups are switched on, so a change
    /// meant for Teaching can be tried one group at a time.
    /// </summary>
    Custom = 2,
}

/// <summary>
/// What happens to the Insert tool once it has drawn something. Handing it back
/// to Select is the default, so the shape just drawn is the thing the next tap
/// picks up rather than the start of another; Escape and any tool button leave
/// the tool either way.
/// </summary>
public enum AfterInsert
{
    KeepTool = 0,
    ReturnToSelect = 1,
}

public sealed class AppSettings
{
    public int Version { get; set; } = AppSettingsSerializer.CurrentVersion;

    public BoardMode Mode { get; set; } = BoardMode.Design;

    /// <summary>
    /// What the View row's Design toggle turns back to, the way
    /// <see cref="LastGridStyle"/> says which grid the Grid button brings back.
    /// Design itself is never kept here: it would leave the toggle with nothing
    /// to return to.
    /// </summary>
    public BoardMode LastNonDesignMode { get; set; } = BoardMode.Teaching;

    /// <summary>
    /// The four groups <see cref="BoardMode.Custom"/> is made of. They are on
    /// by default, and choosing Teaching or Design leaves them as they are, so
    /// Custom comes back to the arrangement it was left in.
    /// </summary>
    public bool DesignTools { get; set; } = true;

    public bool PropertyBar { get; set; } = true;

    public bool ExtendedSelection { get; set; } = true;

    public bool DepthAndDuplicate { get; set; } = true;

    public AreaSelection AreaSelection { get; set; } = AreaSelection.PartlyInside;

    public ExtendSelection ExtendSelection { get; set; } = ExtendSelection.Ignore;

    public AreaSelectionTool AreaSelectionTool { get; set; } = AreaSelectionTool.Rectangle;

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

    public GridStyle Grid { get; set; } = GridStyle.Off;

    /// <summary>
    /// What the View row's Grid button turns back on. Someone who prefers dots
    /// gets dots back, and a first toggle from the default gives lines rather
    /// than nothing at all.
    /// </summary>
    public GridStyle LastGridStyle { get; set; } = GridStyle.Lines;

    /// <summary>
    /// Whether the floating toolbar carries an Insert button and a Lasso
    /// chevron. Off, because it is critical that the compact layouts stay the
    /// width they are; the Insert tab holds the same things whatever this says.
    /// </summary>
    public bool InsertOnToolbar { get; set; }

    public AfterInsert AfterInsert { get; set; } = AfterInsert.ReturnToSelect;

    /// <summary>
    /// Whether the Insert palette - the shapes, the connectors, and Text on a
    /// panel of their own - is on the board. The pin at the end of the Insert
    /// row and the Preferences row both ask for the same thing, so the answer
    /// is kept here rather than in either of them.
    /// </summary>
    public bool InsertPaletteShown { get; set; }

    /// <summary>
    /// Where that palette was left, as a fraction of the window's client size,
    /// so another window size or another monitor puts it back roughly where it
    /// was. Null until it is first dragged, which is what lets the default keep
    /// following <see cref="ToolbarPlacement"/>.
    /// </summary>
    public double? InsertPaletteX { get; set; }

    public double? InsertPaletteY { get; set; }

    public ShapeSettings Shape { get; set; } = new();

    /// <summary>
    /// What the last connector was drawn with, so the next one is drawn the
    /// same way.
    /// </summary>
    public ConnectorSettings Connector { get; set; } = new();

    /// <summary>
    /// What the last label was written with, so the next one is written the
    /// same way. Like the pen's color and size, it is remembered rather than
    /// configured, and so has no row in Preferences.
    /// </summary>
    public LabelSettings Label { get; set; } = new();

    public List<string> SnippetFormatOrder { get; set; } = [.. TextLanguageIds.DetectionOrder];

    public InkToolSettings Pen { get; set; } = InkToolSettings.From(InkPalettes.DefaultPen);

    public InkToolSettings Highlighter { get; set; } =
        InkToolSettings.From(InkPalettes.DefaultHighlighter);

    public InkToolSettings Calligraphy { get; set; } =
        InkToolSettings.From(InkPalettes.DefaultCalligraphy);

    public LaserSettings Laser { get; set; } = new();

    public PenButtonSettings PenButtons { get; set; } = new();

    public ExportSettings Export { get; set; } = new();

    public ImportSettings Import { get; set; } = new();

    /// <summary>
    /// Whether to say at startup that Windows reports nothing to draw with. The
    /// tablet list this reads is a list of digitizers rather than an answer
    /// about what is plugged in, so a pen that has never been brought into
    /// range can be missing from it - which is the other reason this can be
    /// turned off.
    /// </summary>
    public bool WarnWhenNoDigitizer { get; set; } = true;

    /// <summary>
    /// Whether a board left open at the last exit comes back at the next start. It governs
    /// the silent restore only: a board left behind by a copy that crashed is always
    /// offered, because losing work to a crash is the thing the session copy exists to
    /// prevent and is not a preference about how the application starts.
    /// </summary>
    public bool RestoreLastSession { get; set; } = true;

    public bool CheckForUpdates { get; set; } = true;

    public DateTimeOffset? LastUpdateCheckUtc { get; set; }

    public string? LatestKnownVersion { get; set; }

    public string? LastDismissedVersion { get; set; }

    public string? UpdateCheckETag { get; set; }
}

public static class AppSettingsSerializer
{
    public const int CurrentVersion = 19;

    /// <summary>
    /// The version that moved plain text to the end of the default snippet
    /// format order. A file older than this whose order is still a shipped
    /// default was never customized, and takes the new default.
    /// </summary>
    private const int VersionWithPlainTextLast = 16;

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

        if (!Enum.IsDefined(settings.Mode))
        {
            settings.Mode = BoardMode.Design;
        }

        // Design here would leave the View row's toggle with nothing to return
        // to, the way Off would leave the Grid button with nothing to turn on.
        if (!Enum.IsDefined(settings.LastNonDesignMode) ||
            settings.LastNonDesignMode == BoardMode.Design)
        {
            settings.LastNonDesignMode = BoardMode.Teaching;
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

        if (!Enum.IsDefined(settings.AreaSelection))
        {
            settings.AreaSelection = AreaSelection.PartlyInside;
        }

        if (!Enum.IsDefined(settings.ExtendSelection))
        {
            settings.ExtendSelection = ExtendSelection.Ignore;
        }

        if (!Enum.IsDefined(settings.AreaSelectionTool))
        {
            settings.AreaSelectionTool = AreaSelectionTool.Rectangle;
        }

        if (!Enum.IsDefined(settings.Grid))
        {
            settings.Grid = GridStyle.Off;
        }

        // Off here would leave the View row's toggle with nothing to turn on.
        if (!Enum.IsDefined(settings.LastGridStyle) || settings.LastGridStyle == GridStyle.Off)
        {
            settings.LastGridStyle = GridStyle.Lines;
        }

        if (!Enum.IsDefined(settings.AfterInsert))
        {
            settings.AfterInsert = AfterInsert.ReturnToSelect;
        }

        settings.InsertPaletteX = NormalizeFraction(settings.InsertPaletteX);
        settings.InsertPaletteY = NormalizeFraction(settings.InsertPaletteY);

        settings.Shape = ShapeSettings.Normalize(settings.Shape);
        settings.Connector = ConnectorSettings.Normalize(settings.Connector);

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
        settings.Label = LabelStyles.Normalize(settings.Label);
        settings.PenButtons = PenButtonSettings.Normalize(settings.PenButtons);
        settings.Export = ExportSettings.Normalize(settings.Export);
        settings.Import = ImportSettings.Normalize(settings.Import);
        if (settings.Version < VersionWithPlainTextLast &&
            TextLanguageIds.IsLegacyDefaultOrder(settings.SnippetFormatOrder))
        {
            settings.SnippetFormatOrder = [.. TextLanguageIds.DetectionOrder];
        }

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

    /// <summary>
    /// A palette position is a fraction of the window, so anything outside 0..1
    /// is a file edited by hand or a window that has since changed shape. It is
    /// pulled back to the edge rather than dropped, which keeps the palette on
    /// the board and near where it was asked for.
    /// </summary>
    private static double? NormalizeFraction(double? value)
    {
        if (value is not { } fraction || !double.IsFinite(fraction))
        {
            return null;
        }

        return Math.Clamp(fraction, 0, 1);
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
