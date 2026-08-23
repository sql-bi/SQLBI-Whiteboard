using SQLBI.Whiteboard.Core.Settings;

namespace SQLBI.Whiteboard;

internal enum SettingEditorKind
{
    EnumChoice,
    BooleanSwitch,
    DoubleRange,
    MonitorChoice,
    OrderedList,

    /// <summary>
    /// The laser trail weights, drawn side by side as the strokes they produce.
    /// Naming the options tells you nothing about what they look like, and this
    /// is a setting about how something looks.
    /// </summary>
    LaserWeightChoice,
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

    public bool HideInStore { get; init; }
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

        public const string LaserTrailWeight = "laser.trailWeight";
        public const string ToolbarPlacement = "toolbar.placement";
        public const string ToolbarLayout = "toolbar.layout";
        public const string FingerMode = "input.fingerMode";
        public const string SnippetFormatOrder = "input.snippetFormatOrder";
        public const string CheckForUpdates = "updates.check";
    }

    public const string Startup = "Startup";
    public const string Input = "Input";
    public const string Laser = "Laser pointer";
    public const string Toolbar = "Toolbar";
    public const string Updates = "Updates";

    public static IReadOnlyList<string> Categories { get; } =
        [Startup, Input, Laser, Toolbar, Updates];

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
            Description = "Fill the current monitor and hide the title and tabs the next time the application starts. F11 still toggles this session. Ctrl+F11 hides chrome without filling the monitor.",
            Keywords = ["fullscreen", "full screen", "f11", "maximize"],
            Editor = SettingEditorKind.BooleanSwitch,
        },
        new()
        {
            Id = Ids.FingerMode,
            Category = Input,
            Title = "Finger drawing",
            Description = "New installs default to When no pen is detected. Off keeps one-finger pan. On makes one finger use the current tool; two fingers still pan and pinch-zoom, and Eraser and Pan appear on the toolbar. \"When no pen is detected\" uses the digitizer list Windows reports, which is not the same as a pen being in the room.",
            Keywords = ["finger", "touch", "pen", "draw", "tablet", "stylus", "digitizer"],
            Editor = SettingEditorKind.EnumChoice,
            Choices =
            [
                new() { Id = nameof(Core.Settings.FingerMode.WhenNoPen), Title = "When no pen is detected" },
                new() { Id = nameof(Core.Settings.FingerMode.Off), Title = "Off" },
                new() { Id = nameof(Core.Settings.FingerMode.On), Title = "On" },
            ],
        },
        new()
        {
            Id = Ids.SnippetFormatOrder,
            Category = Input,
            Title = "Snippet format order",
            Description = "Paste tries formats from top to bottom and uses the first that accepts the text. Plain text always accepts, so putting it first keeps every paste as plain text. Recognized file extensions (.dax, .sql, .txt) keep their language.",
            Keywords = ["snippet", "language", "dax", "sql", "paste", "format", "text", "order"],
            Editor = SettingEditorKind.OrderedList,
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
            Id = Ids.LaserTrailWeight,
            Category = Laser,
            Title = "Trail weight",
            Description = "A pen reports little pressure on a quick tap. Each option shows that tap above a firm stroke: the firm stroke never changes, only how much the light one is thinned out.",
            Keywords = ["laser", "weight", "thickness", "width", "pressure", "trail"],
            Editor = SettingEditorKind.LaserWeightChoice,
            Choices =
            [
                new() { Id = nameof(LaserTrailWeight.Light), Title = "Light" },
                new() { Id = nameof(LaserTrailWeight.Medium), Title = "Medium" },
                new() { Id = nameof(LaserTrailWeight.Bold), Title = "Bold" },
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
        new()
        {
            Id = Ids.CheckForUpdates,
            Category = Updates,
            Title = "Check for new versions",
            Description = "Once a day the application asks GitHub whether a newer released build exists. It does not send a machine identifier. Microsoft Store installs are updated by the Store and never make this request.",
            Keywords = ["update", "version", "github", "download", "release"],
            Editor = SettingEditorKind.BooleanSwitch,
            HideInStore = true,
        },
    ];

    public static IReadOnlyList<SettingDescriptor> Filter(string? query, string? category)
    {
        IEnumerable<SettingDescriptor> items = All;
        if (StorePackage.IsStoreInstall)
        {
            items = items.Where(setting => !setting.HideInStore);
        }

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
