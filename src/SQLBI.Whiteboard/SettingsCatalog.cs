using SQLBI.Whiteboard.Core.Settings;

namespace SQLBI.Whiteboard;

internal enum SettingEditorKind
{
    EnumChoice,
    BooleanSwitch,
    DoubleRange,
    MonitorChoice,
}

internal sealed class SettingChoice
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public bool IsSeparator { get; init; }

    public bool IsAvailable { get; init; } = true;
}

internal sealed class SettingDescriptor
{
    public required string Id { get; init; }

    public required string Category { get; init; }

    public required string Title { get; init; }

    public required string Description { get; init; }

    public required string[] Keywords { get; init; }

    public required SettingEditorKind Editor { get; init; }

    public IReadOnlyList<SettingChoice> Choices { get; init; } = [];

    public double Minimum { get; init; }

    public double Maximum { get; init; }
}

internal static class SettingsCatalog
{
    public static class Ids
    {
        public const string StartupMonitor = "startup.monitor";
        public const string StartFullScreen = "startup.fullscreen";
        public const string LaserHoldSeconds = "laser.holdSeconds";
        public const string LaserFadeSeconds = "laser.fadeSeconds";
        public const string LaserHoldMode = "laser.holdMode";
        public const string ToolbarPlacement = "toolbar.placement";
        public const string ToolbarLayout = "toolbar.layout";
        public const string FingerMode = "input.fingerMode";
    }

    public const string Startup = "Startup";
    public const string Input = "Input";
    public const string Laser = "Laser pointer";
    public const string Toolbar = "Toolbar";

    public static IReadOnlyList<string> Categories { get; } = [Startup, Input, Laser, Toolbar];

    public static IReadOnlyList<SettingDescriptor> All { get; } =
    [
        new()
        {
            Id = Ids.StartupMonitor,
            Category = Startup,
            Title = "Open on",
            Description = "Which display the window uses at launch.",
            Keywords = ["monitor", "display", "cintiq", "wacom", "screen"],
            Editor = SettingEditorKind.MonitorChoice,
        },
        new()
        {
            Id = Ids.StartFullScreen,
            Category = Startup,
            Title = "Start full screen",
            Description = "Hide the menu and title bar the next time the application starts. F11 still toggles this session.",
            Keywords = ["fullscreen", "full screen", "f11", "maximize"],
            Editor = SettingEditorKind.BooleanSwitch,
        },
        new()
        {
            Id = Ids.FingerMode,
            Category = Input,
            Title = "Finger drawing",
            Description = "Off keeps one-finger pan. On makes one finger use the current tool; two fingers still pan and pinch-zoom, and Eraser and Pan appear on the toolbar. \"When no pen is detected\" uses the digitizer list Windows reports, which is not the same as a pen being in the room.",
            Keywords = ["finger", "touch", "pen", "draw", "tablet", "stylus", "digitizer"],
            Editor = SettingEditorKind.EnumChoice,
            Choices =
            [
                new() { Id = nameof(Core.Settings.FingerMode.Off), Title = "Off" },
                new() { Id = nameof(Core.Settings.FingerMode.On), Title = "On" },
                new() { Id = nameof(Core.Settings.FingerMode.WhenNoPen), Title = "When no pen is detected" },
            ],
        },
        new()
        {
            Id = Ids.LaserHoldSeconds,
            Category = Laser,
            Title = "Trail duration",
            Description = "How long the laser stays fully visible after you lift.",
            Keywords = ["laser", "decay", "hold", "trail", "duration"],
            Editor = SettingEditorKind.DoubleRange,
            Minimum = LaserSettings.MinimumHoldSeconds,
            Maximum = LaserSettings.MaximumHoldSeconds,
        },
        new()
        {
            Id = Ids.LaserFadeSeconds,
            Category = Laser,
            Title = "Fade duration",
            Description = "How long the trail takes to disappear after the hold.",
            Keywords = ["laser", "fade", "decay", "trail"],
            Editor = SettingEditorKind.DoubleRange,
            Minimum = LaserSettings.MinimumFadeSeconds,
            Maximum = LaserSettings.MaximumFadeSeconds,
        },
        new()
        {
            Id = Ids.LaserHoldMode,
            Category = Laser,
            Title = "Hold",
            Description = "Whether a new stroke keeps the previous trail alive or starts its own timer.",
            Keywords = ["laser", "hold", "shared", "stroke"],
            Editor = SettingEditorKind.EnumChoice,
            Choices =
            [
                new() { Id = nameof(LaserHoldMode.Shared), Title = "Shared across strokes" },
                new() { Id = nameof(LaserHoldMode.PerStroke), Title = "Each stroke separately" },
            ],
        },
        new()
        {
            Id = Ids.ToolbarPlacement,
            Category = Toolbar,
            Title = "Position",
            Description = "Top right keeps the toolbar under a typical presenter picture-in-picture during recording.",
            Keywords = ["toolbar", "position", "placement", "pip"],
            Editor = SettingEditorKind.EnumChoice,
            Choices =
            [
                new() { Id = nameof(ToolbarPlacement.TopRight), Title = "Top right" },
                new() { Id = nameof(ToolbarPlacement.TopLeft), Title = "Top left" },
                new() { Id = nameof(ToolbarPlacement.BottomRight), Title = "Bottom right" },
                new() { Id = nameof(ToolbarPlacement.BottomLeft), Title = "Bottom left" },
                new() { Id = nameof(ToolbarPlacement.BottomCenter), Title = "Bottom center" },
            ],
        },
        new()
        {
            Id = Ids.ToolbarLayout,
            Category = Toolbar,
            Title = "Layout",
            Description = "Dual palette keeps both tools’ colors and sizes visible. The other layouts use a compact bar and a single-tool panel.",
            Keywords = ["toolbar", "layout", "calligraphy", "palette", "chevron"],
            Editor = SettingEditorKind.EnumChoice,
            Choices =
            [
                new() { Id = nameof(CalligraphyAccess.DualPalette), Title = "Dual palette" },
                new() { Id = nameof(CalligraphyAccess.Chevron), Title = "Chevron on the Pen button" },
                new() { Id = nameof(CalligraphyAccess.SizeRow), Title = "Icons beside the size chips" },
            ],
        },
    ];

    public static IReadOnlyList<SettingDescriptor> Filter(string? query, string? category)
    {
        IEnumerable<SettingDescriptor> items = All;
        if (!string.IsNullOrWhiteSpace(category))
        {
            items = items.Where(setting => setting.Category == category);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            items = items.Where(setting => Matches(setting, query));
        }

        return items.ToArray();
    }

    public static IReadOnlyList<string> CategoriesFor(IReadOnlyList<SettingDescriptor> items) =>
        Categories.Where(category => items.Any(item => item.Category == category)).ToArray();

    public static bool Matches(SettingDescriptor setting, string query)
    {
        ArgumentNullException.ThrowIfNull(setting);
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var term = query.Trim();
        return Contains(setting.Title, term) ||
               Contains(setting.Description, term) ||
               Contains(setting.Category, term) ||
               setting.Keywords.Any(keyword => Contains(keyword, term));
    }

    private static bool Contains(string value, string term) =>
        value.Contains(term, StringComparison.OrdinalIgnoreCase);
}
