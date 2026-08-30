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
using System.Windows.Shell;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Document;
using Microsoft.Win32;
using SQLBI.Whiteboard.Core.Commands;
using SQLBI.Whiteboard.Core.Geometry;
using SQLBI.Whiteboard.Core.Import;
using SQLBI.Whiteboard.Core.Model;
using SQLBI.Whiteboard.Core.Persistence;
using SQLBI.Whiteboard.Core.Settings;
using SQLBI.Whiteboard.Core.Updates;
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
        Ink,
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
    private BoardTool _toolBeforeBarrel = BoardTool.Pen;
    private bool _barrelToolTemporary;
    private bool _discardInkStroke;
    private StylusButton? _barrelButton;
    private bool _lastContactWasPen;
    private readonly List<InkPoint> _penInk = [];
    private PointD? _penInkAnchor;
    private StraightLineDirection _penInkDirection;
    private PointD? _penInkPrevious;
    private double _penInkSpeed;
    private int _penInkWeightless;
    private PenStyle _penStyle = InkPalettes.DefaultPen;
    private PointerAction _stylusAction;
    private PointerAction _mouseAction;
    private bool _mouseToolBorrowed;
    private PointD _lastPanPoint;
    private bool _penInContact;
    private bool _touchNavigationLocked;
    private int? _fingerToolDeviceId;
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
    private bool _updatingLanguageChip;
    private string _textEditLanguageId = TextLanguageIds.Plain;
    private readonly TextClassificationColorizer _textColorizer = new();
    private readonly DispatcherTimer _textHighlightTimer;
    private CancellationTokenSource? _textAnalysisCancellation;
    private enum SessionChromeMode
    {
        Windowed,
        CanvasOnly,
        FullScreen,
    }

    private SessionChromeMode _chromeMode;
    private bool _isToolPaletteHidden;
    private bool _isInkOptionsOpen;
    private bool _isNibPickerOpen;
    private AppSettings _settings = new();

    private const double ChevronInkOptionsWidth = 240;

    private bool _syntheticLaserContact;
    private bool _penInverted;

    // How far the eraser reaches from the pen, in screen pixels at any zoom.
    // The hover square is drawn from the same number, so what it outlines is
    // what a tap would clear.
    private const double EraserScreenRadius = 12;
    private const double SessionTabHeight = 32;
    private const double ToolPaletteInset = 16;
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
        SessionBar.CommandRequested += SessionChrome_CommandRequested;
        SessionBar.ViewOpened += UpdateLiveViewMenuItems;
        SessionBar.UpdateDownloadRequested += _ => OpenUpdateDownload();
        SessionBar.UpdateDismissed += SessionBar_UpdateDismissed;
        Title += AppChannel.WindowTitleSuffix;
        _initialBoardPath = initialBoardPath;
        TextEditorLanguageCombo.ItemsSource = TextLanguageRegistry.All;
        LanguageChipCombo.ItemsSource = TextLanguageRegistry.All;
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
        ApplyPointerModes();
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
            SetChromeMode(SessionChromeMode.FullScreen);
        }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;
        WarnWhenNothingToDrawWith();
        _ = CheckForUpdatesAsync();
        if (_initialBoardPath is not null)
        {
            await OpenPathAsync(_initialBoardPath, confirmDiscard: false);
        }
    }

    // Which pointing device this session is drawing with is worth saying out
    // loud, rather than leaving someone to work out from the toolbar that the
    // left button has taken on a job it does not have on a pen machine.
    private void WarnWhenNothingToDrawWith()
    {
        if (!_settings.WarnWhenNoDigitizer || NoDigitizerWindow.HasDrawingDevice())
        {
            return;
        }

        var notice = new NoDigitizerWindow(IsMouseModeEffective) { Owner = this };
        notice.ShowDialog();
        if (!notice.DoNotShowAgain)
        {
            return;
        }

        _settings.WarnWhenNoDigitizer = false;
        AppSettingsStore.Save(_settings);
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
        SessionBar.SetEditEnabled(_history.CanUndo, _history.CanRedo);
    }

    // Only touch reaches here now. Pen ink is collected from the pen's own
    // stream instead - see AppendPenInk - because the barrel switch tears the
    // WPF contact in two on every press and every release, and nothing built on
    // top of that bookkeeping could be made to behave like the Shift key.
    private void InkSurface_StrokeCollected(object sender, InkCanvasStrokeCollectedEventArgs e)
    {
        InkSurface.Strokes.Remove(e.Stroke);
        if (EffectiveTool == BoardTool.Laser ||
            _stylusAction == PointerAction.Laser ||
            _discardInkStroke ||
            _lastContactWasPen ||
            e.Stroke.StylusPoints.Count == 0)
        {
            _discardInkStroke = false;
            return;
        }

        var firstTimestamp = Stopwatch.GetTimestamp();
        CommitInkPoints(e.Stroke.StylusPoints
            .Select((point, index) => new InkPoint(
                _camera.ScreenToWorld(new PointD(point.X, point.Y)),
                point.PressureFactor,
                firstTimestamp + index))
            .ToArray());
        SceneSurface.InvalidateVisual();
    }

    private void CommitInkPoints(IReadOnlyList<InkPoint> points)
    {
        var stroke = InkStrokeObject.Create(
            points,
            _penStyle,
            _document.NextZIndex);
        if (_document.FindSingleTouchedContainer(stroke) is { } container)
        {
            stroke = stroke with { ContainerId = container.Id };
        }

        _history.Execute(new AddObjectCommand(stroke), _document);
    }

    // Every packet the pen reports, in contact or not. This is the whole of the
    // pen ink path: the constraint is one question asked per point, exactly as
    // it is for Shift, because nothing here depends on WPF believing the pen is
    // down. It does not believe it for as long as a barrel button is held.
    private void AppendPenInk(StylusEventArgs e)
    {
        if (!IsInkTool || _stylusAction != PointerAction.None || e.StylusDevice.Inverted)
        {
            EndPenInk();
            return;
        }

        var points = e.GetStylusPoints(InkSurface);
        var pressed = false;
        for (var index = 0; index < points.Count; index++)
        {
            var point = points[index];
            if (point.PressureFactor <= 0)
            {
                continue;
            }

            pressed = true;
            AppendInkPoint(new PointD(point.X, point.Y), point.PressureFactor);
        }

        if (pressed)
        {
            _penInkWeightless = 0;
            _lastContactWasPen = true;
            _penInContact = true;
            SceneSurface.PendingStroke = _penInk;
            SceneSurface.PendingStrokeStyle = _penStyle;
            SceneSurface.InvalidateVisual();
            return;
        }

        // One weightless packet is a dropped reading; a run of them is the pen
        // off the glass.
        if (_penInk.Count > 0 && ++_penInkWeightless < LiftedPacketCount)
        {
            return;
        }

        EndPenInk();
    }

    // Shared by the pen and the mouse: it takes a screen point and a pressure,
    // and has no opinion about where either came from. The straight-line
    // constraint and the calligraphy dynamics live here, which is why the mouse
    // gets both without a second implementation.
    private void AppendInkPoint(PointD screen, float pressure)
    {
        var constrained = StraightLineConstraintActive;
        if (!constrained)
        {
            _penInkAnchor = null;
            _penInkDirection = StraightLineDirection.None;
        }
        else if (_penInkAnchor is null)
        {
            _penInkAnchor = screen;
            _penInkDirection = StraightLineDirection.None;
        }

        if (constrained && _penInkAnchor is PointD anchor)
        {
            // The axis is chosen once and kept. A hand that drifts off it is
            // still drawing the line it asked for, however far away it gets.
            if (_penInkDirection == StraightLineDirection.None)
            {
                _penInkDirection = StraightLineSnap.DetectDirection(anchor, screen);
            }

            screen = _penInkDirection == StraightLineDirection.None
                ? anchor
                : StraightLineSnap.Apply(screen, anchor, _penInkDirection);

            // A straight line is drawn, not written: uniform width.
            pressure = StraightLinePressure;
            _penInkPrevious = screen;
            _penInkSpeed = 0;
        }
        else if (_penStyle.Kind == PenKind.Calligraphy)
        {
            if (_penInkPrevious is PointD previous)
            {
                var deltaX = screen.X - previous.X;
                var deltaY = screen.Y - previous.Y;
                var speed = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
                _penInkSpeed = _penInkSpeed == 0
                    ? speed
                    : (_penInkSpeed * 0.65) + (speed * 0.35);
            }

            pressure = CalligraphyDynamics.AdjustPressure(pressure, _penInkSpeed);
            _penInkPrevious = screen;
        }
        else
        {
            _penInkPrevious = screen;
        }

        _penInk.Add(new InkPoint(
            _camera.ScreenToWorld(screen),
            pressure,
            Stopwatch.GetTimestamp()));
    }

    private void EndPenInk()
    {
        _penInkWeightless = 0;
        _penInkAnchor = null;
        _penInkDirection = StraightLineDirection.None;
        _penInkPrevious = null;
        _penInkSpeed = 0;
        SceneSurface.PendingStroke = null;
        if (_penInk.Count > 1 && !_discardInkStroke)
        {
            CommitInkPoints([.. _penInk]);
        }

        _discardInkStroke = false;
        if (_penInk.Count > 0)
        {
            _penInk.Clear();
            SceneSurface.InvalidateVisual();
        }
    }

    private void InkSurface_PreviewStylusDown(object sender, StylusDownEventArgs e)
    {
        PenTrace.Write("stylus-down", e, PenTraceState());
        _lastContactWasPen = !IsTouchStylus(e);
        CommitTextEdit();
        SessionBar.CollapseIfTransient();

        if (TryActivatePaletteFromStylus(e))
        {
            return;
        }

        if (!IsTouchStylus(e) && !IsPenTipDown(e))
        {
            Debug.WriteLine("[Pen] barrel button opened a stylus down in mid-air");

            // The event still reached the InkCanvas, which restores its own ink
            // cursor on the way past. Nothing else refreshes it until the pen
            // moves again, and a pen resting still after a barrel click does not
            // move, so the cursor is put back here rather than left showing.
            UsePenCursor();

            // Handled so the InkCanvas does not open an editing gesture for a
            // touch we have just decided never happened.
            e.Handled = true;
            return;
        }

        // A barrel transition while the tip is already down is delivered as a
        // stylus down of its own. Our own state is already right for a contact
        // that never ended, so none of it is redone here - but the event is left
        // to reach the InkCanvas, which opens the next stroke with it. Held
        // back, the InkCanvas instead carried the previous stroke across the
        // pen's absence and drew a line through it.
        if (!IsTouchStylus(e) && _penInContact && HasTipPressure(e))
        {
            return;
        }

        if (IsTouchStylus(e))
        {
            if (!TryBeginFingerTool(e))
            {
                return;
            }
        }
        else
        {
            _discardInkStroke = false;
            _penInContact = true;
            CancelFingerTool();
            ClearTouchNavigation();
            UsePenCursor();
            HidePointerDot();
        }

        if (e.StylusDevice.Inverted || EffectiveTool != BoardTool.Select)
        {
            ClearSelection();
        }

        var screen = ToPointD(e.GetPosition(InkSurface));
        if (e.StylusDevice.Inverted || EffectiveTool == BoardTool.Eraser)
        {
            BeginErase(screen);
            _stylusAction = PointerAction.Erase;
            _discardInkStroke = true;
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
            _lastContactWasPen = false;
            InkSurface.RegisterTouchTablet(e.StylusDevice.TabletDevice.Id);
            if (IsTouchNavigating)
            {
                UpdateTouchNavigation(e);
                return;
            }

            TrackTouchPoint(e);
        }
        else
        {
            PenTrace.Write("stylus-move", e, PenTraceState());
            AppendPenInk(e);
        }

        RecoverLaserContactFromPressure(e);

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
            if (!_penInContact)
            {
                UpdateHoverPointerDot(e);
            }
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
        PenTrace.Write("stylus-up", e, PenTraceState());
        if (IsTouchStylus(e))
        {
            InkSurface.RegisterTouchTablet(e.StylusDevice.TabletDevice.Id);
            var navigating = IsTouchNavigating;
            EndTouchTracking(e, navigating);
            if (navigating)
            {
                return;
            }
        }
        else if (HasTipPressure(e))
        {
            // A stylus up still carrying tip pressure is a barrel transition,
            // not a lift, so the contact state here is left alone - but the
            // event goes on to the InkCanvas, which ends the stroke with it.
            // That is the truth of what follows: Windows reports the pen in the
            // air from here until the next stylus down, so the stroke really
            // does stop, and joining it to whatever comes next draws a line
            // through everywhere the pen was not.
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
        _syntheticLaserContact = false;
        if (EffectiveTool == BoardTool.Laser)
        {
            StopLaserSampling();
            LaserTrail.Lift();
            UsePenCursor();
            UpdateHoverPointerDot(e);
        }
        else if (EffectiveTool == BoardTool.Select)
        {
            var hoverScreen = ToPointD(e.GetPosition(InkSurface));
            UpdateSelectHover(hoverScreen);
            InkSurface.Cursor = SelectCursorAt(hoverScreen);
            HidePointerDot();
        }
        else
        {
            UpdateHoverPointerDot(e);
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
        TryActivatePaletteFromStylus(e);
    }

    // Clicking the barrel button over a hovering pen is delivered as a stylus
    // down that claims everything a real touch claims - not in air, tip switch
    // pressed. Only the pressure gives it away, because nothing is pressing on
    // the tip. Requiring the barrel to be down as well keeps a genuinely light
    // first packet from being mistaken for one of these.
    private bool IsPenTipDown(StylusEventArgs e)
    {
        if (e.StylusDevice.InAir)
        {
            return false;
        }

        var points = e.GetStylusPoints(InkSurface);
        if (points.Count == 0)
        {
            return true;
        }

        for (var index = 0; index < points.Count; index++)
        {
            var point = points[index];
            if (point.PressureFactor > 0 ||
                !point.HasProperty(StylusPointProperties.BarrelButton) ||
                point.GetPropertyValue(StylusPointProperties.BarrelButton) == 0)
            {
                return true;
            }
        }

        return false;
    }

    // The barrel click opens a stylus down that WPF does not close until well
    // after the pen has touched and left again, so the first real landing is
    // delivered as moves inside it and never gets a down of its own. Tip
    // pressure is the only honest signal that the pen is on the glass, so the
    // trail starts and ends on that instead of waiting for events that are not
    // coming.
    private void RecoverLaserContactFromPressure(StylusEventArgs e)
    {
        if (IsTouchStylus(e))
        {
            return;
        }

        var pressed = HasTipPressure(e);
        if (pressed &&
            !_penInContact &&
            _stylusAction == PointerAction.None)
        {
            _penInContact = true;
            HidePointerDot();
            UsePenCursor();
            if (e.StylusDevice.Inverted || EffectiveTool == BoardTool.Eraser)
            {
                BeginErase(ToPointD(e.GetPosition(InkSurface)));
                InkSurface.CaptureStylus();
                _stylusAction = PointerAction.Erase;
                _discardInkStroke = true;
            }
            else if (EffectiveTool == BoardTool.Laser)
            {
                _syntheticLaserContact = true;
                BeginLaserContact(e);
                _stylusAction = PointerAction.Laser;
            }

            return;
        }

        if (!pressed && _syntheticLaserContact)
        {
            EndSyntheticLaserContact();
        }
    }

    private void EndSyntheticLaserContact()
    {
        _syntheticLaserContact = false;
        _penInContact = false;
        _stylusAction = PointerAction.None;
        StopLaserSampling();
        LaserTrail.Lift();
    }

    private bool HasTipPressure(StylusEventArgs e)
    {
        var points = e.GetStylusPoints(InkSurface);
        for (var index = 0; index < points.Count; index++)
        {
            if (points[index].PressureFactor > 0)
            {
                return true;
            }
        }

        return false;
    }

    private void ToolPalette_PreviewStylusDown(object sender, StylusDownEventArgs e)
    {
        SessionBar.CollapseIfTransient();
        ReleaseBoardPointerCapture(e.StylusDevice);
        LaserTrail.Lift();
    }

    private bool TryActivatePaletteFromStylus(StylusDownEventArgs e)
    {
        if (!TryActivatePaletteAt(e.GetPosition(ToolPalette), e.StylusDevice))
        {
            return false;
        }

        CancelFingerTool();
        e.Handled = true;
        return true;
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
        if (IsTouchStylus(e))
        {
            return;
        }

        PenTrace.Write("button-down", e, PenTraceState());

        if (!IsBarrelButton(e.StylusDevice, e.StylusButton))
        {
            return;
        }

        _barrelButton = e.StylusButton;
        RefreshBarrelState(e);
        e.Handled = true;
    }

    private void InkSurface_PreviewStylusButtonUp(object sender, StylusButtonEventArgs e)
    {
        PenTrace.Write("button-up", e, PenTraceState());
        if (!ReferenceEquals(e.StylusButton, _barrelButton))
        {
            return;
        }

        _barrelButton = null;
        RefreshBarrelState(e);
        e.Handled = true;
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

    private bool BarrelHolds(PenButtonAction action) =>
        _barrelButton is not null && _settings.PenButtons.Barrel == action;

    // Actions that swap the tool for as long as the button is held. A modifier
    // such as the straight-line constraint has no tool of its own.
    private static BoardTool? BarrelToolFor(PenButtonAction action) => action switch
    {
        PenButtonAction.Laser => BoardTool.Laser,
        _ => null,
    };

    private void RefreshBarrelState(StylusEventArgs e)
    {
        if (_barrelButton is not null &&
            BarrelToolFor(_settings.PenButtons.Barrel) is BoardTool tool)
        {
            ApplyTemporaryBarrelTool(tool, e);
        }
        else
        {
            EndTemporaryBarrelTool();
        }

    }

    private void ApplyTemporaryBarrelTool(BoardTool tool, StylusEventArgs e)
    {
        if (!_barrelToolTemporary)
        {
            _toolBeforeBarrel = _activeTool == tool ? _lastDrawingTool : _activeTool;
            _barrelToolTemporary = true;
            SetActiveTool(tool);
            InkSurface.AbortWetInk();
            _discardInkStroke = _penInContact;

            // Engaging a tool from the barrel button is the one tool change that
            // can happen without the pen moving, so it cannot wait for the next
            // pointer event to settle the cursor.
            UsePenCursor();
        }

        if (_penInContact && tool == BoardTool.Laser && _stylusAction != PointerAction.Laser)
        {
            BeginLaserContact(e);
            _stylusAction = PointerAction.Laser;
        }
    }

    private void EndTemporaryBarrelTool()
    {
        if (!_barrelToolTemporary)
        {
            return;
        }

        _barrelToolTemporary = false;
        if (_stylusAction == PointerAction.Laser)
        {
            StopLaserSampling();
            LaserTrail.Lift();
            _stylusAction = PointerAction.None;
        }

        LaserTrail.HideHead();
        SetActiveTool(_toolBeforeBarrel);
        UsePenCursor();
    }

    // WPF's neutral pressure: a constrained segment is drawn at exactly the
    // configured thickness, with no taper at either end.
    private const float StraightLinePressure = 0.5f;

    // Packets arrive a few milliseconds apart, so a lift is tens of weightless
    // ones. A shorter run is the digitizer missing a reading.
    private const int LiftedPacketCount = 4;

    private bool StraightLineConstraintActive =>
        Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ||
        BarrelHolds(PenButtonAction.StraightLine);

    private bool IsInkTool =>
        EffectiveTool is BoardTool.Pen or BoardTool.Highlighter or BoardTool.Calligraphy;

    // The barrel button is whichever button is neither the writing tip nor the
    // reverse end. Both of those raise button events of their own - the tip on
    // every contact, the reverse end when it lands - and neither is assignable.
    private static bool IsBarrelButton(StylusDevice device, StylusButton button)
    {
        if (button.Guid == StylusPointProperties.TipButton.Id ||
            PenBarrelButton.IsWritingTipName(button.Name))
        {
            return false;
        }

        if (button.Guid == StylusPointProperties.BarrelButton.Id)
        {
            return true;
        }

        if (button.Guid == StylusPointProperties.SecondaryTipButton.Id ||
            PenBarrelButton.IsReverseEndName(button.Name))
        {
            return false;
        }

        // A pen that names its buttons something else entirely: take the first
        // one left after the tip and the reverse end are ruled out.
        var barrel = device.StylusButtons
            .Cast<StylusButton>()
            .FirstOrDefault(item =>
                item.Guid != StylusPointProperties.TipButton.Id &&
                item.Guid != StylusPointProperties.SecondaryTipButton.Id &&
                !PenBarrelButton.IsWritingTipName(item.Name) &&
                !PenBarrelButton.IsReverseEndName(item.Name));
        return barrel is not null && ReferenceEquals(barrel, button);
    }

    private const float MouseLaserPressure = 0.5f;

    // A mouse reports a button, not a pressure. This is the neutral value the
    // straight-line constraint already draws at: exactly the configured
    // thickness, with no taper at either end. Calligraphy still varies its
    // width, because that comes from speed rather than from this number.
    private const float MousePressure = 0.5f;

    /// <summary>
    /// Whether the left button does what the active tool does. A mouse gets the
    /// tools, not the gestures: nothing here simulates pressure, hover, the
    /// reverse end or the barrel button, and the pen path is untouched.
    /// </summary>
    private bool IsMouseModeEffective =>
        _settings.MouseMode switch
        {
            MouseMode.On => true,
            MouseMode.WhenNoDigitizer => !NoDigitizerWindow.HasDrawingDevice(),
            _ => false,
        };

    private void BeginMouseInk(PointD screen)
    {
        EndPenInk();
        AppendInkPoint(screen, MousePressure);
    }

    private void UpdateMouseInk(PointD screen)
    {
        AppendInkPoint(screen, MousePressure);
        SceneSurface.PendingStroke = _penInk;
        SceneSurface.PendingStrokeStyle = _penStyle;
        SceneSurface.InvalidateVisual();
    }

    // A click that never moves is a dot, and a dot still has to be a stroke with
    // some length: points in the same place enclose nothing to render. A pen tap
    // does not have this problem - it reports a burst of packets, and no hand is
    // that still. The nudge is a hundredth of a screen pixel at the current
    // zoom, so what appears is the nib and nothing wider.
    private void EndMouseInk()
    {
        if (_penInk.Count > 0 && _penInk.TrueForAll(point => point.Position == _penInk[0].Position))
        {
            var nudge = 0.01 / Math.Max(_camera.Zoom, 0.0001);
            var last = _penInk[^1];
            _penInk.Add(last with
            {
                Position = new PointD(last.Position.X + nudge, last.Position.Y + nudge),
            });
        }

        EndPenInk();
    }

    private void InkSurface_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.StylusDevice is not null)
        {
            // Touch is owned by the stylus handlers. If this promotion is left
            // unhandled, InkCanvas treats the tap as mouse ink: a leftover
            // pen dot or highlighter square, then the stylus path pans.
            if (IsTouchDevice(e.StylusDevice))
            {
                e.Handled = true;
            }

            return;
        }

        SessionBar.CollapseIfTransient();
        CommitTextEdit();
        InkSurface.Focus();
        var screen = ToPointD(e.GetPosition(InkSurface));
        var mouseDraws = IsMouseModeEffective;

        // Ctrl is the old mouse. Letting the left button draw takes away the one
        // genuinely good thing about mouse input - moving an image without
        // leaving the Pen - so it is handed straight back on a modifier rather
        // than lost. With mouse drawing off, every left gesture is this one.
        var borrowSelect = !mouseDraws || Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

        if (e.ChangedButton is MouseButton.Middle or MouseButton.Right)
        {
            _lastPanPoint = screen;
            _mouseAction = PointerAction.Pan;

            // A sticky tool is not given back afterwards: reverting would take
            // someone who chose the Eraser and panned with the right button and
            // quietly leave them holding a pen.
            _mouseToolBorrowed = !mouseDraws;
            Mouse.Capture(InkSurface);
            e.Handled = true;
        }
        else if (e.ChangedButton == MouseButton.Left &&
                 e.ClickCount >= 2 &&
                 (borrowSelect || EffectiveTool is BoardTool.Select or BoardTool.Pan))
        {
            // Two quick dabs with an ink tool are two strokes, not a request to
            // reframe the board, so framing moves behind Ctrl exactly where the
            // left button has something else to do.
            FrameContentAt(screen);
            e.Handled = true;
        }
        else if (e.ChangedButton == MouseButton.Left && EffectiveTool == BoardTool.Laser)
        {
            BeginLaserContact(e.GetPosition(LaserTrail), MouseLaserPressure);
            _mouseAction = PointerAction.Laser;
            _mouseToolBorrowed = false;
            Mouse.Capture(InkSurface);
            e.Handled = true;
        }
        else if (e.ChangedButton == MouseButton.Left && borrowSelect)
        {
            SetActiveTool(BoardTool.Select);
            BeginContainerGesture(screen);
            _mouseAction = PointerAction.Container;
            _mouseToolBorrowed = true;
            Mouse.Capture(InkSurface);
            e.Handled = true;
        }
        else if (e.ChangedButton == MouseButton.Left)
        {
            BeginMouseAction(screen);
            Mouse.Capture(InkSurface);
            e.Handled = true;
        }
        else
        {
            // No physical mouse button is allowed to enter InkCanvas' ink path.
            e.Handled = true;
        }
    }

    // The left button under mouse drawing, branching on the tool exactly as
    // InkSurface_PreviewStylusDown does for the pen. The tool is sticky here:
    // there is nothing to hand it back to.
    private void BeginMouseAction(PointD screen)
    {
        _mouseToolBorrowed = false;
        if (EffectiveTool != BoardTool.Select)
        {
            ClearSelection();
        }

        switch (EffectiveTool)
        {
            case BoardTool.Eraser:
                BeginErase(screen);
                _mouseAction = PointerAction.Erase;
                break;
            case BoardTool.Pan:
                _lastPanPoint = screen;
                _mouseAction = PointerAction.Pan;
                break;
            case BoardTool.Select:
                BeginContainerGesture(screen);
                _mouseAction = PointerAction.Container;
                break;
            default:
                BeginMouseInk(screen);
                _mouseAction = PointerAction.Ink;
                break;
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
            if (IsTouchDevice(e.StylusDevice))
            {
                e.Handled = true;
            }

            return;
        }

        if (_penInContact)
        {
            UsePenCursor();
            return;
        }

        var screen = ToPointD(e.GetPosition(InkSurface));
        var mouseDraws = IsMouseModeEffective;
        if (EffectiveTool == BoardTool.Laser)
        {
            HidePointerDot();

            // The laser hides the cursor for pen input. A physical mouse still
            // gets the arrow, matching pen, highlighter, and calligraphy. Hide
            // it only while the mouse is drawing a trail. The pen's hover comet
            // is not borrowed here: it exists so the room can follow a pointer
            // it cannot otherwise see, and a mouse arrow is already on screen.
            if (_mouseAction == PointerAction.Laser)
            {
                InkSurface.Cursor = Cursors.None;
            }
            else
            {
                InkSurface.Cursor = Cursors.Arrow;
                LaserTrail.HideHead();
            }
        }
        else if (EffectiveTool == BoardTool.Select && _mouseAction == PointerAction.None)
        {
            HidePointerDot();
            UpdateSelectHover(screen);
            InkSurface.Cursor = SelectCursorAt(screen);
        }
        else if (mouseDraws && EffectiveTool == BoardTool.Eraser)
        {
            // What a click would erase is a patch of board rather than a point,
            // and the patch has nothing to do with the shape of an arrow.
            PointerDot.Visibility = Visibility.Collapsed;
            ShowEraserHint(e.GetPosition(RootGrid));
            InkSurface.Cursor = Cursors.None;
        }
        else if (mouseDraws && IsInkTool)
        {
            // The arrow's hotspot is its tip, so it is not inaccurate - but its
            // body covers the canvas the ink is about to land on.
            HideEraserHint();
            ShowPointerDot(e.GetPosition(RootGrid));
            InkSurface.Cursor = Cursors.None;
        }
        else
        {
            HidePointerDot();
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
            case PointerAction.Ink:
                UpdateMouseInk(screen);
                break;
        }
    }

    // Nothing else clears the mouse's own pointer dot or eraser patch: the
    // hover watchdog is the pen's, and a mouse that has moved onto the toolbar
    // simply stops reporting.
    private void InkSurface_MouseLeave(object sender, MouseEventArgs e)
    {
        if (e.StylusDevice is null && _mouseAction == PointerAction.None)
        {
            HidePointerDot();
        }
    }

    private void InkSurface_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.StylusDevice is not null)
        {
            if (IsTouchDevice(e.StylusDevice))
            {
                e.Handled = true;
            }

            return;
        }

        if (_mouseAction == PointerAction.None)
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
        else if (_mouseAction == PointerAction.Laser)
        {
            StopLaserSampling();
            LaserTrail.Lift();
        }
        else if (_mouseAction == PointerAction.Ink)
        {
            EndMouseInk();
        }

        _mouseAction = PointerAction.None;
        if (!hadMouseAction)
        {
            // CompleteMouseAction releases the capture itself and is still
            // mid-way through deciding what to do with the tool. Nothing here
            // may touch that decision.
            return;
        }

        if (_mouseToolBorrowed && EffectiveTool != BoardTool.Laser)
        {
            SetActiveTool(_lastDrawingTool);
        }
        else if (EffectiveTool == BoardTool.Laser)
        {
            InkSurface.Cursor = Cursors.Arrow;
        }

        _mouseToolBorrowed = false;
    }

    private void CompleteMouseAction(PointD screen)
    {
        var borrowed = _mouseToolBorrowed;
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
            case PointerAction.Ink:
                UpdateMouseInk(screen);
                EndMouseInk();
                break;
        }

        _mouseAction = PointerAction.None;
        if (Mouse.Captured == InkSurface)
        {
            Mouse.Capture(null);
        }

        if (borrowed && EffectiveTool != BoardTool.Laser)
        {
            SetActiveTool(_lastDrawingTool);
        }
        else if (EffectiveTool == BoardTool.Laser)
        {
            InkSurface.Cursor = Cursors.Arrow;
        }

        _mouseToolBorrowed = false;
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

    private bool IsFingerModeEffective =>
        _settings.FingerMode switch
        {
            FingerMode.On => true,
            FingerMode.WhenNoPen => !HasStylusDigitizer(),
            _ => false,
        };

    private bool IsTouchNavigating =>
        !IsFingerModeEffective || _touchNavigationLocked || _penInContact;

    private static bool HasStylusDigitizer()
    {
        foreach (TabletDevice device in Tablet.TabletDevices)
        {
            if (device.Type == TabletDeviceType.Stylus)
            {
                return true;
            }
        }

        return false;
    }

    // Finger drawing and mouse drawing are separate settings that need the same
    // two toolbar buttons, for the same reason: erasing is the pen's reverse end
    // and panning is touch or Space, and a device with neither has nowhere else
    // to reach them.
    private void ApplyPointerModes()
    {
        var fingerInk = IsFingerModeEffective;
        if (!fingerInk)
        {
            CancelFingerTool();
        }

        InkSurface.SetAllowTouchInk(fingerInk);

        var extraTools = fingerInk || IsMouseModeEffective;
        if (ExtraToolsRow is not null)
        {
            ExtraToolsRow.Visibility = extraTools ? Visibility.Visible : Visibility.Collapsed;
        }

        if (!extraTools && _activeTool is BoardTool.Eraser or BoardTool.Pan)
        {
            SetActiveTool(_lastDrawingTool);
        }
    }

    private bool TryBeginFingerTool(StylusDownEventArgs e)
    {
        InkSurface.RegisterTouchTablet(e.StylusDevice.TabletDevice.Id);

        if (_penInContact)
        {
            e.Handled = true;
            return false;
        }

        TrackTouchPoint(e);

        if (!IsFingerModeEffective)
        {
            InkSurface.AbortWetInk();
            e.StylusDevice.Capture(InkSurface);
            e.Handled = true;
            return false;
        }

        if (_touchPoints.Count > 1 || _touchNavigationLocked)
        {
            CancelFingerTool();
            _touchNavigationLocked = true;
            CaptureTouchDevices();
            e.Handled = true;
            return false;
        }

        _fingerToolDeviceId = e.StylusDevice.Id;
        HidePointerDot();
        return true;
    }

    private void TrackTouchPoint(StylusEventArgs e)
    {
        _touchPoints[e.StylusDevice.Id] = e.GetPosition(InkSurface);
        _touchDevices[e.StylusDevice.Id] = e.StylusDevice;
    }

    private void CaptureTouchDevices()
    {
        foreach (var device in _touchDevices.Values)
        {
            device.Capture(InkSurface);
        }
    }

    private void CancelFingerTool()
    {
        if (_fingerToolDeviceId is null && _stylusAction == PointerAction.None)
        {
            return;
        }

        _discardInkStroke = true;
        InkSurface.AbortWetInk();
        switch (_stylusAction)
        {
            case PointerAction.Erase:
                CompleteErase();
                break;
            case PointerAction.Container:
                CompleteContainerGesture();
                break;
            case PointerAction.Laser:
                StopLaserSampling();
                LaserTrail.Lift();
                break;
        }

        if (_stylusAction != PointerAction.None)
        {
            ReleaseBoardPointerCapture();
        }

        _stylusAction = PointerAction.None;
        _fingerToolDeviceId = null;
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

    private void EndTouchTracking(StylusEventArgs e, bool navigating)
    {
        var id = e.StylusDevice.Id;
        _touchPoints.Remove(id);
        _touchDevices.Remove(id);
        if (_fingerToolDeviceId == id)
        {
            _fingerToolDeviceId = null;
        }

        if (_touchPoints.Count == 0)
        {
            _touchNavigationLocked = false;
        }

        if (navigating)
        {
            e.StylusDevice.Capture(null);
            e.Handled = true;
        }
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
        _touchNavigationLocked = false;
        _fingerToolDeviceId = null;
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
        var radius = EraserScreenRadius / _camera.Zoom;
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

    private static BoardObject WithZIndex(BoardObject item, int zIndex) => item switch
    {
        ImageBoardObject image => image with { ZIndex = zIndex },
        TextBoardObject text => text with { ZIndex = zIndex },
        LiveViewBoardObject liveView => liveView with { ZIndex = zIndex },
        InkStrokeObject stroke => stroke with { ZIndex = zIndex },
        _ => throw new NotSupportedException($"Unsupported object type {item.GetType().Name}."),
    };

    private BoardObject? GetSelectedContainer()
    {
        if (_selectedObjectId is not Guid selectedId)
        {
            return null;
        }

        BoardObject? item = _document.Objects.FirstOrDefault(candidate => candidate.Id == selectedId);
        return item is IBoardContainer ? item : null;
    }

    private void UpdateZOrderCommands()
    {
        if (_textEditBefore is not null || GetSelectedContainer() is not { } container)
        {
            SessionBar.SetZOrderEnabled(canBringToFront: false, canSendToBack: false);
            return;
        }

        BoardObject[] group = _document.GetDeletionGroup(container.Id)
            .OrderBy(item => item.ZIndex)
            .ToArray();
        SessionBar.SetZOrderEnabled(
            canBringToFront: !IsZGroupAtFront(group),
            canSendToBack: !IsZGroupAtBack(group));
    }

    private bool IsZGroupAtFront(IReadOnlyList<BoardObject> group)
    {
        HashSet<Guid> ids = group.Select(item => item.Id).ToHashSet();
        int maxGroup = group.Max(item => item.ZIndex);
        return _document.Objects.Where(item => !ids.Contains(item.Id))
            .All(item => item.ZIndex < maxGroup);
    }

    private bool IsZGroupAtBack(IReadOnlyList<BoardObject> group)
    {
        HashSet<Guid> ids = group.Select(item => item.Id).ToHashSet();
        int minGroup = group.Min(item => item.ZIndex);
        return _document.Objects.Where(item => !ids.Contains(item.Id))
            .All(item => item.ZIndex > minGroup);
    }

    private void BringSelectedContainerToFront()
    {
        ReorderSelectedContainer(toFront: true);
    }

    private void SendSelectedContainerToBack()
    {
        ReorderSelectedContainer(toFront: false);
    }

    private void ReorderSelectedContainer(bool toFront)
    {
        if (GetSelectedContainer() is not { } container)
        {
            return;
        }

        BoardObject[] before = _document.GetDeletionGroup(container.Id)
            .OrderBy(item => item.ZIndex)
            .ToArray();
        if (before.Length == 0 ||
            (toFront ? IsZGroupAtFront(before) : IsZGroupAtBack(before)))
        {
            return;
        }

        HashSet<Guid> ids = before.Select(item => item.Id).ToHashSet();
        IEnumerable<int> otherZ = _document.Objects
            .Where(item => !ids.Contains(item.Id))
            .Select(item => item.ZIndex);
        int start = toFront
            ? otherZ.DefaultIfEmpty(-1).Max() + 1
            : otherZ.DefaultIfEmpty(0).Min() - before.Length;
        BoardObject[] after = before
            .Select((item, index) => WithZIndex(item, start + index))
            .ToArray();
        if (before.SequenceEqual(after))
        {
            return;
        }

        _history.Execute(new ReplaceObjectsCommand(before, after), _document);
        UpdateZOrderCommands();
    }

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

    private void FormatSelectedText()
    {
        if (GetSelectedText() is not { } textObject)
        {
            return;
        }

        ITextLanguageService language = TextLanguageRegistry.Resolve(textObject.LanguageId);
        if (!language.CanFormat ||
            !language.TryFormat(textObject.Text, out string formatted) ||
            string.Equals(textObject.Text, formatted, StringComparison.Ordinal))
        {
            return;
        }

        double desiredHeight = TextContainerVisual.MeasureDesiredHeight(
            formatted,
            textObject.Bounds.Width,
            textObject.VisualScale,
            VisualTreeHelper.GetDpi(SceneSurface).PixelsPerDip,
            language.Id);
        RectD bounds = desiredHeight > textObject.Bounds.Height
            ? textObject.Bounds.WithSize(textObject.Bounds.Width, desiredHeight)
            : textObject.Bounds;
        var after = textObject with
        {
            Text = formatted,
            Bounds = bounds,
        };
        InkStrokeObject[] linkedBefore = _document.LinkedStrokes(textObject.Id).ToArray();
        InkStrokeObject[] linkedAfter = textObject.Bounds == bounds
            ? linkedBefore
            : linkedBefore
                .Select(stroke => stroke.TransformWithContainer(textObject.Bounds, bounds))
                .ToArray();
        _history.Execute(
            new ReplaceObjectsCommand(
                [textObject, .. linkedBefore],
                [after, .. linkedAfter]),
            _document);
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

        SessionBar.SetEditEnabled(TextEditor.CanUndo, TextEditor.CanRedo);
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

    private void EraserToolButton_Click(object sender, RoutedEventArgs e)
    {
        LeaveLaserIfActive();
        SetActiveTool(BoardTool.Eraser);
    }

    private void SelectToolButton_Click(object sender, RoutedEventArgs e)
    {
        LeaveLaserIfActive();
        SetActiveTool(BoardTool.Select);
    }

    private void PanToolButton_Click(object sender, RoutedEventArgs e)
    {
        LeaveLaserIfActive();
        SetActiveTool(BoardTool.Pan);
    }

    private BoardTool EffectiveTool =>
        _spaceTemporaryPan ? BoardTool.Pan : _activeTool;

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
        }

        var penFamilyActive = tool is BoardTool.Pen or BoardTool.Calligraphy;
        PenToolButton.IsChecked = penFamilyActive;
        HighlighterToolButton.IsChecked = tool == BoardTool.Highlighter;
        SelectToolButton.IsChecked = tool == BoardTool.Select;
        if (EraserToolButton is not null)
        {
            EraserToolButton.IsChecked = tool == BoardTool.Eraser;
        }

        if (PanToolButton is not null)
        {
            PanToolButton.IsChecked = tool == BoardTool.Pan;
        }

        UpdateDualToolChecks();
        // Ink here is the finger's; the pen's is collected by AppendPenInk. A
        // stylus reading as inverted is a pen the wrong way round, and erasing
        // is ours to do, so the InkCanvas is given nothing to do with it.
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
        LaserTrail.TrailWeight = laser.TrailWeight;
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
        if (_barrelToolTemporary)
        {
            _barrelToolTemporary = false;
            _barrelButton = null;
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

    private void ApplyPreferences()
    {
        ApplyToolbarPlacement();
        ApplyCalligraphyAccess();
        ApplyLaserSettings();
        ApplyPointerModes();
        if (!_settings.CheckForUpdates)
        {
            SessionBar.HideUpdateNotice();
        }

        PersistSettings();
        _ = CheckForUpdatesAsync();
    }

    private async Task CheckForUpdatesAsync()
    {
        if (StorePackage.IsStoreInstall || !_settings.CheckForUpdates)
        {
            return;
        }

        OfferKnownUpdate();
        if (_settings.LastUpdateCheckUtc is { } last &&
            DateTimeOffset.UtcNow - last < TimeSpan.FromHours(24))
        {
            return;
        }

        try
        {
            var result = await UpdateCheckClient.CheckAsync(
                _settings.UpdateCheckETag,
                CancellationToken.None);
            if (result is null)
            {
                return;
            }

            _settings.LastUpdateCheckUtc = DateTimeOffset.UtcNow;
            if (!result.NotModified)
            {
                if (result.ETag is not null)
                {
                    _settings.UpdateCheckETag = result.ETag;
                }

                if (result.Version is not null)
                {
                    _settings.LatestKnownVersion = result.Version;
                }
            }

            PersistSettings();
            OfferKnownUpdate();
        }
        catch (Exception exception)
        {
            Debug.WriteLine("[Update] " + exception.Message);
        }
    }

    private void OfferKnownUpdate()
    {
        if (!_settings.CheckForUpdates ||
            !UpdateVersion.IsNewer(AppVersion.Informational, _settings.LatestKnownVersion) ||
            string.Equals(
                _settings.LastDismissedVersion,
                _settings.LatestKnownVersion,
                StringComparison.Ordinal))
        {
            return;
        }

        SessionBar.ShowUpdateNotice(_settings.LatestKnownVersion!);
    }

    private void SessionBar_UpdateDismissed(string version)
    {
        _settings.LastDismissedVersion = version;
        PersistSettings();
    }

    private static void OpenUpdateDownload()
    {
        Process.Start(new ProcessStartInfo(UpdateCheckClient.DownloadUrl)
        {
            UseShellExecute = true,
        });
    }

    private void PreferencesMenuItem_Click(object sender, RoutedEventArgs e) =>
        ShowOwnedDialog(new PreferencesWindow(_settings, ApplyPreferences));

    private void SessionChrome_CommandRequested(SessionCommand command)
    {
        switch (command)
        {
            case SessionCommand.New:
                NewMenuItem_Click(this, new RoutedEventArgs());
                break;
            case SessionCommand.Open:
                OpenButton_Click(this, new RoutedEventArgs());
                break;
            case SessionCommand.Save:
                SaveButton_Click(this, new RoutedEventArgs());
                break;
            case SessionCommand.SaveAs:
                SaveAsMenuItem_Click(this, new RoutedEventArgs());
                break;
            case SessionCommand.Close:
                Close();
                break;
            case SessionCommand.Undo:
                UndoButton_Click(this, new RoutedEventArgs());
                break;
            case SessionCommand.Redo:
                RedoButton_Click(this, new RoutedEventArgs());
                break;
            case SessionCommand.Copy:
                CopySelectionToClipboard();
                break;
            case SessionCommand.Paste:
                PasteButton_Click(this, new RoutedEventArgs());
                break;
            case SessionCommand.FullScreen:
                SetChromeMode(
                    _chromeMode == SessionChromeMode.FullScreen
                        ? SessionChromeMode.Windowed
                        : SessionChromeMode.FullScreen);
                break;
            case SessionCommand.CanvasOnly:
                SetChromeMode(
                    _chromeMode == SessionChromeMode.CanvasOnly
                        ? SessionChromeMode.Windowed
                        : SessionChromeMode.CanvasOnly);
                break;
            case SessionCommand.BringToFront:
                BringSelectedContainerToFront();
                break;
            case SessionCommand.SendToBack:
                SendSelectedContainerToBack();
                break;
            case SessionCommand.AddLiveView:
                AddLiveViewMenuItem_Click(this, new RoutedEventArgs());
                break;
            case SessionCommand.FreezeLiveView:
                FreezeLiveViewMenuItem_Click(this, new RoutedEventArgs());
                break;
            case SessionCommand.DisconnectLiveView:
                DisconnectSelectedLiveView();
                break;
            case SessionCommand.ReconnectLiveView:
                ReconnectLiveViewMenuItem_Click(this, new RoutedEventArgs());
                break;
            case SessionCommand.Preferences:
                PreferencesMenuItem_Click(this, new RoutedEventArgs());
                break;
            case SessionCommand.About:
                ShowAbout();
                break;
        }
    }

    private void ShowAbout() => ShowOwnedDialog(new AboutWindow());

    private void ShowOwnedDialog(Window dialog)
    {
        SessionBar.Collapse();
        CloseLanguagePickers();
        UpdateLayout();
        dialog.Owner = this;
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
        ApplySessionTabRow();
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

    private void DisconnectSelectedLiveView()
    {
        if (GetSelectedLiveView() is not { } liveView ||
            !_liveViewPresenters.TryGetValue(liveView.Id, out LiveViewPresenter? presenter) ||
            !presenter.HasTarget)
        {
            return;
        }

        try
        {
            LiveViewBoardObject updated = SaveLiveViewSnapshot(
                liveView with { IsFrozen = true },
                presenter);
            presenter.ClearTarget();
            DetachLiveViewSurface(presenter);
            _document.ReplaceObject(updated);
            UpdateLiveViewMenuItems();
            UpdateLiveViewActionOverlay();
        }
        catch (Exception exception)
        {
            ShowError("Could not disconnect LiveView", exception);
        }
    }

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
                "Importable files|*.wimport;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.svg|Whiteboard import|*.wimport|Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.svg",
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

    private async Task PasteFromClipboardAsync()
    {
        try
        {
            string[] files = ClipboardImage.GetImportableFiles();
            if (files.Length > 0)
            {
                await ImportDroppedFilesAsync(files, _camera.Center);
                return;
            }

            // Ahead of the bitmap: an application that offers both is offering the same
            // picture twice, and only one of the two survives being enlarged.
            byte[]? svg = ClipboardImage.TryGetSvgBytes();
            if (svg is not null)
            {
                AddImage(
                    svg,
                    "clipboard-image" + DroppedFileImport.SvgExtension,
                    DroppedFileImport.SvgContentType);
                return;
            }

            byte[]? png = ClipboardImage.TryGetEncodedPng();
            if (png is not null)
            {
                AddImage(png, "clipboard-image.png", "image/png");
                return;
            }

            if (Clipboard.ContainsText(TextDataFormat.UnicodeText))
            {
                string text = Clipboard.GetText(TextDataFormat.UnicodeText);
                AddText(text, languageId: ResolveSnippetLanguage(text));
            }
        }
        catch (Exception exception)
        {
            ShowError("Could not paste clipboard content", exception);
        }
    }

    private void AddImage(
        byte[] bytes,
        string fileName,
        string contentType,
        PointD? worldCenter = null)
    {
        var decoded = BoardImageCodec.Decode(bytes);
        var assetId = Guid.NewGuid().ToString("N");
        _document.AddAsset(new BoardAsset(assetId, fileName, contentType, bytes));
        SceneSurface.InvalidateAssets();

        var (width, height) = BoardImageCodec.ArrivalSize(decoded);
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
        bool beginEdit = false,
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
        string resolvedLanguageId = TextLanguageIds.Normalize(languageId);
        double height = TextContainerVisual.MeasureDesiredHeight(
            text,
            width,
            1,
            VisualTreeHelper.GetDpi(SceneSurface).PixelsPerDip,
            resolvedLanguageId);
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
            LanguageId: resolvedLanguageId);

        _history.Execute(new AddObjectCommand(textObject), _document);
        _selectedObjectId = textObject.Id;
        SceneSurface.SelectedObjectId = textObject.Id;
        SetActiveTool(BoardTool.Select);
        if (beginEdit)
        {
            BeginTextEdit(textObject);
            return;
        }

        UpdateLiveViewActionOverlay();
    }

    private string ResolveSnippetLanguage(string text) =>
        TextLanguageRegistry.ResolveFromOrder(text, _settings.SnippetFormatOrder);

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
        WinRtThreading.EnsureDispatcherQueue();
        GraphicsCapturePicker picker = new();
        try
        {
            nint windowHandle = new WindowInteropHelper(this).Handle;
            WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
            return await picker.PickSingleItemAsync();
        }
        finally
        {
            WinRtThreading.Release(picker);
        }
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
        if (liveView is null ||
            !_liveViewPresenters.TryGetValue(liveView.Id, out LiveViewPresenter? presenter))
        {
            SessionBar.SetLiveViewCommands(selected: liveView is not null, hasTarget: false, frozen: false);
            return;
        }

        SessionBar.SetLiveViewCommands(
            selected: true,
            hasTarget: presenter.HasTarget,
            frozen: presenter.IsFrozen);
    }

    private void UpdateLiveViewActionOverlay()
    {
        UpdateLiveViewMenuItems();
        LiveViewBoardObject? liveView = GetSelectedLiveView();
        if (liveView is null ||
            !_liveViewPresenters.TryGetValue(liveView.Id, out LiveViewPresenter? overlayPresenter) ||
            !overlayPresenter.HasTarget)
        {
            LiveViewActionsBorder.Visibility = Visibility.Collapsed;
            UpdateLanguageChipOverlay();
            UpdateZOrderCommands();
            return;
        }

        PointD topLeft = _camera.WorldToScreen(
            new PointD(liveView.Bounds.Left, liveView.Bounds.Top));
        PointD bottomRight = _camera.WorldToScreen(
            new PointD(liveView.Bounds.Right, liveView.Bounds.Bottom));

        const double overlayWidth = 104;
        const double overlayHeight = 48;
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
        UpdateLanguageChipOverlay();
        UpdateZOrderCommands();
    }

    private TextBoardObject? GetSelectedText() =>
        _selectedObjectId is Guid selectedId
            ? _document.Objects.FirstOrDefault(item => item.Id == selectedId) as TextBoardObject
            : null;

    private void UpdateLanguageChipOverlay()
    {
        TextBoardObject? textObject = GetSelectedText();
        if (textObject is null || _textEditBefore is not null)
        {
            HideLanguageChip();
            return;
        }

        PointD topLeft = _camera.WorldToScreen(
            new PointD(textObject.Bounds.Left, textObject.Bounds.Top));
        PointD bottomRight = _camera.WorldToScreen(
            new PointD(textObject.Bounds.Right, textObject.Bounds.Bottom));
        double width = Math.Max(1, bottomRight.X - topLeft.X);
        double height = Math.Max(1, bottomRight.Y - topLeft.Y);
        const double chipWidth = TextContainerVisual.LanguageChipWidth;
        const double chipHeight = TextContainerVisual.LanguageChipHeight;
        const double margin = TextContainerVisual.LanguageChipMargin;
        if (width < chipWidth + (margin * 2) || height < chipHeight)
        {
            HideLanguageChip();
            return;
        }

        double scale = Math.Max(0.01, textObject.VisualScale * _camera.Zoom);
        double titleHeight = Math.Min(height, TextContainerVisual.TitleBarHeight * scale);
        double left = bottomRight.X - margin - chipWidth;
        double top = topLeft.Y + Math.Max(0, (titleHeight - chipHeight) / 2);
        if (LanguageChipCombo.IsDropDownOpen &&
            LanguageChipCombo.Visibility == Visibility.Visible &&
            (Math.Abs(Canvas.GetLeft(LanguageChipCombo) - left) > 0.5 ||
             Math.Abs(Canvas.GetTop(LanguageChipCombo) - top) > 0.5))
        {
            LanguageChipCombo.IsDropDownOpen = false;
        }

        Canvas.SetLeft(LanguageChipCombo, left);
        Canvas.SetTop(LanguageChipCombo, top);

        ITextLanguageService language = TextLanguageRegistry.Resolve(textObject.LanguageId);
        if (!ReferenceEquals(LanguageChipCombo.SelectedItem, language))
        {
            _updatingLanguageChip = true;
            try
            {
                LanguageChipCombo.SelectedItem = language;
            }
            finally
            {
                _updatingLanguageChip = false;
            }
        }

        LanguageChipCombo.Visibility = Visibility.Visible;
    }

    private void HideLanguageChip()
    {
        LanguageChipCombo.IsDropDownOpen = false;
        LanguageChipCombo.Visibility = Visibility.Collapsed;
    }

    private void CloseLanguagePickers()
    {
        LanguageChipCombo.IsDropDownOpen = false;
        TextEditorLanguageCombo.IsDropDownOpen = false;
    }

    private void LanguageChipCombo_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.StylusDevice is not null)
        {
            return;
        }

        SessionBar.CollapseIfTransient();
        if (e.ChangedButton != MouseButton.Left || e.ClickCount < 2)
        {
            return;
        }

        LanguageChipCombo.IsDropDownOpen = false;
        FrameContentAt(ToPointD(e.GetPosition(InkSurface)));
        e.Handled = true;
    }

    private void LanguageChipCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingLanguageChip ||
            LanguageChipCombo.SelectedItem is not ITextLanguageService language ||
            GetSelectedText() is not { } textObject)
        {
            return;
        }

        if (TextLanguageIds.Normalize(textObject.LanguageId) == language.Id)
        {
            return;
        }

        ApplyTextLanguage(textObject, language);
        InkSurface.Focus();
    }

    private void ApplyTextLanguage(TextBoardObject textObject, ITextLanguageService language)
    {
        double desiredHeight = TextContainerVisual.MeasureDesiredHeight(
            textObject.Text,
            textObject.Bounds.Width,
            textObject.VisualScale,
            VisualTreeHelper.GetDpi(SceneSurface).PixelsPerDip,
            language.Id);
        RectD bounds = desiredHeight > textObject.Bounds.Height
            ? textObject.Bounds.WithSize(textObject.Bounds.Width, desiredHeight)
            : textObject.Bounds;
        var after = textObject with
        {
            LanguageId = language.Id,
            Bounds = bounds,
        };
        InkStrokeObject[] linkedBefore = _document.LinkedStrokes(textObject.Id).ToArray();
        InkStrokeObject[] linkedAfter = textObject.Bounds == bounds
            ? linkedBefore
            : linkedBefore
                .Select(stroke => stroke.TransformWithContainer(textObject.Bounds, bounds))
                .ToArray();
        _history.Execute(
            new ReplaceObjectsCommand(
                [textObject, .. linkedBefore],
                [after, .. linkedAfter]),
            _document);
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

            BoardImage copied = BoardImageCodec.Decode(asset.Data);
            if (!copied.IsVector)
            {
                Clipboard.SetImage(BoardImageCodec.Rasterize(copied));
                return;
            }

            var flattened = BoardImageCodec.Rasterize(copied, selected.Bounds);

            // A vector goes out as both, so an editor receives the markup and a slide
            // receives a picture, and pasting it back into a board keeps it a vector.
            var vector = new DataObject();
            vector.SetData(
                DroppedFileImport.SvgContentType,
                new MemoryStream(asset.Data, writable: false));
            vector.SetText(
                System.Text.Encoding.UTF8.GetString(asset.Data),
                TextDataFormat.UnicodeText);
            vector.SetImage(flattened);
            Clipboard.SetDataObject(vector, copy: true);
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

                        byte[] bytes = await File.ReadAllBytesAsync(path);
                        if (!DroppedFileImport.LooksLikeText(bytes))
                        {
                            continue;
                        }

                        string droppedText = System.Text.Encoding.UTF8.GetString(bytes);
                        string droppedLanguage = DroppedFileImport.HasRecognizedLanguageExtension(path)
                            ? DroppedFileImport.LanguageIdFor(path)
                            : ResolveSnippetLanguage(droppedText);
                        AddText(
                            droppedText,
                            center,
                            beginEdit: false,
                            title: Path.GetFileNameWithoutExtension(path),
                            languageId: droppedLanguage);
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
                    var (width, height) = BoardImageCodec.ArrivalSize(
                        BoardImageCodec.Decode(item.ImageBytes));
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
        var modifiers = Keyboard.Modifiers;
        var controlDown = modifiers.HasFlag(ModifierKeys.Control);
        var shiftDown = modifiers.HasFlag(ModifierKeys.Shift);
        var altDown = modifiers.HasFlag(ModifierKeys.Alt) || e.Key == Key.System;

        if (e.Key == Key.F11 || (e.Key == Key.System && e.SystemKey == Key.F11))
        {
            if (!e.IsRepeat)
            {
                SetChromeMode(
                    controlDown
                        ? (_chromeMode == SessionChromeMode.CanvasOnly
                            ? SessionChromeMode.Windowed
                            : SessionChromeMode.CanvasOnly)
                        : (_chromeMode == SessionChromeMode.FullScreen
                            ? SessionChromeMode.Windowed
                            : SessionChromeMode.FullScreen));
            }

            e.Handled = true;
            return;
        }

        if (SessionBar.IsCommandRowOpen && e.Key == Key.Escape)
        {
            SessionBar.Collapse();
            e.Handled = true;
            return;
        }

        var mnemonicKey = e.Key == Key.System ? e.SystemKey : e.Key;
        if (altDown &&
            !controlDown &&
            _chromeMode == SessionChromeMode.Windowed &&
            !e.IsRepeat &&
            SessionBar.TryHandleAltKey(mnemonicKey))
        {
            e.Handled = true;
            return;
        }

        if (SessionBar.IsCommandRowOpen && !controlDown && !altDown)
        {
            var isMove = mnemonicKey is Key.Left or Key.Right or Key.Home or Key.End;
            if ((isMove || !e.IsRepeat) && SessionBar.TryHandleCommandKey(mnemonicKey))
            {
                e.Handled = true;
                return;
            }
        }
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

        if (_chromeMode != SessionChromeMode.Windowed && e.Key == Key.Escape)
        {
            SetChromeMode(SessionChromeMode.Windowed);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F6 && !e.IsRepeat)
        {
            FormatSelectedText();
            e.Handled = true;
        }
        else if (e.Key == Key.F2 &&
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

    private void SetChromeMode(SessionChromeMode mode)
    {
        if (_chromeMode == mode)
        {
            return;
        }

        SessionBar.Collapse();
        if (mode == SessionChromeMode.Windowed)
        {
            _chromeMode = SessionChromeMode.Windowed;
            WindowChrome.SetWindowChrome(this, null);
            ApplySessionTabRow();
            RestoreWindowedChrome();
            return;
        }

        if (_chromeMode == SessionChromeMode.Windowed)
        {
            SnapshotWindowedChrome();
        }

        WindowState = WindowState.Normal;
        SessionBar.Visibility = Visibility.Collapsed;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(0),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });
        if (mode == SessionChromeMode.FullScreen)
        {
            MonitorStartupPlacement.FillCurrentMonitor(this);
        }
        else if (_chromeMode == SessionChromeMode.FullScreen)
        {
            RestoreWindowedBounds();
        }

        _chromeMode = mode;
        ApplySessionTabRow();
    }

    private void ApplySessionTabRow()
    {
        var windowed = _chromeMode == SessionChromeMode.Windowed;
        SessionTabRow.Height = windowed
            ? new GridLength(SessionTabHeight)
            : new GridLength(0);

        // Measured from the window top so full screen keeps the same 16px
        // inset the palette has from the tab strip in a windowed session.
        var top = windowed && ToolPalette.VerticalAlignment == VerticalAlignment.Top
            ? SessionTabHeight + ToolPaletteInset
            : ToolPaletteInset;
        ToolPalette.Margin = new Thickness(
            ToolPaletteInset,
            top,
            ToolPaletteInset,
            ToolPaletteInset);
    }

    private void SnapshotWindowedChrome()
    {
        _windowStateBeforeFullScreen = WindowState;
        _windowStyleBeforeFullScreen = WindowStyle;
        _resizeModeBeforeFullScreen = ResizeMode;
        _windowBoundsBeforeFullScreen = new Rect(Left, Top, Width, Height);
    }

    private void RestoreWindowedChrome()
    {
        WindowState = WindowState.Normal;
        SessionBar.Visibility = Visibility.Visible;
        WindowStyle = _windowStyleBeforeFullScreen;
        ResizeMode = _resizeModeBeforeFullScreen;
        RestoreWindowedBounds();
        WindowState = _windowStateBeforeFullScreen;
    }

    private void RestoreWindowedBounds()
    {
        if (_windowStateBeforeFullScreen == WindowState.Normal)
        {
            Left = _windowBoundsBeforeFullScreen.Left;
            Top = _windowBoundsBeforeFullScreen.Top;
            Width = _windowBoundsBeforeFullScreen.Width;
            Height = _windowBoundsBeforeFullScreen.Height;
        }
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
        _mouseToolBorrowed = false;
        _penInContact = false;
        _syntheticLaserContact = false;
        _barrelButton = null;
        EndPenInk();
        ClearTouchNavigation();
        InkSurface.Cursor = Cursors.Arrow;
        HidePointerDot();
        StopLaserSampling();
        LaserTrail.HideHead();
        EndTemporaryBarrelTool();
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
        if (!IsTouchStylus(e))
        {
            PenTrace.Write("in-range", e, PenTraceState());
        }

        if (!_penInContact)
        {
            UpdateHoverPointerDot(e);
        }
    }

    private void Window_PreviewStylusOutOfRange(object sender, StylusEventArgs e)
    {
        if (!IsTouchStylus(e))
        {
            EndPenInk();
            HidePointerDot();
        }
    }

    // The pen reports the same way in the air as in contact - and while a barrel
    // button is held it reports in the air the whole time, pressure and all. The
    // packets are the same packets; only WPF's opinion of them differs.
    private void Window_PreviewStylusInAirMove(object sender, StylusEventArgs e)
    {
        if (!IsTouchStylus(e))
        {
            PenTrace.Write("air-move", e, PenTraceState());
            AppendPenInk(e);
            if (_penInk.Count > 0)
            {
                return;
            }

            _penInContact = false;
        }

        UpdateHoverPointerDot(e);
    }

    private string PenTraceState() =>
        $"contact={_penInContact} barrel={_barrelButton is not null} " +
        $"temporary={_barrelToolTemporary} action={_stylusAction} tool={EffectiveTool}";

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

    // Assigning Cursor only takes effect at the next cursor query, and a pen
    // held still raises none - the barrel button can change the tool without any
    // pointer movement at all. Without the refresh the previous cursor stays on
    // screen until something else moves, which is why a tap appeared to fix it.
    private void UsePenCursor()
    {
        InkSurface.Cursor = EffectiveTool is BoardTool.Select
            ? Cursors.Arrow
            : Cursors.None;
        Mouse.UpdateCursor();
    }

    // Contact packets arrive as StylusMove; hover is StylusInAirMove. Wacom
    // Cintiqs also fire OutOfRange when the pen leaves detection, which is
    // not the same as leaving the InkCanvas hit-test bounds.
    private void UpdateHoverPointerDot(StylusEventArgs e)
    {
        if (IsTouchStylus(e))
        {
            HidePointerDot();
            return;
        }

        // A reversed pen used to be dropped here. It erases on contact, so it
        // has more to show while hovering than any other pose, not less.
        _penInverted = e.StylusDevice.Inverted;
        if (_penInContact)
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
        if (IsErasing)
        {
            // What a tap would erase is a patch of board, not a point, so the
            // pointer shows the patch. A dot would say nothing about reach.
            PointerDot.Visibility = Visibility.Collapsed;
            LaserTrail.EndHover();
            ShowEraserHint(rootPosition);
        }
        else if (EffectiveTool == BoardTool.Laser)
        {
            // The laser is the same instrument in the air as on the glass, so
            // hover drives the trail surface rather than the plain hover dot.
            // HidePointerDot is not used here: it ends the hover it is about to
            // be handed.
            PointerDot.Visibility = Visibility.Collapsed;
            LaserTrail.Hover(RootGrid.TranslatePoint(rootPosition, LaserTrail));
        }
        else
        {
            // Switching tools mid-hover has to take the comet with it; the pen
            // is still in range, so the hover watchdog would never fire.
            LaserTrail.EndHover();
            HideEraserHint();
            ShowPointerDot(rootPosition);
        }

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

    // Every path that means "the pen is no longer over the board" comes through
    // here, including the hover watchdog, so the laser comet is cleared here too
    // rather than at each of those call sites.
    private void HidePointerDot()
    {
        PointerDot.Visibility = Visibility.Collapsed;
        LaserTrail.EndHover();
        HideEraserHint();
        _hoverWatch.Stop();
    }

    // Reversing the pen erases without changing the selected tool, so the tool
    // alone does not answer what a tap would do here.
    private bool IsErasing => _penInverted || EffectiveTool == BoardTool.Eraser;

    private void ShowEraserHint(Point position)
    {
        var side = EraserScreenRadius * 2;
        EraserHint.Width = side;
        EraserHint.Height = side;
        EraserHintTransform.X = position.X - (side / 2);
        EraserHintTransform.Y = position.Y - (side / 2);
        EraserHint.Visibility = Visibility.Visible;
    }

    private void HideEraserHint() => EraserHint.Visibility = Visibility.Collapsed;

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
        IsTouchDevice(e.StylusDevice);

    private static bool IsTouchDevice(StylusDevice? device) =>
        device?.TabletDevice?.Type == TabletDeviceType.Touch;

    private static string ContentTypeFor(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".bmp" => "image/bmp",
            ".gif" => "image/gif",
            DroppedFileImport.SvgExtension => DroppedFileImport.SvgContentType,
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
