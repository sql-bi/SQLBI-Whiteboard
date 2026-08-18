using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Document;
using Microsoft.Win32;
using SQLBI.Whiteboard.Core.Commands;
using SQLBI.Whiteboard.Core.Geometry;
using SQLBI.Whiteboard.Core.Import;
using SQLBI.Whiteboard.Core.Model;
using SQLBI.Whiteboard.Core.Persistence;
using SQLBI.Whiteboard.Core.Settings;
using SQLBI.Whiteboard.Core.Viewport;
using SQLBI.Whiteboard.LiveView;
using Windows.Graphics.Capture;

namespace SQLBI.Whiteboard;

public partial class MainWindow : Window
{
    private enum BoardTool
    {
        Pen,
        Highlighter,
        Calligraphy,
        Eraser,
        Select,
        Pan,
        Laser,
    }

    private enum PointerAction
    {
        None,
        Erase,
        Pan,
        Container,
        Laser,
    }

    private readonly Camera2D _camera = new();
    private readonly CommandHistory _history = new();
    private readonly Dictionary<int, Point> _touchPoints = [];
    private readonly Dictionary<int, StylusDevice> _touchDevices = [];
    private readonly List<BoardObject> _erasedObjects = [];
    private readonly Dictionary<PenKind, PenStyle> _styleByKind = new()
    {
        [PenKind.Pen] = InkPalettes.DefaultPen,
        [PenKind.Highlighter] = InkPalettes.DefaultHighlighter,
        [PenKind.Calligraphy] = InkPalettes.DefaultCalligraphy,
    };
    private readonly Dictionary<Guid, LiveViewPresenter> _liveViewPresenters = [];

    private BoardDocument _document = new();
    private string? _currentBoardPath;
    private BoardTool _activeTool = BoardTool.Pen;
    private BoardTool _lastDrawingTool = BoardTool.Pen;
    private BoardTool _toolBeforeSpace = BoardTool.Pen;
    private BoardTool _toolBeforeLaser = BoardTool.Pen;
    private bool _laserTemporary;
    private bool _discardInkStroke;
    private StylusButton? _laserBarrelButton;
    private PenStyle _penStyle = InkPalettes.DefaultPen;
    private PointerAction _stylusAction;
    private PointerAction _mouseAction;
    private PointD _lastPanPoint;
    private bool _penInContact;
    private bool _spaceTemporaryPan;
    private Guid? _selectedObjectId;
    private BoardObject? _containerGestureBefore;
    private BoardObject? _containerGestureCurrent;
    private InkStrokeObject[] _containerGestureLinkedBefore = [];
    private InkStrokeObject[] _containerGestureLinkedCurrent = [];
    private PointD _containerGestureStartWorld;
    private bool _containerGestureIsResize;
    private TextBoardObject? _textEditBefore;
    private InkStrokeObject[] _textEditLinkedBefore = [];
    private RectD _textEditBounds;
    private bool _updatingTextEditor;
    private string _textEditLanguageId = TextLanguageIds.Plain;
    private readonly TextClassificationColorizer _textColorizer = new();
    private readonly DispatcherTimer _textHighlightTimer;
    private CancellationTokenSource? _textAnalysisCancellation;
    private bool _isFullScreen;
    private bool _isToolPaletteHidden;
    private bool _isInkOptionsOpen;
    private bool _isNibPickerOpen;
    private AppSettings _settings = new();

    private const double ChevronInkOptionsWidth = 240;
    private WindowState _windowStateBeforeFullScreen;
    private WindowStyle _windowStyleBeforeFullScreen;
    private ResizeMode _resizeModeBeforeFullScreen;
    private Rect _windowBoundsBeforeFullScreen;
    private readonly string? _initialBoardPath;
    private readonly DispatcherTimer _hoverWatch;
    private long _lastHoverTimestamp;

    public MainWindow()
        : this(null)
    {
    }

    public MainWindow(string? initialBoardPath)
    {
        InitializeComponent();
        Title += AppChannel.WindowTitleSuffix;
        _initialBoardPath = initialBoardPath;
        TextEditorLanguageCombo.ItemsSource = TextLanguageRegistry.All;
        TextEditor.TextArea.TextView.LineTransformers.Add(_textColorizer);
        TextEditor.Options.ConvertTabsToSpaces = true;
        TextEditor.Options.IndentationSize = 4;
        _textHighlightTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(75),
        };
        _textHighlightTimer.Tick += TextHighlightTimer_Tick;
        _hoverWatch = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(50),
        };
        _hoverWatch.Tick += HoverWatch_Tick;
        InkSurface.HoverTracker.Hovered += HoverTracker_Hovered;
        SourceInitialized += MainWindow_SourceInitialized;
        Loaded += MainWindow_Loaded;
        _document.Changed += Document_Changed;
        _history.Changed += History_Changed;
        SceneSurface.Configure(_document, _camera);
        SceneSurface.LiveViewImageSourceProvider = GetLiveViewImageSource;
        InkSurface.Cursor = Cursors.Arrow;
        _settings = AppSettingsStore.Load();
        LoadInkFromSettings();
        ApplyLaserSettings();
        ApplyToolbarPlacement();
        ApplyCalligraphyAccess();
        ApplyDrawingAttributes();
        SetActiveTool(BoardTool.Pen);
        InkSurface.Focus();
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        MonitorStartupPlacement.PlaceMaximized(
            this,
            _settings.StartupMonitor,
            _settings.StartupMonitorName);
        if (_settings.StartFullScreen)
        {
            EnterFullScreen();
        }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;
        if (_initialBoardPath is not null)
        {
            await OpenPathAsync(_initialBoardPath, confirmDiscard: false);
        }
    }

    private void BoardViewport_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        _camera.Resize(e.NewSize.Width, e.NewSize.Height);
        SceneSurface.InvalidateVisual();
        UpdateLiveViewActionOverlay();
        UpdateTextEditorOverlay();
    }

    private void Document_Changed(object? sender, EventArgs e)
    {
        var liveViewIds = _document.Objects.OfType<LiveViewBoardObject>()
            .Select(item => item.Id)
            .ToHashSet();
        foreach (var removedId in _liveViewPresenters.Keys
                     .Where(id => !liveViewIds.Contains(id))
                     .ToArray())
        {
            DisposeLiveViewPresenter(removedId);
        }

        if (_selectedObjectId is Guid selectedId &&
            _document.Objects.All(item => item.Id != selectedId))
        {
            _selectedObjectId = null;
        }

        SceneSurface.SelectedObjectId = _selectedObjectId;
        SceneSurface.InvalidateVisual();
        UpdateLiveViewActionOverlay();
        UpdateTextEditorOverlay();
    }

    private void History_Changed(object? sender, EventArgs e)
    {
        UndoMenuItem.IsEnabled = _history.CanUndo;
        RedoMenuItem.IsEnabled = _history.CanRedo;
    }

    private void InkSurface_StrokeCollected(object sender, InkCanvasStrokeCollectedEventArgs e)
    {
        if (EffectiveTool == BoardTool.Laser ||
            _stylusAction == PointerAction.Laser ||
            _discardInkStroke)
        {
            _discardInkStroke = false;
            InkSurface.Strokes.Remove(e.Stroke);
            return;
        }

        if (e.Stroke.StylusPoints.Count == 0)
        {
            InkSurface.Strokes.Remove(e.Stroke);
            return;
        }

        var firstTimestamp = Stopwatch.GetTimestamp();
        var points = e.Stroke.StylusPoints
            .Select((point, index) => new InkPoint(
                _camera.ScreenToWorld(new PointD(point.X, point.Y)),
                point.PressureFactor,
                firstTimestamp + index))
            .ToArray();
        var stroke = InkStrokeObject.Create(
            points,
            _penStyle,
            _document.NextZIndex);
        if (_document.FindSingleTouchedContainer(stroke) is { } container)
        {
            stroke = stroke with { ContainerId = container.Id };
        }

        _history.Execute(new AddObjectCommand(stroke), _document);

        // The DynamicRenderer owns the low-latency wet stroke. Once WPF has
        // collected it, the same pressure points move into the retained world
        // model and the temporary InkCanvas copy can be discarded.
        InkSurface.Strokes.Remove(e.Stroke);
        SceneSurface.InvalidateVisual();
        Debug.WriteLine(
            $"[WpfInk] committed {points.Length} pressure points to world coordinates");
    }

    private void InkSurface_PreviewStylusDown(object sender, StylusDownEventArgs e)
    {
        CommitTextEdit();

        if (IsTouchStylus(e))
        {
            InkSurface.RegisterTouchTablet(e.StylusDevice.TabletDevice.Id);
            BeginTouchNavigation(e);
            return;
        }

        if (TryForwardCapturedStylusToPalette(e))
        {
            return;
        }

        _penInContact = true;
        ClearTouchNavigation();
        UsePenCursor();
        HidePointerDot();

        if (e.StylusDevice.Inverted || EffectiveTool != BoardTool.Select)
        {
            ClearSelection();
        }

        var screen = ToPointD(e.GetPosition(InkSurface));
        if (e.StylusDevice.Inverted || _activeTool == BoardTool.Eraser)
        {
            BeginErase(screen);
            _stylusAction = PointerAction.Erase;
            InkSurface.CaptureStylus();
            e.Handled = true;
        }
        else if (EffectiveTool == BoardTool.Pan)
        {
            _lastPanPoint = screen;
            _stylusAction = PointerAction.Pan;
            InkSurface.CaptureStylus();
            e.Handled = true;
        }
        else if (EffectiveTool == BoardTool.Select)
        {
            BeginContainerGesture(screen);
            _stylusAction = PointerAction.Container;
            InkSurface.CaptureStylus();
            e.Handled = true;
        }
        else if (EffectiveTool == BoardTool.Laser && !e.StylusDevice.Inverted)
        {
            BeginLaserContact(e);
            _stylusAction = PointerAction.Laser;
            InkSurface.CaptureStylus();
            e.Handled = true;
        }
        else
        {
            _stylusAction = PointerAction.None;
        }

        Debug.WriteLine("[WpfInk] stylus-down reached WPF");
    }

    private void InkSurface_PreviewStylusMove(object sender, StylusEventArgs e)
    {
        if (IsTouchStylus(e))
        {
            InkSurface.RegisterTouchTablet(e.StylusDevice.TabletDevice.Id);
            UpdateTouchNavigation(e);
            return;
        }

        var position = e.GetPosition(InkSurface);
        var screen = ToPointD(position);

        switch (_stylusAction)
        {
            case PointerAction.Erase:
                EraseAt(_camera.ScreenToWorld(screen));
                e.Handled = true;
                break;
            case PointerAction.Pan:
                PanTo(screen);
                e.Handled = true;
                break;
            case PointerAction.Container:
                UpdateContainerGesture(_camera.ScreenToWorld(screen));
                e.Handled = true;
                break;
            case PointerAction.Laser:
                AddLaserSamples(e, leaveTrail: true);
                e.Handled = true;
                break;
        }

        if (e.StylusDevice.Inverted)
        {
            LaserTrail.HideHead();
        }
        else if (EffectiveTool == BoardTool.Laser)
        {
            InkSurface.Cursor = Cursors.None;
            if (_stylusAction == PointerAction.Laser || _penInContact)
            {
                HidePointerDot();
            }
            else
            {
                LaserTrail.HideHead();
                UpdateHoverPointerDot(e);
            }
        }
        else if (EffectiveTool == BoardTool.Select)
        {
            HidePointerDot();
            if (!_penInContact)
            {
                UpdateSelectHover(screen);
                InkSurface.Cursor = SelectCursorAt(screen);
            }
        }
        else if (_penInContact)
        {
            UsePenCursor();
            HidePointerDot();
        }
        else
        {
            UpdateHoverPointerDot(e);
        }
    }

    private void InkSurface_PreviewStylusInAirMove(object sender, StylusEventArgs e) =>
        UpdateHoverPointerDot(e);

    private void InkSurface_PreviewStylusUp(object sender, StylusEventArgs e)
    {
        if (IsTouchStylus(e))
        {
            InkSurface.RegisterTouchTablet(e.StylusDevice.TabletDevice.Id);
            EndTouchNavigation(e);
            return;
        }

        var screen = ToPointD(e.GetPosition(InkSurface));
        switch (_stylusAction)
        {
            case PointerAction.Erase:
                EraseAt(_camera.ScreenToWorld(screen));
                CompleteErase();
                e.Handled = true;
                break;
            case PointerAction.Pan:
                PanTo(screen);
                e.Handled = true;
                break;
            case PointerAction.Container:
                UpdateContainerGesture(_camera.ScreenToWorld(screen));
                CompleteContainerGesture();
                e.Handled = true;
                break;
            case PointerAction.Laser:
                ReleaseBoardPointerCapture(e.StylusDevice);
                LaserTrail.Lift();
                e.Handled = true;
                break;
        }

        if (_stylusAction != PointerAction.None)
        {
            InkSurface.ReleaseStylusCapture();
            Stylus.Capture(null);
            e.StylusDevice.Capture(null);
        }

        _stylusAction = PointerAction.None;
        _penInContact = false;
        if (EffectiveTool == BoardTool.Laser)
        {
            StopLaserSampling();
            LaserTrail.Lift();
            InkSurface.Cursor = Cursors.None;
            if (e.StylusDevice.InAir)
            {
                UpdateHoverPointerDot(e);
            }
            else
            {
                HidePointerDot();
            }
        }
        else if (EffectiveTool == BoardTool.Select)
        {
            var hoverScreen = ToPointD(e.GetPosition(InkSurface));
            UpdateSelectHover(hoverScreen);
            InkSurface.Cursor = SelectCursorAt(hoverScreen);
            HidePointerDot();
        }
        else if (e.StylusDevice.InAir)
        {
            UpdateHoverPointerDot(e);
        }
        else
        {
            HidePointerDot();
        }
        Debug.WriteLine("[WpfInk] stylus-up reached WPF");
    }

    private void InkSurface_StylusEnter(object sender, StylusEventArgs e)
    {
        if (IsTouchStylus(e))
        {
            return;
        }

        if (EffectiveTool == BoardTool.Laser)
        {
            LaserTrail.HideHead();
            InkSurface.Cursor = Cursors.None;
            if (!_penInContact)
            {
                UpdateHoverPointerDot(e);
            }

            return;
        }

        if (EffectiveTool == BoardTool.Select)
        {
            HidePointerDot();
            var screen = ToPointD(e.GetPosition(InkSurface));
            UpdateSelectHover(screen);
            InkSurface.Cursor = SelectCursorAt(screen);
            return;
        }

        UsePenCursor();
        if (!_penInContact)
        {
            UpdateHoverPointerDot(e);
        }
    }

    private void InkSurface_StylusLeave(object sender, StylusEventArgs e)
    {
        if (IsTouchStylus(e))
        {
            return;
        }

        HidePointerDot();
        if (EffectiveTool == BoardTool.Laser || _stylusAction == PointerAction.Laser)
        {
            ReleaseBoardPointerCapture(e.StylusDevice);
            LaserTrail.Lift();
        }
    }

    private void Window_PreviewStylusDown(object sender, StylusDownEventArgs e)
    {
        if (IsTouchStylus(e) || !ShouldRouteStylusToPalette())
        {
            return;
        }

        if (TryActivatePaletteAt(e.GetPosition(ToolPalette), e.StylusDevice))
        {
            e.Handled = true;
        }
    }

    private void ToolPalette_PreviewStylusDown(object sender, StylusDownEventArgs e)
    {
        ReleaseBoardPointerCapture(e.StylusDevice);
        LaserTrail.Lift();
    }

    private bool ShouldRouteStylusToPalette() =>
        EffectiveTool == BoardTool.Laser ||
        _stylusAction == PointerAction.Laser ||
        InkSurface.IsStylusCaptured ||
        Stylus.Captured is not null;

    private bool TryForwardCapturedStylusToPalette(StylusDownEventArgs e)
    {
        if (!ShouldRouteStylusToPalette())
        {
            return false;
        }

        return TryActivatePaletteAt(e.GetPosition(ToolPalette), e.StylusDevice);
    }

    private bool TryActivatePaletteAt(Point palettePoint, StylusDevice? device)
    {
        if (ToolPalette.InputHitTest(palettePoint) is not { } hit)
        {
            return false;
        }

        ReleaseBoardPointerCapture(device);
        LaserTrail.Lift();
        if (FindToggleButton(hit) is { } button)
        {
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }

        return true;
    }

    private static ToggleButton? FindToggleButton(IInputElement? hit)
    {
        var node = hit as DependencyObject;
        while (node is not null)
        {
            if (node is ToggleButton button)
            {
                return button;
            }

            node = node is Visual visual ? VisualTreeHelper.GetParent(visual) : null;
        }

        return null;
    }

    private void ReleaseBoardPointerCapture(StylusDevice? device = null)
    {
        _penInContact = false;
        if (_stylusAction == PointerAction.Laser)
        {
            _stylusAction = PointerAction.None;
        }

        StopLaserSampling();
        if (InkSurface.IsStylusCaptured)
        {
            InkSurface.ReleaseStylusCapture();
        }

        Stylus.Capture(null);
        device?.Capture(null);

        if (Mouse.Captured == InkSurface)
        {
            Mouse.Capture(null);
        }
    }

    private void InkSurface_PreviewStylusButtonDown(object sender, StylusButtonEventArgs e)
    {
        if (IsTouchStylus(e) || e.StylusDevice.Inverted)
        {
            return;
        }

        Debug.WriteLine(
            $"[Laser] stylus button down '{e.StylusButton.Name}' guid={e.StylusButton.Guid} " +
            $"inAir={e.StylusDevice.InAir} contact={_penInContact}");

        if (!IsLowerBarrelButton(e.StylusDevice, e.StylusButton))
        {
            return;
        }

        BeginTemporaryLaser(e.StylusButton);
        if (_penInContact)
        {
            InkSurface.AbortWetInk();
            BeginLaserContact(e);
            _stylusAction = PointerAction.Laser;
        }

        e.Handled = true;
    }

    private void InkSurface_PreviewStylusButtonUp(object sender, StylusButtonEventArgs e)
    {
        if (_laserBarrelButton is null || !ReferenceEquals(e.StylusButton, _laserBarrelButton))
        {
            return;
        }

        EndTemporaryLaser();
        e.Handled = true;
    }

    private void BeginTemporaryLaser(StylusButton button)
    {
        if (_laserTemporary)
        {
            return;
        }

        _laserBarrelButton = button;
        _toolBeforeLaser = _activeTool == BoardTool.Laser
            ? _lastDrawingTool
            : _activeTool;
        _laserTemporary = true;
        _discardInkStroke = _penInContact;
        SetActiveTool(BoardTool.Laser);
        InkSurface.AbortWetInk();
    }

    private void BeginLaserContact(Point position, float pressure)
    {
        HidePointerDot();
        InkSurface.Cursor = Cursors.None;
        StartLaserSampling();
        LaserTrail.BeginOrResumeStroke();
        LaserTrail.AddSample(position, pressure, leaveTrail: true);
    }

    private void BeginLaserContact(StylusEventArgs e)
    {
        HidePointerDot();
        InkSurface.Cursor = Cursors.None;
        StartLaserSampling();
        LaserTrail.BeginOrResumeStroke();
        AddLaserSamples(e, leaveTrail: true);
    }

    private void StartLaserSampling()
    {
        InkSurface.SetLaserMode(true);
        InkSurface.AbortWetInk();
        InkSurface.LaserSamples.Collect = false;
        InkSurface.LaserSamples.Clear();
    }

    private void StopLaserSampling()
    {
        InkSurface.LaserSamples.Collect = false;
        InkSurface.LaserSamples.Clear();
        InkSurface.SetLaserMode(EffectiveTool == BoardTool.Laser);
    }

    private void AddLaserSamples(StylusEventArgs e, bool leaveTrail)
    {
        if (e.StylusDevice.Inverted)
        {
            StopLaserSampling();
            LaserTrail.HideHead();
            return;
        }

        var points = e.GetStylusPoints(LaserTrail);
        if (points.Count == 0)
        {
            LaserTrail.AddSample(e.GetPosition(LaserTrail), 0.5f, leaveTrail);
            return;
        }

        for (var index = 0; index < points.Count; index++)
        {
            var point = points[index];
            LaserTrail.AddSample(
                new Point(point.X, point.Y),
                Math.Clamp(point.PressureFactor, 0.02f, 1f),
                leaveTrail);
        }
    }

    private void UpdateLaser(Point position, bool leaveTrail, float pressure)
    {
        LaserTrail.AddSample(position, pressure, leaveTrail);
    }

    private static bool IsLowerBarrelButton(StylusDevice device, StylusButton button)
    {
        var name = button.Name ?? string.Empty;
        if (name.Contains("Tip", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Eraser", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (name.Contains('2', StringComparison.Ordinal) ||
            name.Contains("Upper", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Secondary", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var extras = device.StylusButtons
            .Cast<StylusButton>()
            .Where(item =>
            {
                var itemName = item.Name ?? string.Empty;
                return !itemName.Contains("Tip", StringComparison.OrdinalIgnoreCase) &&
                       !itemName.Contains("Eraser", StringComparison.OrdinalIgnoreCase);
            })
            .ToArray();

        return extras.Length == 0 || ReferenceEquals(extras[0], button);
    }

    private void EndTemporaryLaser()
    {
        if (!_laserTemporary)
        {
            return;
        }

        _laserTemporary = false;
        _laserBarrelButton = null;
        LaserTrail.HideHead();
        SetActiveTool(_toolBeforeLaser);
    }

    private const float MouseLaserPressure = 0.5f;

    private void InkSurface_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.StylusDevice is not null)
        {
            return;
        }

        CommitTextEdit();
        InkSurface.Focus();
        var screen = ToPointD(e.GetPosition(InkSurface));
        if (e.ChangedButton is MouseButton.Middle or MouseButton.Right)
        {
            _lastPanPoint = screen;
            _mouseAction = PointerAction.Pan;
            Mouse.Capture(InkSurface);
            e.Handled = true;
        }
        else if (e.ChangedButton == MouseButton.Left && e.ClickCount >= 2)
        {
            FrameContentAt(screen);
            e.Handled = true;
        }
        else if (e.ChangedButton == MouseButton.Left && EffectiveTool == BoardTool.Laser)
        {
            BeginLaserContact(e.GetPosition(LaserTrail), MouseLaserPressure);
            _mouseAction = PointerAction.Laser;
            Mouse.Capture(InkSurface);
            e.Handled = true;
        }
        else if (e.ChangedButton == MouseButton.Left)
        {
            SetActiveTool(BoardTool.Select);
            BeginContainerGesture(screen);
            _mouseAction = PointerAction.Container;
            Mouse.Capture(InkSurface);
            e.Handled = true;
        }
        else
        {
            // No physical mouse button is allowed to enter InkCanvas' ink path.
            e.Handled = true;
        }
    }

    private void ToolPalette_PreviewMouseRightButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.StylusDevice is not null)
        {
            return;
        }

        _isToolPaletteHidden = !_isToolPaletteHidden;
        ApplyToolPaletteChrome();
        e.Handled = true;
    }

    private void InkSurface_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        // WPF promotes pen input to mouse events. StylusDevice is non-null for
        // those events, so only a physical mouse can restore the arrow.
        if (e.StylusDevice is not null)
        {
            return;
        }

        if (_penInContact)
        {
            UsePenCursor();
            return;
        }

        HidePointerDot();
        var screen = ToPointD(e.GetPosition(InkSurface));
        if (EffectiveTool == BoardTool.Laser)
        {
            HidePointerDot();
            InkSurface.Cursor = Cursors.None;
            if (_mouseAction == PointerAction.Laser)
            {
                UpdateLaser(e.GetPosition(LaserTrail), leaveTrail: true, MouseLaserPressure);
            }
            else
            {
                LaserTrail.HideHead();
            }
        }
        else if (EffectiveTool == BoardTool.Select && _mouseAction == PointerAction.None)
        {
            UpdateSelectHover(screen);
            InkSurface.Cursor = SelectCursorAt(screen);
        }
        else
        {
            InkSurface.Cursor = Cursors.Arrow;
        }

        switch (_mouseAction)
        {
            case PointerAction.Erase:
                EraseAt(_camera.ScreenToWorld(screen));
                break;
            case PointerAction.Pan:
                PanTo(screen);
                break;
            case PointerAction.Container:
                UpdateContainerGesture(_camera.ScreenToWorld(screen));
                break;
            case PointerAction.Laser:
                UpdateLaser(e.GetPosition(LaserTrail), leaveTrail: true, MouseLaserPressure);
                break;
        }
    }

    private void InkSurface_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.StylusDevice is not null || _mouseAction == PointerAction.None)
        {
            return;
        }

        var screen = ToPointD(e.GetPosition(InkSurface));
        CompleteMouseAction(screen);
        e.Handled = true;
    }

    private void InkSurface_LostMouseCapture(object sender, MouseEventArgs e)
    {
        var hadMouseAction = _mouseAction != PointerAction.None;
        if (_mouseAction == PointerAction.Erase)
        {
            CompleteErase();
        }
        else if (_mouseAction == PointerAction.Container)
        {
            CompleteContainerGesture();
        }

        _mouseAction = PointerAction.None;
        if (hadMouseAction && EffectiveTool != BoardTool.Laser)
        {
            SetActiveTool(_lastDrawingTool);
        }
    }

    private void CompleteMouseAction(PointD screen)
    {
        switch (_mouseAction)
        {
            case PointerAction.Erase:
                EraseAt(_camera.ScreenToWorld(screen));
                CompleteErase();
                break;
            case PointerAction.Pan:
                PanTo(screen);
                break;
            case PointerAction.Container:
                UpdateContainerGesture(_camera.ScreenToWorld(screen));
                CompleteContainerGesture();
                break;
            case PointerAction.Laser:
                StopLaserSampling();
                LaserTrail.Lift();
                break;
        }

        _mouseAction = PointerAction.None;
        if (Mouse.Captured == InkSurface)
        {
            Mouse.Capture(null);
        }

        if (EffectiveTool != BoardTool.Laser)
        {
            SetActiveTool(_lastDrawingTool);
        }
    }

    private void InkSurface_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.StylusDevice is not null)
        {
            return;
        }

        ZoomAtMouseWheel(e);
    }

    private void TextEditorBorder_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        if (e.StylusDevice is not null ||
            (!modifiers.HasFlag(ModifierKeys.Control) &&
             !modifiers.HasFlag(ModifierKeys.Shift)))
        {
            return;
        }

        ZoomAtMouseWheel(e);
    }

    private void ZoomAtMouseWheel(MouseWheelEventArgs e)
    {
        var screen = ToPointD(e.GetPosition(InkSurface));
        var sensitivity = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
            ? 0.0005
            : 0.0015;
        var factor = Math.Pow(1 + sensitivity, e.Delta);
        _camera.ZoomAt(screen, _camera.Zoom * factor);
        CameraChanged();
        e.Handled = true;
    }

    private void BeginTouchNavigation(StylusEventArgs e)
    {
        if (_penInContact)
        {
            e.Handled = true;
            return;
        }

        var id = e.StylusDevice.Id;
        _touchPoints[id] = e.GetPosition(InkSurface);
        _touchDevices[id] = e.StylusDevice;
        e.StylusDevice.Capture(InkSurface);
        e.Handled = true;
    }

    private void UpdateTouchNavigation(StylusEventArgs e)
    {
        var id = e.StylusDevice.Id;
        if (_penInContact || !_touchPoints.ContainsKey(id))
        {
            e.Handled = true;
            return;
        }

        var before = new Dictionary<int, Point>(_touchPoints);
        _touchPoints[id] = e.GetPosition(InkSurface);
        UpdateTouchNavigation(before);
        e.Handled = true;
    }

    private void EndTouchNavigation(StylusEventArgs e)
    {
        var id = e.StylusDevice.Id;
        _touchPoints.Remove(id);
        _touchDevices.Remove(id);
        e.StylusDevice.Capture(null);
        e.Handled = true;
    }

    private void UpdateTouchNavigation(IReadOnlyDictionary<int, Point> before)
    {
        if (_touchPoints.Count == 1)
        {
            var pair = _touchPoints.First();
            if (before.TryGetValue(pair.Key, out var oldPoint))
            {
                _camera.PanByScreenDelta(ToPointD(pair.Value) - ToPointD(oldPoint));
            }
        }
        else if (_touchPoints.Count >= 2)
        {
            var ids = _touchPoints.Keys.OrderBy(id => id).Take(2).ToArray();
            if (before.TryGetValue(ids[0], out var oldFirst) &&
                before.TryGetValue(ids[1], out var oldSecond))
            {
                var newFirst = _touchPoints[ids[0]];
                var newSecond = _touchPoints[ids[1]];
                var oldCenter = Midpoint(oldFirst, oldSecond);
                var newCenter = Midpoint(newFirst, newSecond);
                var oldDistance = Distance(oldFirst, oldSecond);
                var newDistance = Distance(newFirst, newSecond);

                _camera.PanByScreenDelta(ToPointD(newCenter) - ToPointD(oldCenter));
                if (oldDistance > 1 && newDistance > 1)
                {
                    _camera.ZoomAt(
                        ToPointD(newCenter),
                        _camera.Zoom * (newDistance / oldDistance));
                }
            }
        }

        CameraChanged();
    }

    private void ClearTouchNavigation()
    {
        foreach (var device in _touchDevices.Values)
        {
            device.Capture(null);
        }

        _touchPoints.Clear();
        _touchDevices.Clear();
    }

    private void PanTo(PointD screen)
    {
        _camera.PanByScreenDelta(screen - _lastPanPoint);
        _lastPanPoint = screen;
        CameraChanged();
    }

    private void CameraChanged()
    {
        ApplyDrawingAttributes();
        if (IsDualLayout)
        {
            UpdateDualSizeChipZooms();
        }
        else if (_isInkOptionsOpen)
        {
            UpdateSizeChipZooms();
        }

        SceneSurface.InvalidateVisual();
        UpdateLiveViewActionOverlay();
        UpdateTextEditorOverlay();
    }

    private void ClearSelection()
    {
        if (_selectedObjectId is null &&
            SceneSurface.SelectedObjectId is null &&
            SceneSurface.HoveredObjectId is null)
        {
            return;
        }

        _selectedObjectId = null;
        SceneSurface.SelectedObjectId = null;
        SceneSurface.HoveredObjectId = null;
        SceneSurface.InvalidateVisual();
        UpdateLiveViewActionOverlay();
    }

    private void FrameContentAt(PointD screenPoint)
    {
        var container = _document.HitTestTopContainer(_camera.ScreenToWorld(screenPoint));
        if (container is not null)
        {
            _selectedObjectId = container.Id;
            SceneSurface.SelectedObjectId = container.Id;
            _camera.Frame(container.Bounds);
        }
        else
        {
            _selectedObjectId = null;
            SceneSurface.SelectedObjectId = null;
            if (_document.ContentBounds is RectD contentBounds)
            {
                _camera.Frame(contentBounds);
            }
            else
            {
                _camera.Reset();
            }
        }

        CameraChanged();
        SetActiveTool(_lastDrawingTool);
    }

    private void BeginErase(PointD screen)
    {
        _erasedObjects.Clear();
        EraseAt(_camera.ScreenToWorld(screen));
    }

    private void EraseAt(PointD worldPoint)
    {
        var radius = 12 / _camera.Zoom;
        var hits = _document.Objects
            .OfType<InkStrokeObject>()
            .Where(stroke => _erasedObjects.All(item => item.Id != stroke.Id))
            .Where(stroke => stroke.HitTest(worldPoint, radius))
            .ToArray();

        foreach (var stroke in hits)
        {
            _erasedObjects.Add(stroke);
            _document.RemoveObject(stroke.Id);
            if (_selectedObjectId == stroke.Id)
            {
                _selectedObjectId = null;
            }
        }
    }

    private void CompleteErase()
    {
        if (_erasedObjects.Count > 0)
        {
            _history.RecordExecuted(new RemoveObjectsCommand(_erasedObjects.ToArray()));
        }

        _erasedObjects.Clear();
    }

    private void BeginContainerGesture(PointD screenPoint)
    {
        var worldPoint = _camera.ScreenToWorld(screenPoint);
        var selected = _document.HitTestTopContainer(worldPoint);
        _containerGestureIsResize = false;

        if (_selectedObjectId is Guid existingId &&
            _document.Objects.FirstOrDefault(item => item.Id == existingId) is { } existing &&
            existing is IBoardContainer)
        {
            var handle = _camera.WorldToScreen(
                new PointD(existing.Bounds.Right, existing.Bounds.Bottom));
            if (Distance(ToPoint(handle), ToPoint(screenPoint)) <= 16)
            {
                selected = existing;
                _containerGestureIsResize = true;
            }
        }

        _selectedObjectId = selected?.Id;
        SceneSurface.SelectedObjectId = _selectedObjectId;
        if (selected is null)
        {
            ResetContainerGesture();
            SceneSurface.InvalidateVisual();
            UpdateLiveViewActionOverlay();
            return;
        }

        _containerGestureBefore = selected;
        _containerGestureCurrent = selected;
        _containerGestureLinkedBefore = _document.LinkedStrokes(selected.Id).ToArray();
        _containerGestureLinkedCurrent = _containerGestureLinkedBefore;
        _containerGestureStartWorld = worldPoint;
        SceneSurface.InvalidateVisual();
        UpdateLiveViewActionOverlay();
    }

    private void UpdateContainerGesture(PointD worldPoint)
    {
        if (_containerGestureBefore is null)
        {
            return;
        }

        var bounds = _containerGestureBefore.Bounds;
        if (_containerGestureIsResize)
        {
            var minimumWorldSize = 32 / _camera.Zoom;
            var requestedWidth = worldPoint.X - bounds.Left;
            var requestedHeight = worldPoint.Y - bounds.Top;
            var diagonalSquared =
                (bounds.Width * bounds.Width) +
                (bounds.Height * bounds.Height);
            var requestedScale =
                ((requestedWidth * bounds.Width) +
                 (requestedHeight * bounds.Height)) /
                diagonalSquared;
            var minimumScale = Math.Max(
                minimumWorldSize / bounds.Width,
                minimumWorldSize / bounds.Height);
            var scale = Math.Max(minimumScale, requestedScale);
            bounds = bounds.WithSize(
                bounds.Width * scale,
                bounds.Height * scale);
        }
        else
        {
            bounds = bounds.Translate(worldPoint - _containerGestureStartWorld);
        }

        _containerGestureCurrent = WithBounds(_containerGestureBefore, bounds);
        _containerGestureLinkedCurrent = _containerGestureLinkedBefore
            .Select(stroke => stroke.TransformWithContainer(_containerGestureBefore.Bounds, bounds))
            .ToArray();
        BoardObject[] replacements =
            [_containerGestureCurrent, .. _containerGestureLinkedCurrent];
        _document.ReplaceObjects(replacements);
    }

    private void CompleteContainerGesture()
    {
        if (_containerGestureBefore is not null &&
            _containerGestureCurrent is not null &&
            _containerGestureBefore.Bounds != _containerGestureCurrent.Bounds)
        {
            BoardObject[] before =
                [_containerGestureBefore, .. _containerGestureLinkedBefore];
            BoardObject[] after =
                [_containerGestureCurrent, .. _containerGestureLinkedCurrent];
            _history.RecordExecuted(new ReplaceObjectsCommand(before, after));
        }

        ResetContainerGesture();
    }

    private void ResetContainerGesture()
    {
        _containerGestureBefore = null;
        _containerGestureCurrent = null;
        _containerGestureLinkedBefore = [];
        _containerGestureLinkedCurrent = [];
        _containerGestureIsResize = false;
    }

    private static BoardObject WithBounds(BoardObject item, RectD bounds) => item switch
    {
        ImageBoardObject image => image with { Bounds = bounds },
        TextBoardObject text => text with
        {
            Bounds = bounds,
            VisualScale = text.VisualScale * (bounds.Width / Math.Max(0.000001, text.Bounds.Width)),
        },
        LiveViewBoardObject liveView => liveView with { Bounds = bounds },
        _ => throw new NotSupportedException($"Unsupported container type {item.GetType().Name}."),
    };

    private void BeginTextEdit(TextBoardObject textObject)
    {
        if (_textEditBefore?.Id == textObject.Id)
        {
            TextEditor.Focus();
            return;
        }

        CommitTextEdit();
        ResetContainerGesture();
        _textEditBefore = textObject;
        _textEditLinkedBefore = _document.LinkedStrokes(textObject.Id).ToArray();
        _textEditBounds = textObject.Bounds;
        _selectedObjectId = textObject.Id;
        SceneSurface.SelectedObjectId = null;
        SceneSurface.HoveredObjectId = null;
        SceneSurface.HiddenObjectId = textObject.Id;

        _updatingTextEditor = true;
        _textEditLanguageId = TextLanguageIds.Normalize(textObject.LanguageId);
        ITextLanguageService language = TextLanguageRegistry.Resolve(_textEditLanguageId);
        TextEditor.Document = new TextDocument(textObject.Text);
        TextEditor.CaretOffset = TextEditor.Text.Length;
        ApplyTextEditorLanguage(language, updateCombo: true);
        RequestTextEditorAnalysis();
        _updatingTextEditor = false;
        EnsureTextEditFits();

        UpdateLiveViewActionOverlay();
        UpdateTextEditorOverlay();
        UpdateTextEditHistoryMenuState();
        SceneSurface.InvalidateVisual();
        _ = Dispatcher.InvokeAsync(
            () =>
            {
                TextEditor.Focus();
                Keyboard.Focus(TextEditor);
                TextEditor.CaretOffset = TextEditor.Text.Length;
            },
            DispatcherPriority.Input);
    }

    private void CommitTextEdit()
    {
        if (_textEditBefore is not { } before)
        {
            return;
        }

        InkStrokeObject[] linkedBefore = _textEditLinkedBefore;
        RectD afterBounds = _textEditBounds;
        var after = before with
        {
            Bounds = afterBounds,
            Text = TextEditor.Text,
            LanguageId = _textEditLanguageId,
        };
        InkStrokeObject[] linkedAfter = before.Bounds == afterBounds
            ? linkedBefore
            : linkedBefore
                .Select(stroke => stroke.TransformWithContainer(before.Bounds, afterBounds))
                .ToArray();

        EndTextEditVisual(before.Id);
        if (before == after)
        {
            return;
        }

        BoardObject[] beforeItems = [before, .. linkedBefore];
        BoardObject[] afterItems = [after, .. linkedAfter];
        _history.Execute(new ReplaceObjectsCommand(beforeItems, afterItems), _document);
    }

    private void CancelTextEdit()
    {
        if (_textEditBefore is not { } before)
        {
            return;
        }

        EndTextEditVisual(before.Id);
    }

    private void EndTextEditVisual(Guid? selectedObjectId)
    {
        _textEditBefore = null;
        _textEditLinkedBefore = [];
        _textEditBounds = default;
        _updatingTextEditor = false;
        _textHighlightTimer.Stop();
        CancelTextEditorAnalysis();
        _textEditLanguageId = TextLanguageIds.Plain;
        _textColorizer.Update([], new FontFamily("Segoe UI"));
        TextEditorBorder.Visibility = Visibility.Collapsed;
        SceneSurface.HiddenObjectId = null;
        _selectedObjectId = selectedObjectId;
        SceneSurface.SelectedObjectId = selectedObjectId;
        SceneSurface.InvalidateVisual();
        UpdateLiveViewActionOverlay();
        History_Changed(this, EventArgs.Empty);
        InkSurface.Focus();
    }

    private void TextEditor_TextChanged(object? sender, EventArgs e)
    {
        if (_updatingTextEditor || _textEditBefore is null)
        {
            return;
        }

        EnsureTextEditFits();
        UpdateTextEditorOverlay();
        UpdateTextEditHistoryMenuState();
        _textHighlightTimer.Stop();
        _textHighlightTimer.Start();
    }

    private void TextEditorLanguageCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_updatingTextEditor ||
            _textEditBefore is null ||
            TextEditorLanguageCombo.SelectedItem is not ITextLanguageService language)
        {
            return;
        }

        _textEditLanguageId = language.Id;
        ApplyTextEditorLanguage(language, updateCombo: false);
        EnsureTextEditFits();
        UpdateTextEditorOverlay();
        RequestTextEditorAnalysis();
        TextEditor.Focus();
    }

    private void TextHighlightTimer_Tick(object? sender, EventArgs e)
    {
        _textHighlightTimer.Stop();
        RequestTextEditorAnalysis();
    }

    private void ApplyTextEditorLanguage(
        ITextLanguageService language,
        bool updateCombo)
    {
        TextEditor.FontFamily = new FontFamily(language.FontFamilyName);
        TextEditor.WordWrap = language.WordWrap;
        TextEditor.ShowLineNumbers = language.ShowLineNumbers;
        TextEditor.HorizontalScrollBarVisibility = language.WordWrap
            ? ScrollBarVisibility.Disabled
            : ScrollBarVisibility.Auto;
        if (updateCombo)
        {
            TextEditorLanguageCombo.SelectedItem = language;
        }
    }

    private async void RequestTextEditorAnalysis()
    {
        if (_textEditBefore is not { } textObject)
        {
            return;
        }

        CancelTextEditorAnalysis();
        var cancellation = new CancellationTokenSource();
        _textAnalysisCancellation = cancellation;
        ITextLanguageService language = TextLanguageRegistry.Resolve(_textEditLanguageId);
        string source = TextEditor.Text;
        string languageId = _textEditLanguageId;
        try
        {
            TextLanguageAnalysis analysis = language.UseBackgroundAnalysis
                ? await Task.Run(
                    () => language.Analyze(source, textObject.Title),
                    cancellation.Token)
                : language.Analyze(source, textObject.Title);
            if (cancellation.IsCancellationRequested ||
                _textEditBefore?.Id != textObject.Id ||
                _textEditLanguageId != languageId ||
                !string.Equals(TextEditor.Text, source, StringComparison.Ordinal))
            {
                return;
            }

            TextEditorTitle.Text = analysis.Title;
            _textColorizer.Update(
                analysis.Spans,
                new FontFamily(language.FontFamilyName));
            TextEditor.TextArea.TextView.Redraw();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[TextEditor] Language analysis failed: {exception.Message}");
        }
        finally
        {
            if (ReferenceEquals(_textAnalysisCancellation, cancellation))
            {
                _textAnalysisCancellation = null;
                cancellation.Dispose();
            }
        }
    }

    private void CancelTextEditorAnalysis()
    {
        CancellationTokenSource? cancellation = _textAnalysisCancellation;
        _textAnalysisCancellation = null;
        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        cancellation.Dispose();
    }

    private void FormatTextEdit()
    {
        if (_textEditBefore is null)
        {
            return;
        }

        ITextLanguageService language = TextLanguageRegistry.Resolve(_textEditLanguageId);
        if (!language.CanFormat ||
            !language.TryFormat(TextEditor.Text, out string formatted) ||
            string.Equals(TextEditor.Text, formatted, StringComparison.Ordinal))
        {
            return;
        }

        int caretOffset = TextEditor.CaretOffset;
        using (TextEditor.Document.RunUpdate())
        {
            TextEditor.Document.Replace(0, TextEditor.Document.TextLength, formatted);
        }

        TextEditor.CaretOffset = Math.Min(caretOffset, formatted.Length);
        EnsureTextEditFits();
        UpdateTextEditorOverlay();
        UpdateTextEditHistoryMenuState();
        _textHighlightTimer.Stop();
        RequestTextEditorAnalysis();
    }

    private void TextEditorResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_textEditBefore is not { } textObject)
        {
            return;
        }

        double minimumWidth = TextContainerVisual.MinimumWidth * textObject.VisualScale;
        double requestedWidth = Math.Max(
            minimumWidth,
            _textEditBounds.Width + (e.HorizontalChange / _camera.Zoom));
        double requestedHeight = Math.Max(
            TextContainerVisual.MeasureDesiredHeight(
                string.Empty,
                requestedWidth,
                textObject.VisualScale,
                VisualTreeHelper.GetDpi(SceneSurface).PixelsPerDip,
                _textEditLanguageId),
            _textEditBounds.Height + (e.VerticalChange / _camera.Zoom));
        _textEditBounds = _textEditBounds.WithSize(requestedWidth, requestedHeight);
        EnsureTextEditFits();
        UpdateTextEditorOverlay();
    }

    private void EnsureTextEditFits()
    {
        if (_textEditBefore is not { } textObject)
        {
            return;
        }

        double desiredHeight = TextContainerVisual.MeasureDesiredHeight(
            TextEditor.Text,
            _textEditBounds.Width,
            textObject.VisualScale,
            VisualTreeHelper.GetDpi(SceneSurface).PixelsPerDip,
            _textEditLanguageId);
        if (desiredHeight > _textEditBounds.Height)
        {
            _textEditBounds = _textEditBounds.WithSize(_textEditBounds.Width, desiredHeight);
        }
    }

    private void UpdateTextEditorOverlay()
    {
        if (_textEditBefore is not { } textObject)
        {
            TextEditorBorder.Visibility = Visibility.Collapsed;
            return;
        }

        PointD topLeft = _camera.WorldToScreen(
            new PointD(_textEditBounds.Left, _textEditBounds.Top));
        PointD bottomRight = _camera.WorldToScreen(
            new PointD(_textEditBounds.Right, _textEditBounds.Bottom));
        double width = Math.Max(1, bottomRight.X - topLeft.X);
        double height = Math.Max(1, bottomRight.Y - topLeft.Y);
        double scale = Math.Max(0.01, textObject.VisualScale * _camera.Zoom);

        Canvas.SetLeft(TextEditorBorder, topLeft.X);
        Canvas.SetTop(TextEditorBorder, topLeft.Y);
        TextEditorBorder.Width = width;
        TextEditorBorder.Height = height;
        TextEditorBorder.BorderThickness = new Thickness(
            Math.Max(0.5, TextContainerVisual.BorderThickness * scale));
        TextEditorTitleRow.Height = new GridLength(TextContainerVisual.TitleBarHeight * scale);
        TextEditorTitle.Margin = new Thickness(TextContainerVisual.ContentPadding * scale, 0, 0, 0);
        TextEditorTitle.FontSize = Math.Max(1, TextContainerVisual.TitleFontSize * scale);
        TextEditorLanguageCombo.Width = Math.Max(72, 110 * scale);
        TextEditorLanguageCombo.Height = Math.Max(18, 22 * scale);
        TextEditorLanguageCombo.Margin = new Thickness(0, 0, 6 * scale, 0);
        TextEditorLanguageCombo.FontSize = Math.Max(1, 12 * scale);
        TextEditorBodyBorder.Padding = new Thickness(TextContainerVisual.ContentPadding * scale);
        TextEditor.FontSize = Math.Max(1, TextContainerVisual.BodyFontSize * scale);
        TextEditorBorder.Visibility = Visibility.Visible;
    }

    private void UpdateTextEditHistoryMenuState()
    {
        if (_textEditBefore is null)
        {
            return;
        }

        UndoMenuItem.IsEnabled = TextEditor.CanUndo;
        RedoMenuItem.IsEnabled = TextEditor.CanRedo;
    }

    private void PenToolButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem)
        {
            SetActiveTool(BoardTool.Pen);
            return;
        }

        if (_activeTool is BoardTool.Pen or BoardTool.Calligraphy)
        {
            ToggleInkOptions();
            SetActiveTool(_activeTool);
            return;
        }

        SetActiveTool(
            _lastDrawingTool == BoardTool.Calligraphy
                ? BoardTool.Calligraphy
                : BoardTool.Pen);
    }

    private void HighlighterToolButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem)
        {
            SetActiveTool(BoardTool.Highlighter);
            return;
        }

        if (_activeTool == BoardTool.Highlighter)
        {
            ToggleInkOptions();
            SetActiveTool(BoardTool.Highlighter);
            return;
        }

        SetActiveTool(BoardTool.Highlighter);
    }

    private void CalligraphyToolButton_Click(object sender, RoutedEventArgs e) =>
        SetActiveTool(BoardTool.Calligraphy);

    private void PenNibButton_Click(object sender, RoutedEventArgs e)
    {
        SetNibPickerOpen(false);
        SetActiveTool(BoardTool.Pen);
    }

    private void CalligraphyNibButton_Click(object sender, RoutedEventArgs e)
    {
        SetNibPickerOpen(false);
        SetActiveTool(BoardTool.Calligraphy);
    }

    private void PenChevronButton_Click(object sender, RoutedEventArgs e) =>
        SetNibPickerOpen(!_isNibPickerOpen);

    private void EraserToolButton_Click(object sender, RoutedEventArgs e) =>
        SetActiveTool(BoardTool.Eraser);

    private void SelectToolButton_Click(object sender, RoutedEventArgs e)
    {
        LeaveLaserIfActive();
        SetActiveTool(BoardTool.Select);
    }

    private void PanToolButton_Click(object sender, RoutedEventArgs e) =>
        SetActiveTool(BoardTool.Pan);

    private BoardTool EffectiveTool =>
        _spaceTemporaryPan
            ? BoardTool.Pan
            : _laserTemporary
                ? BoardTool.Laser
                : _activeTool;

    private void LaserToolButton_Click(object sender, RoutedEventArgs e) =>
        SetActiveTool(BoardTool.Laser);

    private bool IsDualLayout =>
        _settings.CalligraphyAccess == CalligraphyAccess.DualPalette;

    private void DualPenButton_Click(object sender, RoutedEventArgs e)
    {
        LeaveLaserIfActive();
        SetActiveTool(BoardTool.Pen);
    }

    private void DualCalligraphyButton_Click(object sender, RoutedEventArgs e)
    {
        LeaveLaserIfActive();
        SetActiveTool(BoardTool.Calligraphy);
    }

    private void DualHighlighterButton_Click(object sender, RoutedEventArgs e)
    {
        LeaveLaserIfActive();
        SetActiveTool(BoardTool.Highlighter);
    }

    private void SetActiveTool(BoardTool tool)
    {
        CommitTextEdit();
        _activeTool = tool;
        if (tool is BoardTool.Pen or BoardTool.Highlighter or BoardTool.Calligraphy)
        {
            _lastDrawingTool = tool;
            _penStyle = _styleByKind[ToPenKind(tool)];
        }
        else
        {
            SceneSurface.HoveredObjectId = null;
        }

        if (tool is not (BoardTool.Pen or BoardTool.Calligraphy))
        {
            SetNibPickerOpen(false);
        }

        if (tool == BoardTool.Select)
        {
            HidePointerDot();
            InkSurface.Cursor = Cursors.Arrow;
        }

        if (tool != BoardTool.Laser)
        {
            InkSurface.SetLaserMode(false);
            StopLaserSampling();
            LaserTrail.HideHead();
        }
        else
        {
            InkSurface.SetLaserMode(true);
            InkSurface.Cursor = Cursors.None;
        }

        var penFamilyActive = tool is BoardTool.Pen or BoardTool.Calligraphy;
        PenToolButton.IsChecked = penFamilyActive;
        HighlighterToolButton.IsChecked = tool == BoardTool.Highlighter;
        SelectToolButton.IsChecked = tool == BoardTool.Select;
        UpdateDualToolChecks();
        PenToolMenuItem.IsChecked = tool == BoardTool.Pen;
        HighlighterToolMenuItem.IsChecked = tool == BoardTool.Highlighter;
        CalligraphyToolMenuItem.IsChecked = tool == BoardTool.Calligraphy;
        LaserToolMenuItem.IsChecked = tool == BoardTool.Laser;
        InkSurface.EditingMode =
            tool is BoardTool.Pen or BoardTool.Highlighter or BoardTool.Calligraphy or BoardTool.Laser
            ? InkCanvasEditingMode.Ink
            : InkCanvasEditingMode.None;
        InkSurface.EditingModeInverted = InkCanvasEditingMode.None;
        UpdatePenButtonGlyph();
        ApplyDrawingAttributes();
        if (IsDualLayout)
        {
            SyncDualPaletteSelection();
        }
        else if (_isInkOptionsOpen)
        {
            RebuildInkOptions();
        }

        InkSurface.Focus();
    }

    private void ApplyDrawingAttributes()
    {
        if (InkSurface is null)
        {
            return;
        }

        var attributes = InkDrawingAttributes.Create(_penStyle, _camera.Zoom);
        if (EffectiveTool == BoardTool.Laser)
        {
            attributes.Color = Colors.Transparent;
            InkSurface.SetLaserMode(true);
        }

        InkSurface.DefaultDrawingAttributes = attributes;
        InkSurface.SetPenKind(_penStyle.Kind);
        UpdateColorPips();
    }

    private void ToggleInkOptions() => SetInkOptionsOpen(!_isInkOptionsOpen);

    private void SetInkOptionsOpen(bool open)
    {
        _isInkOptionsOpen = open;
        if (InkOptionsPanel is not null)
        {
            InkOptionsPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        }

        if (open)
        {
            RebuildInkOptions();
        }
    }

    private void LoadInkFromSettings()
    {
        _styleByKind[PenKind.Pen] = _settings.Pen.ToStyle(PenKind.Pen);
        _styleByKind[PenKind.Highlighter] = _settings.Highlighter.ToStyle(PenKind.Highlighter);
        _styleByKind[PenKind.Calligraphy] = _settings.Calligraphy.ToStyle(PenKind.Calligraphy);
        _penStyle = _styleByKind[PenKind.Pen];
        UpdateColorPips();
    }

    private void ApplyLaserSettings()
    {
        var laser = LaserSettings.Normalize(_settings.Laser);
        _settings.Laser = laser;
        LaserTrail.HoldSeconds = laser.HoldSeconds;
        LaserTrail.FadeSeconds = laser.FadeSeconds;
        LaserTrail.HoldMode = laser.HoldMode;
    }

    private void CommitInkStyle(PenStyle style)
    {
        _penStyle = style;
        _styleByKind[style.Kind] = style;
        _settings.Pen = InkToolSettings.From(_styleByKind[PenKind.Pen]);
        _settings.Highlighter = InkToolSettings.From(_styleByKind[PenKind.Highlighter]);
        _settings.Calligraphy = InkToolSettings.From(_styleByKind[PenKind.Calligraphy]);
        PersistSettings();
        ApplyDrawingAttributes();
        if (IsDualLayout)
        {
            SyncDualPaletteSelection();
        }
        else if (_isInkOptionsOpen)
        {
            RebuildInkOptions();
        }
    }

    private void RebuildInkOptions()
    {
        if (ColorSwatchHost is null || SizeChipHost is null)
        {
            return;
        }

        ColorSwatchHost.Children.Clear();
        foreach (var swatch in InkPalettes.ColorsFor(_penStyle.Kind))
        {
            var button = new ToggleButton
            {
                Style = (Style)FindResource("ColorSwatchButton"),
                Background = ToFrozenBrush(swatch.Argb),
                ToolTip = swatch.Name,
                Tag = swatch.Argb,
                IsChecked = swatch.Argb == _penStyle.Argb,
            };
            button.Click += ColorSwatch_Click;
            ColorSwatchHost.Children.Add(button);
        }

        SizeChipHost.Children.Clear();
        foreach (var thickness in InkPalettes.ThicknessesFor(_penStyle.Kind))
        {
            var preview = new StrokePreview
            {
                Width = 48,
                Height = 32,
                PenStyle = _penStyle with { Thickness = thickness },
                Zoom = PreviewZoom(),
            };
            var button = new ToggleButton
            {
                Style = (Style)FindResource("SizeChipButton"),
                Content = preview,
                Tag = thickness,
                IsChecked = thickness == _penStyle.Thickness,
                ToolTip = $"Size {thickness:0}",
            };
            button.Click += SizeChip_Click;
            SizeChipHost.Children.Add(button);
        }

        if (_settings.CalligraphyAccess == CalligraphyAccess.SizeRow &&
            _penStyle.Kind is PenKind.Pen or PenKind.Calligraphy)
        {
            SizeChipHost.Children.Add(new System.Windows.Shapes.Rectangle
            {
                Width = 1,
                Height = 20,
                Margin = new Thickness(6, 0, 4, 0),
                Fill = (Brush)FindResource("ToolbarSeparatorBrush"),
                VerticalAlignment = VerticalAlignment.Center,
            });
            SizeChipHost.Children.Add(CreateNibButton(
                PenKind.Pen,
                (Geometry)FindResource("InkingToolGeometry"),
                "Pen"));
            SizeChipHost.Children.Add(CreateNibButton(
                PenKind.Calligraphy,
                (Geometry)FindResource("CalligraphyPenGeometry"),
                "Calligraphy"));
        }

        ApplyInkOptionsWidth();
        UpdateNibPickerChecks();
        UpdatePenButtonGlyph();
        UpdateColorPips();
    }

    private void SetNibPickerOpen(bool open)
    {
        _isNibPickerOpen = open && _settings.CalligraphyAccess == CalligraphyAccess.Chevron;
        if (NibPickerPanel is not null)
        {
            NibPickerPanel.Visibility = _isNibPickerOpen
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        if (PenChevronButton is not null)
        {
            PenChevronButton.IsChecked = _isNibPickerOpen;
        }

        UpdateNibPickerChecks();
    }

    private void UpdateNibPickerChecks()
    {
        if (PickerPenButton is null || PickerCalligraphyButton is null)
        {
            return;
        }

        PickerPenButton.IsChecked = _activeTool == BoardTool.Pen;
        PickerCalligraphyButton.IsChecked = _activeTool == BoardTool.Calligraphy;
    }

    private void SetCalligraphyAccess(CalligraphyAccess access)
    {
        _settings.CalligraphyAccess = access;
        ApplyCalligraphyAccess();
        PersistSettings();
    }

    private void ApplyCalligraphyAccess()
    {
        var dual = IsDualLayout;
        var useChevron = _settings.CalligraphyAccess == CalligraphyAccess.Chevron;
        if (DualPalettePanel is not null)
        {
            DualPalettePanel.Visibility = dual ? Visibility.Visible : Visibility.Collapsed;
        }

        if (CompactToolbarHost is not null)
        {
            CompactToolbarHost.Visibility = dual ? Visibility.Collapsed : Visibility.Visible;
        }

        if (PenChevronButton is not null)
        {
            PenChevronButton.Visibility = useChevron ? Visibility.Visible : Visibility.Collapsed;
        }

        if (!useChevron)
        {
            SetNibPickerOpen(false);
        }

        if (dual)
        {
            SetInkOptionsOpen(false);
            RebuildDualPalette();
            return;
        }

        ApplyInkOptionsWidth();
        if (_isInkOptionsOpen)
        {
            RebuildInkOptions();
        }
    }

    private void ApplyInkOptionsWidth()
    {
        if (InkOptionsPanel is null)
        {
            return;
        }

        if (_settings.CalligraphyAccess == CalligraphyAccess.Chevron)
        {
            InkOptionsPanel.MinWidth = ChevronInkOptionsWidth;
            InkOptionsPanel.Width = ChevronInkOptionsWidth;
        }
        else
        {
            InkOptionsPanel.MinWidth = 0;
            InkOptionsPanel.Width = double.NaN;
        }
    }

    private ToggleButton CreateNibButton(PenKind kind, Geometry geometry, string tooltip)
    {
        var icon = new System.Windows.Shapes.Path
        {
            Width = 20,
            Height = 20,
            Stretch = Stretch.Uniform,
            Fill = (Brush)FindResource("ToolbarIconBrush"),
            Data = geometry,
        };
        var button = new ToggleButton
        {
            Style = (Style)FindResource("ToolbarIconButton"),
            Content = icon,
            ToolTip = tooltip,
            IsChecked = _penStyle.Kind == kind,
        };
        button.Click += kind == PenKind.Calligraphy
            ? CalligraphyNibButton_Click
            : PenNibButton_Click;
        return button;
    }

    private void RebuildDualPalette()
    {
        if (DualPenColors is null || DualHighlighterColors is null)
        {
            return;
        }

        var penFamily = CurrentPenFamilyStyle();
        FillSwatches(DualPenColors, penFamily, argb => ApplyPenGroupChoice(argb, thickness: null));
        FillSizes(DualPenSizes, penFamily, thickness => ApplyPenGroupChoice(argb: null, thickness));

        var highlighter = _styleByKind[PenKind.Highlighter];
        FillSwatches(
            DualHighlighterColors,
            highlighter,
            argb => ApplyHighlighterGroupChoice(argb, thickness: null));
        FillSizes(
            DualHighlighterSizes,
            highlighter,
            thickness => ApplyHighlighterGroupChoice(argb: null, thickness));

        UpdateDualToolChecks();
    }

    private void SyncDualPaletteSelection()
    {
        if (DualPenColors is null || DualHighlighterColors is null)
        {
            return;
        }

        var penFamily = CurrentPenFamilyStyle();
        SyncSwatchChecks(DualPenColors, penFamily.Argb);
        SyncSizeChecks(DualPenSizes, penFamily);
        var highlighter = _styleByKind[PenKind.Highlighter];
        SyncSwatchChecks(DualHighlighterColors, highlighter.Argb);
        SyncSizeChecks(DualHighlighterSizes, highlighter);
        UpdateDualToolChecks();
        UpdateDualSizeChipZooms();
    }

    private static void SyncSwatchChecks(Panel? host, uint argb)
    {
        if (host is null)
        {
            return;
        }

        foreach (var button in host.Children.OfType<ToggleButton>())
        {
            button.IsChecked = button.Tag is uint value && value == argb;
        }
    }

    private static void SyncSizeChecks(Panel? host, PenStyle style)
    {
        if (host is null)
        {
            return;
        }

        foreach (var button in host.Children.OfType<ToggleButton>())
        {
            var selected = button.Tag is double thickness && thickness == style.Thickness;
            button.IsChecked = selected;
            if (button.Content is StrokePreview preview && button.Tag is double size)
            {
                preview.PenStyle = style with { Thickness = size };
            }
        }
    }

    private void FillSwatches(
        Panel host,
        PenStyle style,
        Action<uint> onColor)
    {
        host.Children.Clear();
        foreach (var swatch in InkPalettes.ColorsFor(style.Kind))
        {
            var button = new ToggleButton
            {
                Style = (Style)FindResource("ColorSwatchButton"),
                Background = ToFrozenBrush(swatch.Argb),
                ToolTip = swatch.Name,
                Tag = swatch.Argb,
                IsChecked = swatch.Argb == style.Argb,
            };
            button.Click += (_, _) => onColor(swatch.Argb);
            host.Children.Add(button);
        }
    }

    private void FillSizes(
        Panel host,
        PenStyle style,
        Action<double> onThickness)
    {
        host.Children.Clear();
        foreach (var thickness in InkPalettes.ThicknessesFor(style.Kind))
        {
            var preview = new StrokePreview
            {
                Width = 48,
                Height = 32,
                PenStyle = style with { Thickness = thickness },
                Zoom = PreviewZoom(style),
            };
            var button = new ToggleButton
            {
                Style = (Style)FindResource("SizeChipButton"),
                Content = preview,
                Tag = thickness,
                IsChecked = thickness == style.Thickness,
                ToolTip = $"Size {thickness:0}",
            };
            button.Click += (_, _) => onThickness(thickness);
            host.Children.Add(button);
        }
    }

    private void ApplyPenGroupChoice(uint? argb, double? thickness)
    {
        var tool = _activeTool is BoardTool.Pen or BoardTool.Calligraphy
            ? _activeTool
            : _lastDrawingTool == BoardTool.Calligraphy
                ? BoardTool.Calligraphy
                : BoardTool.Pen;
        var style = _styleByKind[ToPenKind(tool)];
        if (argb is uint color)
        {
            style = style with { Argb = color };
        }

        if (thickness is double value)
        {
            style = style with { Thickness = value };
        }

        LeaveLaserIfActive();
        SetActiveTool(tool);
        CommitInkStyle(style);
        InkSurface.Focus();
    }

    private void ApplyHighlighterGroupChoice(uint? argb, double? thickness)
    {
        var style = _styleByKind[PenKind.Highlighter];
        if (argb is uint color)
        {
            style = style with { Argb = color };
        }

        if (thickness is double value)
        {
            style = style with { Thickness = value };
        }

        LeaveLaserIfActive();
        SetActiveTool(BoardTool.Highlighter);
        CommitInkStyle(style);
        InkSurface.Focus();
    }

    private void LeaveLaserIfActive()
    {
        if (_laserTemporary)
        {
            _laserTemporary = false;
            _laserBarrelButton = null;
        }

        if (_activeTool == BoardTool.Laser)
        {
            StopLaserSampling();
            LaserTrail.HideHead();
        }
    }

    private PenStyle CurrentPenFamilyStyle()
    {
        var tool = _activeTool is BoardTool.Pen or BoardTool.Calligraphy
            ? _activeTool
            : _lastDrawingTool == BoardTool.Calligraphy
                ? BoardTool.Calligraphy
                : BoardTool.Pen;
        return _styleByKind[ToPenKind(tool)];
    }

    private void UpdateDualToolChecks()
    {
        if (DualPenButton is null)
        {
            return;
        }

        DualPenButton.IsChecked = _activeTool == BoardTool.Pen;
        DualCalligraphyButton.IsChecked = _activeTool == BoardTool.Calligraphy;
        DualHighlighterButton.IsChecked = _activeTool == BoardTool.Highlighter;
        DualSelectButton.IsChecked = _activeTool == BoardTool.Select;
        if (DualLaserButton is not null)
        {
            DualLaserButton.IsChecked = _activeTool == BoardTool.Laser;
        }

        var penWash = _activeTool is BoardTool.Pen or BoardTool.Calligraphy
            ? (Brush)FindResource("ToolbarSelectedBrush")
            : Brushes.Transparent;
        var highlighterWash = _activeTool == BoardTool.Highlighter
            ? (Brush)FindResource("ToolbarSelectedBrush")
            : Brushes.Transparent;
        DualPenGroup.Background = penWash;
        DualHighlighterGroup.Background = highlighterWash;
    }

    private void UpdateDualSizeChipZooms()
    {
        UpdateHostSizeZooms(DualPenSizes, CurrentPenFamilyStyle());
        UpdateHostSizeZooms(DualHighlighterSizes, _styleByKind[PenKind.Highlighter]);
    }

    private void UpdateHostSizeZooms(Panel? host, PenStyle style)
    {
        if (host is null)
        {
            return;
        }

        var zoom = PreviewZoom(style);
        foreach (var button in host.Children.OfType<ToggleButton>())
        {
            if (button.Content is StrokePreview preview)
            {
                preview.Zoom = zoom;
            }
        }
    }

    private void ColorSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: uint argb })
        {
            LeaveLaserIfActive();
            SetActiveTool(_lastDrawingTool);
            CommitInkStyle(_penStyle with { Argb = argb });
            InkSurface.Focus();
        }
    }

    private void SizeChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: double thickness })
        {
            LeaveLaserIfActive();
            SetActiveTool(_lastDrawingTool);
            CommitInkStyle(_penStyle with { Thickness = thickness });
            InkSurface.Focus();
        }
    }

    private double PreviewZoom() => PreviewZoom(_penStyle);

    private double PreviewZoom(PenStyle style)
    {
        var thickest = InkPalettes.ThicknessesFor(style.Kind).Max();
        var attributes = InkDrawingAttributes.Create(
            style with { Thickness = thickest },
            1d);
        var extent = Math.Max(attributes.Width, attributes.Height);
        var maxZoom = extent <= 0 ? 1d : 26d / extent;
        return Math.Clamp(_camera.Zoom, 0.35, maxZoom);
    }

    private void UpdateSizeChipZooms()
    {
        UpdateHostSizeZooms(SizeChipHost, _penStyle);
    }

    private void UpdateColorPips()
    {
        var penColor = ToColor(_styleByKind[PenKind.Pen].Argb);
        var highlighterColor = ToColor(_styleByKind[PenKind.Highlighter].Argb);
        var calligraphyColor = ToColor(_styleByKind[PenKind.Calligraphy].Argb);
        if (PenColorPip is not null)
        {
            var activeFamilyColor = _activeTool == BoardTool.Calligraphy
                ? calligraphyColor
                : penColor;
            PenColorPip.Fill = new SolidColorBrush(activeFamilyColor);
        }

        if (HighlighterColorPip is not null)
        {
            HighlighterColorPip.Fill = new SolidColorBrush(highlighterColor);
        }
    }

    private void UpdatePenButtonGlyph()
    {
        if (PenToolIcon is null)
        {
            return;
        }

        var calligraphy = _activeTool == BoardTool.Calligraphy;
        PenToolIcon.Data = (Geometry)FindResource(
            calligraphy ? "CalligraphyPenGeometry" : "InkingToolGeometry");
        PenToolButton.ToolTip = calligraphy ? "Calligraphy" : "Pen";
    }

    private void UpdateSelectHover(PointD screen)
    {
        if (_containerGestureBefore is not null)
        {
            return;
        }

        var container = _document.HitTestTopContainer(_camera.ScreenToWorld(screen));
        var hoveredId = container?.Id;
        if (SceneSurface.HoveredObjectId == hoveredId)
        {
            return;
        }

        SceneSurface.HoveredObjectId = hoveredId;
        SceneSurface.InvalidateVisual();
    }

    private Cursor SelectCursorAt(PointD screen)
    {
        if (IsOverResizeHandle(screen))
        {
            return Cursors.SizeNWSE;
        }

        return _document.HitTestTopContainer(_camera.ScreenToWorld(screen)) is null
            ? Cursors.Arrow
            : Cursors.SizeAll;
    }

    private bool IsOverResizeHandle(PointD screen)
    {
        if (_selectedObjectId is not Guid existingId ||
            _document.Objects.FirstOrDefault(item => item.Id == existingId) is not { } existing ||
            existing is not IBoardContainer)
        {
            return false;
        }

        var handle = _camera.WorldToScreen(
            new PointD(existing.Bounds.Right, existing.Bounds.Bottom));
        return Distance(ToPoint(handle), ToPoint(screen)) <= 16;
    }

    private static PenKind ToPenKind(BoardTool tool) => tool switch
    {
        BoardTool.Highlighter => PenKind.Highlighter,
        BoardTool.Calligraphy => PenKind.Calligraphy,
        _ => PenKind.Pen,
    };

    private static Color ToColor(uint argb) => Color.FromArgb(
        (byte)(argb >> 24),
        (byte)(argb >> 16),
        (byte)(argb >> 8),
        (byte)argb);

    private static SolidColorBrush ToFrozenBrush(uint argb)
    {
        var brush = new SolidColorBrush(ToColor(argb));
        brush.Freeze();
        return brush;
    }

    private void ToolbarPlacementMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string name } &&
            Enum.TryParse<ToolbarPlacement>(name, out var placement))
        {
            SetToolbarPlacement(placement);
        }
    }

    private void ApplyPreferences()
    {
        ApplyToolbarPlacement();
        ApplyCalligraphyAccess();
        ApplyLaserSettings();
        PersistSettings();
    }

    private void PreferencesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PreferencesWindow(_settings, ApplyPreferences)
        {
            Owner = this,
        };
        dialog.ShowDialog();
        InkSurface.Focus();
    }

    private void SetToolbarPlacement(ToolbarPlacement placement)
    {
        _settings.ToolbarPlacement = placement;
        ApplyToolbarPlacement();
        PersistSettings();
    }

    private void ApplyToolbarPlacement()
    {
        var placement = _settings.ToolbarPlacement;
        ToolPalette.HorizontalAlignment = placement switch
        {
            ToolbarPlacement.TopLeft or ToolbarPlacement.BottomLeft => HorizontalAlignment.Left,
            ToolbarPlacement.BottomCenter => HorizontalAlignment.Center,
            _ => HorizontalAlignment.Right,
        };
        ToolPalette.VerticalAlignment = placement switch
        {
            ToolbarPlacement.BottomLeft or ToolbarPlacement.BottomRight or ToolbarPlacement.BottomCenter
                => VerticalAlignment.Bottom,
            _ => VerticalAlignment.Top,
        };

        var isBottom = placement is ToolbarPlacement.BottomLeft
            or ToolbarPlacement.BottomRight
            or ToolbarPlacement.BottomCenter;
        DockPanel.SetDock(ToolChrome, isBottom ? Dock.Bottom : Dock.Top);

        ToolbarTopRightMenuItem.IsChecked = placement == ToolbarPlacement.TopRight;
        ToolbarTopLeftMenuItem.IsChecked = placement == ToolbarPlacement.TopLeft;
        ToolbarBottomRightMenuItem.IsChecked = placement == ToolbarPlacement.BottomRight;
        ToolbarBottomLeftMenuItem.IsChecked = placement == ToolbarPlacement.BottomLeft;
        ToolbarBottomCenterMenuItem.IsChecked = placement == ToolbarPlacement.BottomCenter;
    }

    private void ApplyToolPaletteChrome()
    {
        if (_isToolPaletteHidden)
        {
            SetInkOptionsOpen(false);
            SetNibPickerOpen(false);
            ToolPaletteContents.Visibility = Visibility.Hidden;
            ToolPalette.Background = Brushes.Transparent;
            ToolPalette.BorderBrush = Brushes.Transparent;
            ToolPalette.Effect = null;
            return;
        }

        ToolPaletteContents.Visibility = Visibility.Visible;
        ToolPalette.Background = (Brush)FindResource("ToolbarBackgroundBrush");
        ToolPalette.BorderBrush = (Brush)FindResource("ToolbarBorderBrush");
        ToolPalette.Effect = new DropShadowEffect
        {
            BlurRadius = 18,
            ShadowDepth = 1,
            Direction = 270,
            Opacity = 0.16,
            Color = Colors.Black,
        };
    }

    private void PersistSettings()
    {
        try
        {
            AppSettingsStore.Save(_settings);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[Settings] Could not save preferences: {exception.Message}");
        }
    }

    private async void ImportButton_Click(object sender, RoutedEventArgs e) =>
        await ImportImageAsync();

    private async void PasteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_textEditBefore is not null)
        {
            TextEditor.Paste();
            TextEditor.Focus();
            return;
        }

        await PasteFromClipboardAsync();
    }

    private async void OpenButton_Click(object sender, RoutedEventArgs e) =>
        await OpenBoardAsync();

    private async void SaveButton_Click(object sender, RoutedEventArgs e) =>
        await SaveBoardAsync();

    private async void AddLiveViewMenuItem_Click(object sender, RoutedEventArgs e) =>
        await AddLiveViewAsync();

    private async void ReconnectLiveViewMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedLiveView() is { } liveView)
        {
            await ReconnectLiveViewAsync(liveView);
        }
    }

    private async void FreezeLiveViewMenuItem_Click(object sender, RoutedEventArgs e)
    {
        LiveViewBoardObject? liveView = GetSelectedLiveView();
        if (liveView is null)
        {
            return;
        }

        if (IsLiveViewRunning(liveView.Id))
        {
            PauseLiveView(liveView);
            return;
        }

        await ResumeLiveViewAsync(liveView);
    }

    private void ToolsMenu_SubmenuOpened(object sender, RoutedEventArgs e) =>
        UpdateLiveViewMenuItems();

    private void PauseLiveViewButton_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedLiveView() is { } liveView)
        {
            PauseLiveView(liveView);
        }

        InkSurface.Focus();
    }

    private async void PlayLiveViewButton_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedLiveView() is { } liveView)
        {
            await ResumeLiveViewAsync(liveView);
        }

        InkSurface.Focus();
    }

    private void PauseLiveView(LiveViewBoardObject liveView)
    {
        if (!_liveViewPresenters.TryGetValue(liveView.Id, out LiveViewPresenter? presenter) ||
            !presenter.HasTarget ||
            presenter.IsFrozen)
        {
            return;
        }

        try
        {
            presenter.Freeze();
            LiveViewBoardObject updated = SaveLiveViewSnapshot(
                liveView with { IsFrozen = true },
                presenter);
            _document.ReplaceObject(updated);
            DetachLiveViewSurface(presenter);
            UpdateLiveViewMenuItems();
            UpdateLiveViewActionOverlay();
        }
        catch (Exception exception)
        {
            ShowError("Could not freeze LiveView", exception);
        }
    }

    private async Task ResumeLiveViewAsync(LiveViewBoardObject liveView)
    {
        if (!_liveViewPresenters.TryGetValue(liveView.Id, out LiveViewPresenter? presenter) ||
            !presenter.HasTarget)
        {
            await ReconnectLiveViewAsync(liveView);
            return;
        }

        if (!presenter.IsFrozen)
        {
            return;
        }

        try
        {
            AttachLiveViewSurface(presenter);
            presenter.Resume();
            _document.ReplaceObject(liveView with { IsFrozen = false });
            UpdateLiveViewMenuItems();
            UpdateLiveViewActionOverlay();
        }
        catch (Exception exception)
        {
            ShowError("Could not resume LiveView", exception);
        }
    }

    private bool IsLiveViewRunning(Guid objectId) =>
        _liveViewPresenters.TryGetValue(objectId, out LiveViewPresenter? presenter) &&
        presenter.HasTarget &&
        !presenter.IsFrozen;

    private void NewMenuItem_Click(object sender, RoutedEventArgs e)
    {
        CommitTextEdit();
        if (!ConfirmDiscardUnsaved("Create a new whiteboard? Any unsaved changes will be lost."))
        {
            return;
        }

        ReplaceDocument(new BoardDocument());
        _currentBoardPath = null;
        ResetBoardView();
    }

    private bool ConfirmDiscardUnsaved(string message) =>
        _document.Objects.Count == 0 && _document.Assets.Count == 0 ||
        MessageBox.Show(
            this,
            message,
            "SQLBI Whiteboard",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;

    private async void SaveAsMenuItem_Click(object sender, RoutedEventArgs e) =>
        await SaveBoardAsync(saveAs: true);

    private void CloseMenuItem_Click(object sender, RoutedEventArgs e) => Close();

    private void CopyMenuItem_Click(object sender, RoutedEventArgs e) =>
        CopySelectionToClipboard();

    private void UndoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_textEditBefore is not null)
        {
            if (TextEditor.CanUndo)
            {
                TextEditor.Undo();
            }

            TextEditor.Focus();
            return;
        }

        _history.Undo(_document);
    }

    private void RedoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_textEditBefore is not null)
        {
            if (TextEditor.CanRedo)
            {
                TextEditor.Redo();
            }

            TextEditor.Focus();
            return;
        }

        _history.Redo(_document);
    }

    private async Task ImportImageAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import",
            Filter =
                "Importable files|*.wimport;*.png;*.jpg;*.jpeg;*.bmp;*.gif|Whiteboard import|*.wimport|Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif",
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (DroppedFileImport.Classify(dialog.FileName) == DroppedFileKind.Import)
        {
            await ImportRecipeAsync(dialog.FileName, VisibleTopLeft(), replaceDocument: false);
            return;
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(dialog.FileName);
            AddImage(bytes, Path.GetFileName(dialog.FileName), ContentTypeFor(dialog.FileName));
        }
        catch (Exception exception)
        {
            ShowError("Could not import image", exception);
        }
    }

    private Task PasteFromClipboardAsync()
    {
        try
        {
            if (Clipboard.ContainsImage())
            {
                var bitmap = Clipboard.GetImage();
                if (bitmap is null)
                {
                    return Task.CompletedTask;
                }

                AddImage(
                    WpfImageCodec.EncodePng(bitmap),
                    "clipboard-image.png",
                    "image/png");
                return Task.CompletedTask;
            }

            if (Clipboard.ContainsText(TextDataFormat.UnicodeText))
            {
                AddText(Clipboard.GetText(TextDataFormat.UnicodeText));
            }
        }
        catch (Exception exception)
        {
            ShowError("Could not paste clipboard content", exception);
        }

        return Task.CompletedTask;
    }

    private void AddImage(
        byte[] bytes,
        string fileName,
        string contentType,
        PointD? worldCenter = null)
    {
        var bitmap = WpfImageCodec.Decode(bytes);
        var assetId = Guid.NewGuid().ToString("N");
        _document.AddAsset(new BoardAsset(assetId, fileName, contentType, bytes));
        SceneSurface.InvalidateAssets();

        var naturalWidth = Math.Max(1, bitmap.PixelWidth);
        var naturalHeight = Math.Max(1, bitmap.PixelHeight);
        var scale = Math.Min(1, Math.Min(900d / naturalWidth, 700d / naturalHeight));
        var width = naturalWidth * scale;
        var height = naturalHeight * scale;
        var center = worldCenter ?? _camera.Center;
        var image = new ImageBoardObject(
            Guid.NewGuid(),
            _document.NextZIndex,
            new RectD(
                center.X - (width / 2),
                center.Y - (height / 2),
                width,
                height),
            assetId);

        _history.Execute(new AddObjectCommand(image), _document);
        _selectedObjectId = image.Id;
        SceneSurface.SelectedObjectId = image.Id;
        SetActiveTool(BoardTool.Select);
        UpdateLiveViewActionOverlay();
    }

    private void AddText(
        string text,
        PointD? worldCenter = null,
        bool beginEdit = true,
        string? title = null,
        string? languageId = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        double visibleWidth = _camera.VisibleWorldBounds.Width;
        double width = Math.Min(
            TextContainerVisual.DefaultWidth,
            Math.Max(320, visibleWidth * 0.7));
        double height = TextContainerVisual.MeasureDesiredHeight(
            text,
            width,
            1,
            VisualTreeHelper.GetDpi(SceneSurface).PixelsPerDip);
        PointD center = worldCenter ?? _camera.Center;
        var textObject = new TextBoardObject(
            Guid.NewGuid(),
            _document.NextZIndex,
            new RectD(
                center.X - (width / 2),
                center.Y - (height / 2),
                width,
                height),
            string.IsNullOrWhiteSpace(title) ? "Text" : title,
            text,
            LanguageId: TextLanguageIds.Normalize(languageId));

        _history.Execute(new AddObjectCommand(textObject), _document);
        _selectedObjectId = textObject.Id;
        SceneSurface.SelectedObjectId = textObject.Id;
        SetActiveTool(BoardTool.Select);
        if (beginEdit)
        {
            BeginTextEdit(textObject);
        }
    }

    private async Task AddLiveViewAsync()
    {
        if (!GraphicsCaptureSession.IsSupported())
        {
            MessageBox.Show(
                this,
                "Windows Graphics Capture is not supported on this computer.",
                "LiveView",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        GraphicsCaptureItem? item = await PickLiveViewTargetAsync();
        if (item is null)
        {
            return;
        }

        try
        {
            double naturalWidth = Math.Max(1, item.Size.Width);
            double naturalHeight = Math.Max(1, item.Size.Height);
            double scale = Math.Min(1, Math.Min(1000d / naturalWidth, 700d / naturalHeight));
            double width = naturalWidth * scale;
            double height = naturalHeight * scale;
            var liveView = new LiveViewBoardObject(
                Guid.NewGuid(),
                _document.NextZIndex,
                new RectD(
                    _camera.Center.X - (width / 2),
                    _camera.Center.Y - (height / 2),
                    width,
                    height),
                new LiveViewSourceConfiguration(
                    LiveViewSourceKind.Unknown,
                    DisplayNameOrFallback(item.DisplayName)),
                IsFrozen: false);

            _history.Execute(new AddObjectCommand(liveView), _document);
            AttachLiveViewPresenter(liveView, item);
            _selectedObjectId = liveView.Id;
            SceneSurface.SelectedObjectId = liveView.Id;
            SceneSurface.InvalidateVisual();
            SetActiveTool(BoardTool.Select);
            UpdateLiveViewActionOverlay();
        }
        catch (Exception exception)
        {
            ShowError("Could not add LiveView", exception);
        }
    }

    private async Task ReconnectLiveViewAsync(LiveViewBoardObject liveView)
    {
        GraphicsCaptureItem? item = await PickLiveViewTargetAsync();
        if (item is null)
        {
            return;
        }

        try
        {
            double sourceWidth = Math.Max(1, item.Size.Width);
            double sourceHeight = Math.Max(1, item.Size.Height);
            RectD updatedBounds = liveView.Bounds.WithCenteredAspectRatio(
                sourceWidth / sourceHeight);
            var updated = liveView with
            {
                Bounds = updatedBounds,
                Source = liveView.Source with
                {
                    DisplayName = DisplayNameOrFallback(item.DisplayName),
                },
                IsFrozen = false,
            };
            InkStrokeObject[] linkedStrokes = _document.LinkedStrokes(liveView.Id)
                .Select(stroke => stroke.TransformWithContainer(liveView.Bounds, updatedBounds))
                .ToArray();
            _document.ReplaceObjects([updated, .. linkedStrokes]);
            AttachLiveViewPresenter(updated, item);
            SceneSurface.InvalidateVisual();
            UpdateLiveViewMenuItems();
            UpdateLiveViewActionOverlay();
        }
        catch (Exception exception)
        {
            ShowError("Could not reconnect LiveView", exception);
        }
    }

    private async Task<GraphicsCaptureItem?> PickLiveViewTargetAsync()
    {
        GraphicsCapturePicker picker = new();
        nint windowHandle = new WindowInteropHelper(this).Handle;
        WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
        return await picker.PickSingleItemAsync();
    }

    private void AttachLiveViewPresenter(
        LiveViewBoardObject liveView,
        GraphicsCaptureItem item)
    {
        if (_liveViewPresenters.TryGetValue(liveView.Id, out LiveViewPresenter? existing))
        {
            existing.DesiredFrameRate = liveView.DesiredFrameRate;
            existing.CaptureCursor = liveView.CaptureCursor;
            AttachLiveViewSurface(existing);
            existing.SetTarget(item);
            return;
        }

        LiveViewPresenter presenter = new(liveView.Id, Dispatcher)
        {
            DesiredFrameRate = liveView.DesiredFrameRate,
            CaptureCursor = liveView.CaptureCursor,
        };
        presenter.FramePresented += LiveViewPresenter_FramePresented;
        presenter.TargetClosed += LiveViewPresenter_TargetClosed;
        presenter.CaptureFailed += LiveViewPresenter_CaptureFailed;
        _liveViewPresenters.Add(liveView.Id, presenter);
        AttachLiveViewSurface(presenter);
        presenter.SetTarget(item);
    }

    private void LiveViewPresenter_FramePresented(Guid objectId)
    {
        SceneSurface.InvalidateVisual();
    }

    private void LiveViewPresenter_TargetClosed(Guid objectId)
    {
        if (_document.Objects.FirstOrDefault(item => item.Id == objectId) is not LiveViewBoardObject liveView ||
            !_liveViewPresenters.TryGetValue(objectId, out LiveViewPresenter? presenter))
        {
            return;
        }

        try
        {
            LiveViewBoardObject updated = SaveLiveViewSnapshot(
                liveView with { IsFrozen = true },
                presenter);
            _document.ReplaceObject(updated);
            DetachLiveViewSurface(presenter);
            UpdateLiveViewMenuItems();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[LiveView] Could not retain closed target: {exception}");
        }
    }

    private void LiveViewPresenter_CaptureFailed(Guid objectId, Exception exception)
    {
        Debug.WriteLine($"[LiveView] Capture failed for {objectId}: {exception}");
        ShowError("LiveView capture failed", exception);
    }

    private LiveViewBoardObject SaveLiveViewSnapshot(
        LiveViewBoardObject liveView,
        LiveViewPresenter presenter)
    {
        byte[]? bytes = presenter.CaptureSnapshotPng();
        if (bytes is null)
        {
            return liveView;
        }

        string assetId = liveView.SnapshotAssetId ?? $"liveview-{liveView.Id:N}";
        _document.AddAsset(new BoardAsset(
            assetId,
            $"{SanitizeAssetName(liveView.Source.DisplayName)}.png",
            "image/png",
            bytes));
        SceneSurface.InvalidateAssets();
        return liveView with { SnapshotAssetId = assetId };
    }

    private void RefreshLiveViewSnapshots()
    {
        foreach (LiveViewBoardObject liveView in _document.Objects.OfType<LiveViewBoardObject>().ToArray())
        {
            if (!_liveViewPresenters.TryGetValue(liveView.Id, out LiveViewPresenter? presenter) ||
                !presenter.HasPresentedFrame)
            {
                continue;
            }

            LiveViewBoardObject updated = SaveLiveViewSnapshot(
                liveView with { IsFrozen = presenter.IsFrozen },
                presenter);
            if (updated != liveView)
            {
                _document.ReplaceObject(updated);
            }
        }
    }

    private ImageSource? GetLiveViewImageSource(Guid objectId) =>
        _liveViewPresenters.TryGetValue(objectId, out LiveViewPresenter? presenter)
            ? presenter.ImageSource
            : null;

    private LiveViewBoardObject? GetSelectedLiveView() =>
        _selectedObjectId is Guid selectedId
            ? _document.Objects.FirstOrDefault(item => item.Id == selectedId) as LiveViewBoardObject
            : null;

    private void UpdateLiveViewMenuItems()
    {
        LiveViewBoardObject? liveView = GetSelectedLiveView();
        bool selected = liveView is not null;
        FreezeLiveViewMenuItem.IsEnabled = selected;
        ReconnectLiveViewMenuItem.IsEnabled = selected;

        bool canResume = liveView is not null &&
            (!_liveViewPresenters.TryGetValue(liveView.Id, out LiveViewPresenter? presenter) ||
             presenter.IsFrozen ||
             !presenter.HasTarget);
        FreezeLiveViewMenuItem.Header = canResume
            ? "_Resume selected LiveView..."
            : "_Freeze selected LiveView";
    }

    private void UpdateLiveViewActionOverlay()
    {
        LiveViewBoardObject? liveView = GetSelectedLiveView();
        if (liveView is null)
        {
            LiveViewActionsBorder.Visibility = Visibility.Collapsed;
            return;
        }

        PointD topLeft = _camera.WorldToScreen(
            new PointD(liveView.Bounds.Left, liveView.Bounds.Top));
        PointD bottomRight = _camera.WorldToScreen(
            new PointD(liveView.Bounds.Right, liveView.Bounds.Bottom));

        const double overlayWidth = 132;
        const double overlayHeight = 56;
        const double inset = 8;
        Canvas.SetLeft(
            LiveViewActionsBorder,
            Math.Max(topLeft.X + inset, bottomRight.X - overlayWidth - inset));
        Canvas.SetTop(
            LiveViewActionsBorder,
            Math.Max(topLeft.Y + inset, bottomRight.Y - overlayHeight - inset));

        bool isRunning = IsLiveViewRunning(liveView.Id);
        PauseLiveViewButton.IsEnabled = isRunning;
        PlayLiveViewButton.IsEnabled = !isRunning;
        LiveViewActionsBorder.Visibility = Visibility.Visible;
    }

    private void DisposeLiveViewPresenter(Guid objectId)
    {
        if (!_liveViewPresenters.Remove(objectId, out LiveViewPresenter? presenter))
        {
            return;
        }

        presenter.FramePresented -= LiveViewPresenter_FramePresented;
        presenter.TargetClosed -= LiveViewPresenter_TargetClosed;
        presenter.CaptureFailed -= LiveViewPresenter_CaptureFailed;
        DetachLiveViewSurface(presenter);
        presenter.Dispose();
    }

    private void AttachLiveViewSurface(LiveViewPresenter presenter)
    {
        if (!LiveViewSurfaceHost.Children.Contains(presenter.Surface))
        {
            LiveViewSurfaceHost.Children.Add(presenter.Surface);
        }
    }

    private void DetachLiveViewSurface(LiveViewPresenter presenter)
    {
        presenter.Surface.PrepareForRemoval();
        LiveViewSurfaceHost.Children.Remove(presenter.Surface);
        SceneSurface.InvalidateVisual();
    }

    private void DisposeAllLiveViewPresenters()
    {
        foreach (Guid id in _liveViewPresenters.Keys.ToArray())
        {
            DisposeLiveViewPresenter(id);
        }
    }

    private static string DisplayNameOrFallback(string? displayName) =>
        string.IsNullOrWhiteSpace(displayName) ? "LiveView target" : displayName;

    private static string SanitizeAssetName(string displayName)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string sanitized = string.Concat(displayName.Select(character =>
            invalid.Contains(character) ? '_' : character));
        return string.IsNullOrWhiteSpace(sanitized) ? "liveview" : sanitized;
    }

    private async Task SaveBoardAsync(bool saveAs = false)
    {
        CommitTextEdit();
        var filePath = _currentBoardPath;
        if (saveAs || string.IsNullOrWhiteSpace(filePath))
        {
            var dialog = new SaveFileDialog
            {
                Title = saveAs ? "Save board as" : "Save board",
                Filter = "Whiteboard document|*.wboard",
                DefaultExt = ".wboard",
                AddExtension = true,
                FileName = string.IsNullOrWhiteSpace(filePath)
                    ? "Untitled board.wboard"
                    : Path.GetFileName(filePath),
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            filePath = dialog.FileName;
        }

        try
        {
            RefreshLiveViewSnapshots();
            var preview = BoardPreviewRenderer.Render(_document, GetLiveViewImageSource);
            await using var stream = new FileStream(
                filePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                useAsync: true);
            await BoardArchive.SaveAsync(
                _document,
                stream,
                previewPng: preview is null ? default : preview);
            _currentBoardPath = filePath;
        }
        catch (Exception exception)
        {
            ShowError("Could not save board", exception);
        }
    }

    private async Task OpenBoardAsync()
    {
        CommitTextEdit();
        var dialog = new OpenFileDialog
        {
            Title = "Open",
            Filter =
                "Whiteboard|*.wboard;*.wimport|Whiteboard document|*.wboard|Whiteboard import|*.wimport",
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await OpenPathAsync(dialog.FileName, confirmDiscard: true);
    }

    private async Task OpenPathAsync(string filePath, bool confirmDiscard)
    {
        if (DroppedFileImport.Classify(filePath) == DroppedFileKind.Import)
        {
            if (confirmDiscard &&
                !ConfirmDiscardUnsaved("Open this import? Any unsaved changes will be lost."))
            {
                return;
            }

            await ImportRecipeAsync(filePath, VisibleTopLeft(), replaceDocument: true);
            return;
        }

        if (confirmDiscard &&
            !ConfirmDiscardUnsaved("Open this board? Any unsaved changes will be lost."))
        {
            return;
        }

        await LoadBoardAsync(filePath);
    }

    private async Task LoadBoardAsync(string filePath)
    {
        try
        {
            await using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                useAsync: true);
            var loaded = await BoardArchive.LoadAsync(stream);
            ReplaceDocument(loaded);
            _currentBoardPath = filePath;
            ResetBoardView();
        }
        catch (Exception exception)
        {
            ShowError("Could not open board", exception);
        }
    }

    private void ReplaceDocument(BoardDocument replacement)
    {
        if (_textEditBefore is not null)
        {
            EndTextEditVisual(null);
        }

        DisposeAllLiveViewPresenters();
        _document.Changed -= Document_Changed;
        _document = replacement;
        _document.Changed += Document_Changed;
        SceneSurface.Configure(_document, _camera);
        Document_Changed(this, EventArgs.Empty);
    }

    private void ResetBoardView()
    {
        if (_textEditBefore is not null)
        {
            EndTextEditVisual(null);
        }

        _camera.Reset();
        _selectedObjectId = null;
        SceneSurface.SelectedObjectId = null;
        InkSurface.Strokes.Clear();
        _history.Clear();
        ResetContainerGesture();
        CameraChanged();
        InkSurface.Focus();
    }

    private void CopySelectionToClipboard()
    {
        try
        {
            if (_textEditBefore is not null)
            {
                TextEditor.Copy();
                return;
            }

            if (_selectedObjectId is not Guid selectedId ||
                _document.Objects.FirstOrDefault(item => item.Id == selectedId) is not { } selected)
            {
                return;
            }

            if (selected is TextBoardObject text)
            {
                Clipboard.SetText(text.Text, TextDataFormat.UnicodeText);
                return;
            }

            string? assetId = selected switch
            {
                ImageBoardObject image => image.AssetId,
                LiveViewBoardObject liveView => liveView.SnapshotAssetId,
                _ => null,
            };
            if (selected is LiveViewBoardObject currentLiveView &&
                _liveViewPresenters.TryGetValue(currentLiveView.Id, out LiveViewPresenter? presenter) &&
                presenter.HasPresentedFrame)
            {
                LiveViewBoardObject updated = SaveLiveViewSnapshot(currentLiveView, presenter);
                if (updated != currentLiveView)
                {
                    _document.ReplaceObject(updated);
                    assetId = updated.SnapshotAssetId;
                }
            }

            if (assetId is null || !_document.Assets.TryGetValue(assetId, out BoardAsset? asset))
            {
                return;
            }

            Clipboard.SetImage(WpfImageCodec.Decode(asset.Data));
        }
        catch (Exception exception)
        {
            ShowError("Could not copy selection", exception);
        }
    }

    private void Window_PreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) &&
                    e.Data.GetData(DataFormats.FileDrop) is string[] paths &&
                    DroppedFileImport.CanImportAny(paths)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_PreviewDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return;
        }

        var dropPoint = _camera.ScreenToWorld(ToPointD(e.GetPosition(InkSurface)));
        await ImportDroppedFilesAsync(paths, dropPoint);
    }

    private async Task ImportDroppedFilesAsync(string[] paths, PointD worldPoint)
    {
        var imported = 0;
        try
        {
            foreach (var path in paths)
            {
                var kind = DroppedFileImport.Classify(path);
                if (kind == DroppedFileKind.Unsupported)
                {
                    continue;
                }

                if (kind == DroppedFileKind.Import)
                {
                    await ImportRecipeAsync(path, worldPoint, replaceDocument: false);
                    imported++;
                    continue;
                }

                var center = worldPoint + new PointD(imported * 24, imported * 24);
                switch (kind)
                {
                    case DroppedFileKind.Image:
                        AddImage(
                            await File.ReadAllBytesAsync(path),
                            Path.GetFileName(path),
                            ContentTypeFor(path),
                            center);
                        break;
                    case DroppedFileKind.Text:
                        if (new FileInfo(path).Length > DroppedFileImport.MaximumTextBytes)
                        {
                            ShowError(
                                "Could not drop file",
                                new InvalidOperationException(
                                    $"{Path.GetFileName(path)} is too large to import as text."));
                            continue;
                        }

                        AddText(
                            await File.ReadAllTextAsync(path),
                            center,
                            beginEdit: false,
                            title: Path.GetFileNameWithoutExtension(path),
                            languageId: DroppedFileImport.LanguageIdFor(path));
                        break;
                }

                imported++;
            }
        }
        catch (Exception exception)
        {
            ShowError("Could not drop file", exception);
        }
    }

    private async Task ImportRecipeAsync(
        string filePath,
        PointD originTopLeft,
        bool replaceDocument)
    {
        try
        {
            var markdown = await File.ReadAllTextAsync(filePath);
            var baseDirectory = Path.GetDirectoryName(filePath);
            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                baseDirectory = Environment.CurrentDirectory;
            }

            var imported = ImportDocument.Parse(markdown).Resolve(baseDirectory);
            if (replaceDocument)
            {
                ReplaceDocument(new BoardDocument());
                _currentBoardPath = null;
                ResetBoardView();
                originTopLeft = VisibleTopLeft();
            }

            ApplyImport(imported, originTopLeft, recordUndo: !replaceDocument);
            if (imported.MissingFiles.Count > 0)
            {
                new MissingFilesWindow(imported.MissingFiles)
                {
                    Owner = this,
                }.ShowDialog();
            }
        }
        catch (Exception exception)
        {
            ShowError("Could not import whiteboard", exception);
        }
    }

    private void ApplyImport(ImportDocument imported, PointD originTopLeft, bool recordUndo)
    {
        if (imported.Items.Count == 0)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(SceneSurface).PixelsPerDip;
        var sizes = new List<(double Width, double Height, bool StartNewRow)>(imported.Items.Count);
        var decoded = new List<(ImportItem Item, byte[]? Bytes, double Width, double Height)>(
            imported.Items.Count);
        foreach (var item in imported.Items)
        {
            if (item.Kind == ImportItemKind.Image)
            {
                if (item.ImageBytes is null)
                {
                    continue;
                }

                try
                {
                    var bitmap = WpfImageCodec.Decode(item.ImageBytes);
                    var (width, height) = ImportLayout.ImageSize(bitmap.PixelWidth, bitmap.PixelHeight);
                    sizes.Add((width, height, item.StartNewRow));
                    decoded.Add((item, item.ImageBytes, width, height));
                }
                catch (Exception)
                {
                    continue;
                }
            }
            else
            {
                var text = item.Text ?? string.Empty;
                if (text.Length == 0)
                {
                    continue;
                }

                var width = Math.Min(
                    TextContainerVisual.DefaultWidth,
                    Math.Max(320, _camera.VisibleWorldBounds.Width * 0.7));
                var height = TextContainerVisual.MeasureDesiredHeight(text, width, 1, dpi);
                sizes.Add((width, height, item.StartNewRow));
                decoded.Add((item, null, width, height));
            }
        }

        if (decoded.Count == 0)
        {
            return;
        }

        var rects = ImportLayout.Place(sizes, originTopLeft);
        var objects = new List<BoardObject>(decoded.Count);
        var assets = new List<BoardAsset>();
        for (var index = 0; index < decoded.Count; index++)
        {
            var (item, bytes, _, _) = decoded[index];
            var bounds = rects[index];
            if (item.Kind == ImportItemKind.Image && bytes is not null)
            {
                var assetId = Guid.NewGuid().ToString("N");
                assets.Add(new BoardAsset(
                    assetId,
                    item.ImageFileName ?? "image.png",
                    ContentTypeFor(item.ImageFileName ?? item.SourcePath ?? "image.png"),
                    bytes));
                objects.Add(new ImageBoardObject(
                    Guid.NewGuid(),
                    _document.NextZIndex + objects.Count,
                    bounds,
                    assetId));
            }
            else
            {
                objects.Add(new TextBoardObject(
                    Guid.NewGuid(),
                    _document.NextZIndex + objects.Count,
                    bounds,
                    item.Title,
                    item.Text ?? string.Empty,
                    LanguageId: TextLanguageIds.Normalize(item.LanguageId)));
            }
        }

        var command = new AddImportCommand(objects, assets);
        if (recordUndo)
        {
            _history.Execute(command, _document);
        }
        else
        {
            command.Execute(_document);
            _history.Clear();
        }

        SceneSurface.InvalidateAssets();
        if (objects.Count > 0)
        {
            _selectedObjectId = objects[^1].Id;
            SceneSurface.SelectedObjectId = _selectedObjectId;
            SetActiveTool(BoardTool.Select);
        }

        UpdateLiveViewActionOverlay();
    }

    private PointD VisibleTopLeft() => _camera.ScreenToWorld(new PointD(0, 0));

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            if (!e.IsRepeat)
            {
                ToggleFullScreen();
            }

            e.Handled = true;
            return;
        }

        var modifiers = Keyboard.Modifiers;
        var controlDown = modifiers.HasFlag(ModifierKeys.Control);
        var shiftDown = modifiers.HasFlag(ModifierKeys.Shift);
        if (_textEditBefore is not null)
        {
            if (e.Key == Key.F6 && !e.IsRepeat)
            {
                FormatTextEdit();
                e.Handled = true;
            }
            else if (shiftDown && e.Key == Key.F12)
            {
                CommitTextEdit();
                _ = SaveBoardAsync(saveAs: true);
                e.Handled = true;
            }
            else if (controlDown && e.Key == Key.S)
            {
                CommitTextEdit();
                _ = SaveBoardAsync();
                e.Handled = true;
            }
            else if (controlDown && e.Key == Key.O)
            {
                CommitTextEdit();
                _ = OpenBoardAsync();
                e.Handled = true;
            }
            else if (controlDown && e.Key == Key.Enter)
            {
                CommitTextEdit();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CancelTextEdit();
                e.Handled = true;
            }

            return;
        }

        if (_isFullScreen && e.Key == Key.Escape)
        {
            ExitFullScreen();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F2 &&
            _selectedObjectId is Guid textObjectId &&
            _document.Objects.FirstOrDefault(item => item.Id == textObjectId) is TextBoardObject textObject)
        {
            BeginTextEdit(textObject);
            e.Handled = true;
        }
        else if (shiftDown && e.Key == Key.F12)
        {
            _ = SaveBoardAsync(saveAs: true);
            e.Handled = true;
        }
        else if (controlDown && e.Key == Key.C)
        {
            CopySelectionToClipboard();
            e.Handled = true;
        }
        else if (controlDown && e.Key == Key.Z)
        {
            _history.Undo(_document);
            e.Handled = true;
        }
        else if (controlDown && e.Key == Key.Y)
        {
            _history.Redo(_document);
            e.Handled = true;
        }
        else if (controlDown && e.Key == Key.V)
        {
            _ = PasteFromClipboardAsync();
            e.Handled = true;
        }
        else if (controlDown && e.Key == Key.S)
        {
            _ = SaveBoardAsync();
            e.Handled = true;
        }
        else if (controlDown && e.Key == Key.O)
        {
            _ = OpenBoardAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && _selectedObjectId is Guid selectedId)
        {
            var deletionGroup = _document.GetDeletionGroup(selectedId);
            if (deletionGroup.Count > 0)
            {
                _history.Execute(new RemoveObjectsCommand(deletionGroup), _document);
                _selectedObjectId = null;
                SceneSurface.SelectedObjectId = null;
                SceneSurface.InvalidateVisual();
            }

            e.Handled = true;
        }
        else if (modifiers.HasFlag(ModifierKeys.Alt) &&
                 !e.IsRepeat &&
                 (e.Key == Key.L || e.SystemKey == Key.L))
        {
            SetActiveTool(
                _activeTool == BoardTool.Laser
                    ? _lastDrawingTool
                    : BoardTool.Laser);
            e.Handled = true;
        }
        else if (e.Key == Key.Space &&
                 !_spaceTemporaryPan &&
                 !IsControlFocused())
        {
            _toolBeforeSpace = _activeTool;
            _spaceTemporaryPan = true;
            SetActiveTool(BoardTool.Pan);
            e.Handled = true;
        }
    }

    private void ToggleFullScreen()
    {
        if (_isFullScreen)
        {
            ExitFullScreen();
        }
        else
        {
            EnterFullScreen();
        }
    }

    private void EnterFullScreen()
    {
        _windowStateBeforeFullScreen = WindowState;
        _windowStyleBeforeFullScreen = WindowStyle;
        _resizeModeBeforeFullScreen = ResizeMode;
        _windowBoundsBeforeFullScreen = new Rect(Left, Top, Width, Height);

        WindowState = WindowState.Normal;
        MainMenu.Visibility = Visibility.Collapsed;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        MonitorStartupPlacement.FillCurrentMonitor(this);
        _isFullScreen = true;
    }

    private void ExitFullScreen()
    {
        WindowState = WindowState.Normal;
        MainMenu.Visibility = Visibility.Visible;
        WindowStyle = _windowStyleBeforeFullScreen;
        ResizeMode = _resizeModeBeforeFullScreen;

        if (_windowStateBeforeFullScreen == WindowState.Normal)
        {
            Left = _windowBoundsBeforeFullScreen.Left;
            Top = _windowBoundsBeforeFullScreen.Top;
            Width = _windowBoundsBeforeFullScreen.Width;
            Height = _windowBoundsBeforeFullScreen.Height;
        }

        WindowState = _windowStateBeforeFullScreen;
        _isFullScreen = false;
    }

    private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && _spaceTemporaryPan)
        {
            _spaceTemporaryPan = false;
            SetActiveTool(_toolBeforeSpace);
            e.Handled = true;
        }
    }

    private static bool IsControlFocused() =>
        Keyboard.FocusedElement is ButtonBase or Slider or ComboBox or MenuItem;

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (_stylusAction == PointerAction.Erase || _mouseAction == PointerAction.Erase)
        {
            CompleteErase();
        }
        else if (_stylusAction == PointerAction.Container || _mouseAction == PointerAction.Container)
        {
            CompleteContainerGesture();
        }

        _stylusAction = PointerAction.None;
        _mouseAction = PointerAction.None;
        _penInContact = false;
        ClearTouchNavigation();
        InkSurface.Cursor = Cursors.Arrow;
        HidePointerDot();
        StopLaserSampling();
        LaserTrail.HideHead();
        if (_laserTemporary)
        {
            EndTemporaryLaser();
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        CommitTextEdit();
        // Unregister Vortice's retained Window.Closed callbacks and unload the
        // D3D surfaces before the Closed event begins. This guarantees one
        // native teardown path for both live and already-paused presenters.
        DisposeAllLiveViewPresenters();
    }

    private void Window_PreviewStylusInRange(object sender, StylusEventArgs e)
    {
        if (!_penInContact)
        {
            UpdateHoverPointerDot(e);
        }
    }

    private void Window_PreviewStylusOutOfRange(object sender, StylusEventArgs e)
    {
        if (!IsTouchStylus(e))
        {
            HidePointerDot();
        }
    }

    private void Window_PreviewStylusInAirMove(object sender, StylusEventArgs e) =>
        UpdateHoverPointerDot(e);

    private void HoverTracker_Hovered(Point inkSurfacePoint)
    {
        if (_penInContact)
        {
            return;
        }

        UpdateHoverPointerDotAt(InkSurface.TranslatePoint(inkSurfacePoint, RootGrid), overBoard: true);
    }

    private void HoverWatch_Tick(object? sender, EventArgs e)
    {
        if (Stopwatch.GetElapsedTime(_lastHoverTimestamp).TotalMilliseconds > 120)
        {
            HidePointerDot();
            _hoverWatch.Stop();
        }
    }

    private void UsePenCursor()
    {
        InkSurface.Cursor = EffectiveTool is BoardTool.Select
            ? Cursors.Arrow
            : Cursors.None;
    }

    // Contact packets arrive as StylusMove; hover is StylusInAirMove. Wacom
    // Cintiqs also fire OutOfRange when the pen leaves detection, which is
    // not the same as leaving the InkCanvas hit-test bounds.
    private void UpdateHoverPointerDot(StylusEventArgs e)
    {
        if (IsTouchStylus(e) ||
            e.StylusDevice.Inverted ||
            !e.StylusDevice.InAir)
        {
            HidePointerDot();
            return;
        }

        var rootPosition = e.GetPosition(RootGrid);
        var boardPosition = e.GetPosition(BoardViewport);
        UpdateHoverPointerDotAt(rootPosition, IsWithin(boardPosition, BoardViewport));
    }

    private void UpdateHoverPointerDotAt(Point rootPosition, bool overBoard)
    {
        if (_penInContact ||
            EffectiveTool == BoardTool.Select ||
            !overBoard)
        {
            HidePointerDot();
            return;
        }

        UsePenCursor();
        ShowPointerDot(rootPosition);
        _lastHoverTimestamp = Stopwatch.GetTimestamp();
        if (!_hoverWatch.IsEnabled)
        {
            _hoverWatch.Start();
        }
    }

    private static bool IsWithin(Point position, FrameworkElement element) =>
        position.X >= 0 &&
        position.Y >= 0 &&
        position.X <= element.ActualWidth &&
        position.Y <= element.ActualHeight;

    private void ShowPointerDot(Point position)
    {
        if (EffectiveTool == BoardTool.Select)
        {
            HidePointerDot();
            return;
        }

        PointerDotTransform.X = position.X - (PointerDot.Width / 2);
        PointerDotTransform.Y = position.Y - (PointerDot.Height / 2);
        PointerDot.Visibility = Visibility.Visible;
    }

    private void HidePointerDot()
    {
        PointerDot.Visibility = Visibility.Collapsed;
        _hoverWatch.Stop();
    }

    private void ShowError(string context, Exception exception)
    {
        MessageBox.Show(
            this,
            exception.Message,
            context,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private static bool IsTouchStylus(StylusEventArgs e) =>
        e.StylusDevice.TabletDevice.Type == TabletDeviceType.Touch;

    private static string ContentTypeFor(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".bmp" => "image/bmp",
            ".gif" => "image/gif",
            _ => "application/octet-stream",
        };

    private static PointD ToPointD(Point point) => new(point.X, point.Y);
    private static Point ToPoint(PointD point) => new(point.X, point.Y);
    private static Point Midpoint(Point first, Point second) =>
        new((first.X + second.X) / 2, (first.Y + second.Y) / 2);
    private static double Distance(Point first, Point second) =>
        Math.Sqrt(
            Math.Pow(first.X - second.X, 2) +
            Math.Pow(first.Y - second.Y, 2));
}
