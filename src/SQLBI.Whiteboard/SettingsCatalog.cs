using SQLBI.Whiteboard.Core.Settings;

namespace SQLBI.Whiteboard;

internal enum SettingEditorKind
{
    EnumChoice,
    BooleanSwitch,
    BooleanCheckbox,
    DoubleRange,
    MonitorChoice,
    OrderedList,

    /// <summary>
    /// The laser trail weights, drawn side by side as the strokes they produce.
    /// Naming the options tells you nothing about what they look like, and this
    /// is a setting about how something looks.
    /// </summary>
    LaserWeightChoice,

    /// <summary>
    /// What the pen's barrel button does, drawn as what it draws: a laser trail
    /// or a wobble ruled straight.
    /// </summary>
    PenButtonChoice,

    /// <summary>
    /// Where the toolbar sits, drawn as a board with the toolbar in it. A corner
    /// is a place, and a picture of the place beats the name of it.
    /// </summary>
    ToolbarPlacementChoice,

    /// <summary>
    /// How the ink flyout is arranged, drawn as a miniature of the flyout.
    /// </summary>
    ToolbarLayoutChoice,

    /// <summary>
    /// Whether the Eraser is on the toolbar, drawn as the toolbar with and
    /// without it. It is a boolean, but a switch says nothing about where the
    /// button would appear, and that is the part worth seeing - so the two
    /// pictures follow whatever <see cref="ToolbarLayoutChoice"/> has chosen.
    /// </summary>
    EraserButtonChoice,

    /// <summary>
    /// A choice drawn rather than named, where the picture is not one of the
    /// five above. Every one of them asks what the board will look like or what
    /// a gesture will take, and a name for that is a promise the reader has to
    /// imagine; the row's <see cref="SettingDescriptor.Id"/> is what picks the
    /// pictures, so a new one of these is a factory rather than another member
    /// here. A boolean joins them by carrying the two
    /// <see cref="SettingsCatalog.BooleanChoice"/> ids as its choices.
    /// </summary>
    DrawnChoice,
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

    /// <summary>
    /// The one line that is always on the row, under the title. It has to say
    /// what the setting is for in a single line at the dialog's width, because
    /// nothing else about the setting is visible until someone asks for it.
    /// </summary>
    public required string Summary { get; init; }

    /// <summary>
    /// The reasoning, the defaults, and the consequences - everything that will
    /// not fit on one line, shown only when the row is expanded. Empty for a
    /// setting whose summary is the whole story, and such a row has no
    /// disclosure at all.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    public required string[] Keywords { get; init; }

    public required SettingEditorKind Editor { get; init; }

    public IReadOnlyList<SettingChoice> Choices { get; init; } = [];

    public double Minimum { get; init; }

    public double Maximum { get; init; }

    public double SmallChange { get; init; } = 1;

    public double LargeChange { get; init; } = 10;

    public string Unit { get; init; } = string.Empty;

    public bool HideInStore { get; init; }

    /// <summary>
    /// Whether the row is in the list at all. A setting that only governs a
    /// feature group that is off has nothing to say, so it leaves rather than
    /// sitting there greyed: Teaching's Preferences are 1.5.2's plus Mode,
    /// Board, and the Selection rows its own group keeps.
    /// </summary>
    public Func<AppSettings, bool>? VisibleWhen { get; init; }

    /// <summary>
    /// Whether the row's editor can be used. Unlike <see cref="VisibleWhen"/>
    /// the row stays, showing the value it holds: the four group switches are
    /// what Custom is made of, and reading them is how Custom is chosen at all.
    /// </summary>
    public Func<AppSettings, bool>? EnabledWhen { get; init; }
}

internal static class SettingsCatalog
{
    public static class Ids
    {
        public const string Mode = "mode.mode";
        public const string DesignTools = "mode.designTools";
        public const string PropertyBar = "mode.propertyBar";
        public const string ExtendedSelection = "mode.extendedSelection";
        public const string DepthAndDuplicate = "mode.depthAndDuplicate";
        public const string StartupMonitor = "startup.monitor";
        public const string StartFullScreen = "startup.fullscreen";
        public const string ImportHorizontalSpacing = "import.horizontalSpacing";
        public const string ImportVerticalSpacing = "import.verticalSpacing";
        public const string PauseLiveViewsWhenUnfocused = "liveView.pauseWhenUnfocused";
        public const string LaserHoldSeconds = "laser.holdSeconds";
        public const string LaserFadeSeconds = "laser.fadeSeconds";
        public const string LaserHoldMode = "laser.holdMode";

        public const string LaserTrailWeight = "laser.trailWeight";
        public const string ToolbarPlacement = "toolbar.placement";
        public const string ToolbarLayout = "toolbar.layout";
        public const string ShowEraserButton = "toolbar.eraserButton";
        public const string InsertOnToolbar = "toolbar.insert";
        public const string InsertPalette = "toolbar.insertPalette";
        public const string AfterInsert = "toolbar.afterInsert";
        public const string WarnWhenNoDigitizer = "startup.noDigitizerNotice";
        public const string RestoreLastSession = "startup.restoreLastSession";
        public const string FingerMode = "input.fingerMode";
        public const string MouseMode = "input.mouseMode";
        public const string SuggestMouseMode = "input.mouseModeOffer";
        public const string PenButton = "input.penButton";
        public const string SnippetFormatOrder = "input.snippetFormatOrder";
        public const string AreaSelection = "selection.area";
        public const string ExtendSelection = "selection.extend";
        public const string Grid = "board.grid";
        public const string CheckForUpdates = "updates.check";
    }

    /// <summary>
    /// The two states of a boolean that is offered as a pair of drawn choices
    /// rather than as a switch. Such a row needs choice ids where a switched
    /// boolean needs none, and they are the same two for every one of them, so
    /// that one read and one write serve them all.
    /// </summary>
    public static class BooleanChoice
    {
        public const string Off = "Off";
        public const string On = "On";
    }

    /// <summary>
    /// The choices of a drawn boolean, which are the same wherever they appear.
    /// </summary>
    private static readonly SettingChoice[] OffOn =
    [
        new() { Id = BooleanChoice.Off, Title = "Off" },
        new() { Id = BooleanChoice.On, Title = "On" },
    ];

    public const string Mode = "Mode";
    public const string Startup = "Startup";
    public const string Input = "Input";
    public const string Selection = "Selection";
    public const string Board = "Board";
    public const string Import = "Import";
    public const string LiveView = "Live View";
    public const string Laser = "Laser pointer";
    public const string Toolbar = "Toolbar";
    public const string Updates = "Updates";

    public static IReadOnlyList<string> Categories { get; } =
        [Mode, Startup, Input, Selection, Board, Import, LiveView, Laser, Toolbar, Updates];

    /// <summary>
    /// Whether a group switch can be used: only while the mode is Custom, which
    /// is the one mode made of them.
    /// </summary>
    private static bool IsCustom(AppSettings settings) =>
        settings.Mode == BoardMode.Custom;

    /// <summary>
    /// A setting that only says how a feature group behaves goes with the group
    /// it belongs to, rather than offering a choice about something that is not
    /// there.
    /// </summary>
    private static bool HasDesignTools(AppSettings settings) =>
        Modes.Resolve(settings).DesignTools;

    private static bool HasExtendedSelection(AppSettings settings) =>
        Modes.Resolve(settings).ExtendedSelection;

    public static IReadOnlyList<SettingDescriptor> All { get; } =
    [
        new()
        {
            Id = Ids.Mode,
            Category = Mode,
            Title = "Mode",
            Summary = "Which of the design controls the board offers",
            Description = "Design offers every tool. Teaching keeps the annotation tools and the lasso, and hides the rest. Custom uses the groups below.",
            Keywords = ["mode", "teaching", "design", "custom", "simple", "hide", "tools", "insert"],
            Editor = SettingEditorKind.EnumChoice,
            Choices =
            [
                new() { Id = nameof(BoardMode.Teaching), Title = "Teaching" },
                new() { Id = nameof(BoardMode.Design), Title = "Design" },
                new() { Id = nameof(BoardMode.Custom), Title = "Custom" },
            ],
        },
        new()
        {
            Id = Ids.DesignTools,
            Category = Mode,
            Title = "Design tools",
            Summary = "The Insert tab and palette, the shape, connector, and text tools, and their handles",
            Keywords = ["insert", "shape", "connector", "text", "palette", "rotation", "handle", "custom"],
            Editor = SettingEditorKind.DrawnChoice,
            Choices = OffOn,
            EnabledWhen = IsCustom,
        },
        new()
        {
            Id = Ids.PropertyBar,
            Category = Mode,
            Title = "Property bar",
            Summary = "The bar above a selection: color, thickness, fill, font, and the … menu",
            Keywords = ["property", "bar", "color", "thickness", "fill", "font", "menu", "custom"],
            Editor = SettingEditorKind.DrawnChoice,
            Choices = OffOn,
            EnabledWhen = IsCustom,
        },
        new()
        {
            Id = Ids.ExtendedSelection,
            Category = Mode,
            Title = "Extended selection",
            Summary = "A tap selecting a stroke, the lasso, and the two Selection settings",
            Keywords = ["selection", "stroke", "lasso", "area", "extend", "touching", "custom"],
            Editor = SettingEditorKind.DrawnChoice,
            Choices = OffOn,
            EnabledWhen = IsCustom,
        },
        new()
        {
            Id = Ids.DepthAndDuplicate,
            Category = Mode,
            Title = "Depth and duplicate",
            Summary = "Bring forward, Send backward, and Duplicate",
            Keywords = ["depth", "forward", "backward", "duplicate", "order", "z-order", "custom"],
            Editor = SettingEditorKind.DrawnChoice,
            Choices = OffOn,
            EnabledWhen = IsCustom,
        },
        new()
        {
            Id = Ids.StartupMonitor,
            Category = Startup,
            Title = "Open on",
            Summary = "Which display the window uses at launch",
            Keywords = ["monitor", "display", "cintiq", "wacom", "screen"],
            Editor = SettingEditorKind.MonitorChoice,
        },
        new()
        {
            Id = Ids.StartFullScreen,
            Category = Startup,
            Title = "Start full screen",
            Summary = "Fill the monitor and hide the chrome at launch",
            Description = "Fill the current monitor and hide the title and tabs the next time the application starts. F11 still toggles this session. Ctrl+F11 hides chrome without filling the monitor.",
            Keywords = ["fullscreen", "full screen", "f11", "maximize"],
            Editor = SettingEditorKind.BooleanSwitch,
        },
        new()
        {
            Id = Ids.RestoreLastSession,
            Category = Startup,
            Title = "Reopen the last board",
            Summary = "Come back to whatever was open when you last closed",
            Description = "Come back to the board that was open the last time the application closed, at the zoom and position you left it; off, every start gives you a blank board. A LiveView container returns as its last frame and needs Reconnect.",
            Keywords = ["session", "restore", "reopen", "autosave", "recover", "crash", "startup", "last"],
            Editor = SettingEditorKind.BooleanSwitch,
        },
        new()
        {
            Id = Ids.WarnWhenNoDigitizer,
            Category = Startup,
            Title = "Warn when there is nothing to draw with",
            Summary = "Say so at startup when Windows reports no digitizer",
            Description = "Say so at startup when Windows reports neither a pen tablet nor a touchscreen, and say what Mouse drawing gives you in place of a pen. Turn it off if the notice appears on a machine that does have one.",
            Keywords = ["pen", "touch", "touchscreen", "digitizer", "tablet", "mouse", "warning", "notice", "startup"],
            Editor = SettingEditorKind.BooleanSwitch,
        },
        new()
        {
            Id = Ids.FingerMode,
            Category = Input,
            Title = "Finger drawing",
            Summary = "Whether one finger draws or pans",
            Description = "Off keeps one finger for panning the board. On makes one finger draw with the current tool and puts Eraser and Pan on the toolbar. When no pen is detected draws only on a machine where Windows reports no pen.",
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
            Id = Ids.MouseMode,
            Category = Input,
            Title = "Mouse drawing",
            Summary = "What the left mouse button does",
            Description = "Off keeps the left button for moving and resizing containers. On makes it draw with the current tool and puts Eraser and Pan on the toolbar; Ctrl and the left button then move a container. When there is no pen or touchscreen draws only on a machine where Windows reports neither.",
            Keywords = ["mouse", "draw", "drawing", "pointer", "no pen", "digitizer", "left button", "ctrl"],
            Editor = SettingEditorKind.EnumChoice,
            Choices =
            [
                new() { Id = nameof(Core.Settings.MouseMode.WhenNoDigitizer), Title = "When there is no pen or touchscreen" },
                new() { Id = nameof(Core.Settings.MouseMode.Off), Title = "Off" },
                new() { Id = nameof(Core.Settings.MouseMode.On), Title = "On" },
            ],
        },
        new()
        {
            Id = Ids.SuggestMouseMode,
            Category = Input,
            Title = "Offer mouse drawing when the mouse picks a tool",
            Summary = "Ask once a session while mouse drawing is off",
            Description = "With Mouse drawing off, choosing a tool from the toolbar with the mouse offers to turn it on. Asked once a session, and not again once the offer has been declined for good.",
            Keywords = ["mouse", "offer", "prompt", "dialog", "toolbar", "suggest", "ask"],
            Editor = SettingEditorKind.BooleanSwitch,
        },
        new()
        {
            Id = Ids.PenButton,
            Category = Input,
            Title = "Pen button",
            Summary = "What holding the barrel button does",
            Description = "The barrel button on the side of the pen. Hold it for the assigned action: Laser lasts only while the button is down, Straight line is the same constraint as holding Shift. The reverse end of the pen always erases, and so does the upper button, because Windows reports the two the same way.",
            Keywords = ["pen", "barrel", "button", "laser", "straight", "line", "shift", "stylus", "eraser", "wacom", "cintiq"],
            Editor = SettingEditorKind.PenButtonChoice,
            Choices =
            [
                new() { Id = nameof(PenButtonAction.Laser), Title = "Laser" },
                new() { Id = nameof(PenButtonAction.StraightLine), Title = "Straight line" },
            ],
        },
        new()
        {
            Id = Ids.SnippetFormatOrder,
            Category = Input,
            Title = "Snippet format order",
            Summary = "Which language pasted text is tried as first",
            Description = "Paste tries formats from top to bottom and uses the first that accepts the text. Plain text always accepts and comes last, so code is recognized and a note stays a note; put it first to keep every paste plain. Recognized file extensions (.dax, .sql, .kql, .txt) keep their language. Languages you choose by hand on a container are not in this list.",
            Keywords =
                ["snippet", "language", "dax", "sql", "kql", "paste", "format", "text", "order"],
            Editor = SettingEditorKind.OrderedList,
        },
        new()
        {
            Id = Ids.AreaSelection,
            Category = Selection,
            Title = "Area selects",
            Summary = "Whether an object has to be wholly inside the area",
            Description = "Objects partly inside takes anything the area meets, which is how a quick sweep picks up a diagram. Only objects fully inside asks for the whole of a stroke or an object to be inside, which is what you want when the thing you are after sits among others.",
            Keywords = ["select", "selection", "area", "rubber", "band", "lasso", "marquee", "inside", "partly", "fully"],
            Editor = SettingEditorKind.DrawnChoice,
            Choices =
            [
                new() { Id = nameof(Core.Model.AreaSelection.PartlyInside), Title = "Objects partly inside" },
                new() { Id = nameof(Core.Model.AreaSelection.FullyInside), Title = "Only objects fully inside" },
            ],
            VisibleWhen = HasExtendedSelection,
        },
        new()
        {
            Id = Ids.ExtendSelection,
            Category = Selection,
            Title = "Extend to touching",
            Summary = "Whether the selection grows to what it touches",
            Description = "Ignore takes the area at its word. Single adds one round, so a label beside a picture comes along with it. Recursive keeps going until nothing more is added, which takes a whole connected diagram from one stroke in it.",
            Keywords = ["select", "selection", "extend", "touching", "grow", "recursive", "connected", "neighbour", "neighbor"],
            Editor = SettingEditorKind.DrawnChoice,
            Choices =
            [
                new() { Id = nameof(Core.Model.ExtendSelection.Ignore), Title = "Ignore" },
                new() { Id = nameof(Core.Model.ExtendSelection.Single), Title = "Single" },
                new() { Id = nameof(Core.Model.ExtendSelection.Recursive), Title = "Recursive" },
            ],
            VisibleWhen = HasExtendedSelection,
        },
        new()
        {
            Id = Ids.Grid,
            Category = Board,
            Title = "Background grid",
            Summary = "A faint grid behind the board, off by default",
            Description = "Lines or Dots draw a faint grid under everything on screen, and never in an export, a preview, or the Explorer thumbnail.",
            Keywords = ["grid", "lines", "dots", "background", "board", "zoom", "graph paper", "squared"],
            Editor = SettingEditorKind.DrawnChoice,
            Choices =
            [
                new() { Id = nameof(GridStyle.Off), Title = "Off" },
                new() { Id = nameof(GridStyle.Lines), Title = "Lines" },
                new() { Id = nameof(GridStyle.Dots), Title = "Dots" },
            ],
        },
        new()
        {
            Id = Ids.ImportHorizontalSpacing,
            Category = Import,
            Title = "Horizontal spacing",
            Summary = "Space between imported containers in a row",
            Description = "Pixels at 100% zoom. Applies to the next .wimport file, without rearranging existing containers. The default is 32 px.",
            Keywords = ["import", "wimport", "horizontal", "spacing", "gap", "pixels", "layout"],
            Editor = SettingEditorKind.DoubleRange,
            Minimum = ImportSettings.MinimumSpacing,
            Maximum = ImportSettings.MaximumSpacing,
            Unit = "px",
        },
        new()
        {
            Id = Ids.ImportVerticalSpacing,
            Category = Import,
            Title = "Vertical spacing",
            Summary = "Space between imported rows",
            Description = "Pixels at 100% zoom, measured below the tallest item in the previous row. Applies to explicit line breaks and automatic wrapping in the next .wimport file. The default is 32 px.",
            Keywords = ["import", "wimport", "vertical", "spacing", "gap", "pixels", "row", "line", "layout"],
            Editor = SettingEditorKind.DoubleRange,
            Minimum = ImportSettings.MinimumSpacing,
            Maximum = ImportSettings.MaximumSpacing,
            Unit = "px",
        },
        new()
        {
            Id = Ids.PauseLiveViewsWhenUnfocused,
            Category = LiveView,
            Title = "Pause when Whiteboard loses focus",
            Summary = "Resume previously active LiveViews when you return",
            Description = "Stops capture while another application is in the foreground and keeps the last frame visible. LiveViews you paused manually stay paused. Whiteboard dialogs do not count as switching applications. Off by default; changes apply immediately.",
            Keywords = ["liveview", "live view", "capture", "pause", "resume", "focus", "background", "GPU"],
            Editor = SettingEditorKind.BooleanCheckbox,
        },
        new()
        {
            Id = Ids.LaserHoldSeconds,
            Category = Laser,
            Title = "Trail duration",
            Summary = "How long the laser stays fully visible after you lift",
            Keywords = ["laser", "decay", "hold", "trail", "duration"],
            Editor = SettingEditorKind.DoubleRange,
            Minimum = LaserSettings.MinimumHoldSeconds,
            Maximum = LaserSettings.MaximumHoldSeconds,
            SmallChange = 0.25,
            LargeChange = 1,
            Unit = "s",
        },
        new()
        {
            Id = Ids.LaserFadeSeconds,
            Category = Laser,
            Title = "Fade duration",
            Summary = "How long the trail takes to disappear after the hold",
            Keywords = ["laser", "fade", "decay", "trail"],
            Editor = SettingEditorKind.DoubleRange,
            Minimum = LaserSettings.MinimumFadeSeconds,
            Maximum = LaserSettings.MaximumFadeSeconds,
            SmallChange = 0.05,
            LargeChange = 0.5,
            Unit = "s",
        },
        new()
        {
            Id = Ids.LaserHoldMode,
            Category = Laser,
            Title = "Hold",
            Summary = "Whether a new stroke keeps the previous trail alive",
            Description = "Whether a new stroke keeps the previous trail alive or starts its own timer.",
            Keywords = ["laser", "hold", "shared", "stroke"],
            Editor = SettingEditorKind.DrawnChoice,
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
            Summary = "How much a light touch is thinned out",
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
            Summary = "Which corner the toolbar sits in",
            Description = "Top right keeps the toolbar under a typical presenter picture-in-picture during recording.",
            Keywords = ["toolbar", "position", "placement", "pip"],
            Editor = SettingEditorKind.ToolbarPlacementChoice,
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
            Summary = "How the colors and sizes are arranged",
            Description = "Dual palette keeps both tools’ colors and sizes visible. The other layouts use a compact bar and a single-tool panel.",
            Keywords = ["toolbar", "layout", "calligraphy", "palette", "chevron"],
            Editor = SettingEditorKind.ToolbarLayoutChoice,
            Choices =
            [
                new() { Id = nameof(CalligraphyAccess.DualPalette), Title = "Dual palette" },
                new() { Id = nameof(CalligraphyAccess.Chevron), Title = "Chevron on the Pen button" },
                new() { Id = nameof(CalligraphyAccess.SizeRow), Title = "Icons beside the size chips" },
            ],
        },
        new()
        {
            Id = Ids.ShowEraserButton,
            Category = Toolbar,
            Title = "Always show the Eraser",
            Summary = "Keep it on the toolbar for a pen without one",
            Description = "Off, the Eraser is on the toolbar only when finger or mouse drawing puts it there, because the pen's reverse end already erases. On, it stays there for the pen too, which is the only way to reach the Eraser with a pen that has no reverse end. It joins the row of tools in the compact layouts and sits under the palette in Dual palette. Pan is unaffected: it stays on the toolbar only when something else needs it.",
            Keywords = ["eraser", "toolbar", "button", "pen", "rubber", "erase", "no eraser"],
            Editor = SettingEditorKind.EraserButtonChoice,
            Choices =
            [
                new() { Id = BooleanChoice.Off, Title = "Off" },
                new() { Id = BooleanChoice.On, Title = "On" },
            ],
        },
        new()
        {
            Id = Ids.InsertOnToolbar,
            Category = Toolbar,
            Title = "Insert and Lasso on the toolbar",
            Summary = "An Insert button and a Lasso chevron on the floating toolbar, which grows wider",
            Description = "Off, shapes, connectors, and text come from the Insert tab in the tab strip, and holding the Select button switches what a drag on empty canvas draws. On, the floating toolbar gains an Insert button beside Select whose flyout offers the same things, and a chevron on Select offering Rectangle and Lasso.",
            Keywords = ["insert", "shape", "toolbar", "lasso", "select", "chevron", "flyout", "button"],
            Editor = SettingEditorKind.DrawnChoice,
            Choices = OffOn,
            VisibleWhen = HasDesignTools,
        },
        new()
        {
            Id = Ids.InsertPalette,
            Category = Toolbar,
            Title = "Insert palette",
            Summary = "A second palette with the shapes, connectors, and Text, that you can move",
            Description = "On, a panel holds the eight shapes, the three connectors, and Text where you can reach them while you draw. The grip along its left edge drags it anywhere in the window with a mouse, a pen, or a finger.",
            Keywords = ["insert", "palette", "shape", "connector", "text", "pin", "float", "move", "drag"],
            Editor = SettingEditorKind.DrawnChoice,
            Choices = OffOn,
            VisibleWhen = HasDesignTools,
        },
        new()
        {
            Id = Ids.AfterInsert,
            Category = Toolbar,
            Title = "After inserting an object",
            Summary = "Whether the Insert tool stays for the next drag",
            Description = "Keep the tool leaves the shape, connector, or text tool active, so the next drag draws another of the same thing. Return to Select hands Select back as soon as one object has been drawn, with that object selected, so the next drag moves it rather than starting another.",
            Keywords = ["insert", "shape", "connector", "text", "tool", "sticky", "select", "after"],
            Editor = SettingEditorKind.DrawnChoice,
            Choices =
            [
                new() { Id = nameof(Core.Settings.AfterInsert.KeepTool), Title = "Keep the tool" },
                new() { Id = nameof(Core.Settings.AfterInsert.ReturnToSelect), Title = "Return to Select" },
            ],
            VisibleWhen = HasDesignTools,
        },
        new()
        {
            Id = Ids.CheckForUpdates,
            Category = Updates,
            Title = "Check for new versions",
            Summary = "Ask GitHub once a day whether a newer build exists",
            Description = "Once a day the application asks GitHub whether a newer released build exists. It does not send a machine identifier. Microsoft Store installs are updated by the Store and never make this request.",
            Keywords = ["update", "version", "github", "download", "release"],
            Editor = SettingEditorKind.BooleanSwitch,
            HideInStore = true,
        },
    ];

    public static IReadOnlyList<SettingDescriptor> Filter(
        string? query,
        string? category,
        AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        IEnumerable<SettingDescriptor> items = All
            .Where(setting => setting.VisibleWhen is null || setting.VisibleWhen(settings));
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
               Contains(setting.Summary, term) ||
               Contains(setting.Description, term) ||
               Contains(setting.Category, term) ||
               setting.Keywords.Any(keyword => Contains(keyword, term));
    }

    /// <summary>
    /// Whether a search found this setting only in the prose behind its
    /// disclosure. Such a row marks its chevron, because a hit with nothing
    /// marked on it reads as a fault in the search.
    /// </summary>
    public static bool MatchesDescriptionOnly(SettingDescriptor setting, string? query)
    {
        ArgumentNullException.ThrowIfNull(setting);
        if (string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        var term = query.Trim();
        return Contains(setting.Description, term) &&
               !Contains(setting.Title, term) &&
               !Contains(setting.Summary, term);
    }

    private static bool Contains(string value, string term) =>
        value.Contains(term, StringComparison.OrdinalIgnoreCase);
}
