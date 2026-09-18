using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
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
using SQLBI.Whiteboard.Export;
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
        Shape,
        Connector,
        Text,
    }

    private enum PointerAction
    {
        None,
        Erase,
        Pan,
        Container,
        Laser,
        Ink,
        Shape,
        Connector,
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

    // The history save point answers for everything that arrives as a command, undo
    // included. It cannot answer for the dozen places that change the document directly -
    // a container gesture settling, an asset arriving with a pasted image - so those say so
    // here. The LiveView paths deliberately do not: a frame turning up on its own is not
    // somebody changing the board, and would otherwise put an unasked question on the way
    // out of an application left running beside a feed.
    private bool _dirtyOutsideHistory;

    private readonly SessionStore? _session = SessionStore.Acquire();
    private readonly DispatcherTimer _autosaveTimer;
    private bool _closeConfirmed;
    private bool _autosaveRunning;

    /// <summary>
    /// Whether anything has changed since the last autosave. Without it the timer would
    /// rewrite the same board every interval for as long as the application is open, since
    /// an autosave deliberately does not make the board count as saved.
    /// </summary>
    private bool _autosaveDirty;

    /// <summary>
    /// How often the board is copied into the session slot while it is being worked on. The
    /// copy exists for a crash and for nothing else, so this is set by how much ink somebody
    /// would mind redrawing rather than by what the disk could keep up with.
    /// </summary>
    private static readonly TimeSpan AutosaveInterval = TimeSpan.FromSeconds(30);
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
    private bool _mouseModeOffered;
    private PointD _lastPanPoint;
    private bool _penInContact;
    private bool _touchNavigationLocked;
    private int? _fingerToolDeviceId;
    private bool _spaceTemporaryPan;

    /// <summary>
    /// What is selected. A single selection is a set of one, so every gesture,
    /// command, and overlay reads one thing rather than branching on how many.
    /// </summary>
    private readonly HashSet<Guid> _selectedObjectIds = [];

    // A select gesture moves or scales the whole selection, plus the strokes
    // linked to a selected container that are not themselves selected. Both
    // lists are whole objects rather than ids, so one ReplaceObjectsCommand
    // records the gesture and an undo puts every one of them back.
    private BoardObject[] _gestureBefore = [];
    private BoardObject[] _gestureAfter = [];
    private RectD _gestureBounds;
    private RectD _gestureAfterBounds;
    private PointD _gestureStartWorld;
    private bool _gestureIsResize;
    private bool _gestureFreeResize;
    private bool _gestureReflow;
    private TextBoardObject? _gestureReflowTarget;

    // A press on empty canvas with Select draws a rubber band or a lasso. It
    // only becomes a selection on release, because a press that never moves is
    // a tap, and a tap on empty canvas clears.
    private bool _areaActive;
    private bool _areaExtends;
    private bool _areaDragged;
    private PointD _areaStartScreen;
    private readonly List<PointD> _areaPoints = [];

    // A press with the Shape tool drags a shape out from where it started. A
    // press that never moves is a tap, and a tap inserts one at a readable size
    // rather than a shape with no area at all.
    private ShapeKind _shapeKind = ShapeKind.RoundedRectangle;
    private bool _shapeActive;
    private bool _shapeDragged;
    private PointD _shapeStartScreen;
    private PointD _shapeStartWorld;

    // A press with a connector tool drags the line out from where it started.
    // A press that never moves makes nothing: a connector with no length says
    // nothing about what it joins.
    private ConnectorKind _connectorKind = ConnectorKind.Arrow;
    private bool _connectorActive;
    private bool _connectorDragged;
    private PointD _connectorStartScreen;
    private PointD _connectorStartWorld;

    // Dragging one end of a selected connector, which re-routes it and can
    // re-bind it. It is the gesture the corner handle would otherwise be.
    private ConnectorBoardObject? _endpointBefore;
    private bool _endpointIsStart;
    private PointD _endpointWorld;

    // Dragging the rotation handle of a lone shape or label. The angle follows
    // the hand, offset by where it took hold so the object does not jump, and
    // the object is turned from the one it was when the press started rather
    // than from the one the last move left.
    private BoardObject? _rotationBefore;
    private InkStrokeObject[] _rotationStrokes = [];
    private ConnectorBoardObject[] _rotationConnectors = [];
    private double _rotationStartAngle;
    private double _rotationGrabAngle;
    private double _rotationAngle;

    // Pulling an arrow out of a connector handle on a lone selected shape. The
    // start is the side it came from and never moves; the end follows the hand
    // and binds exactly as the connector tool's does.
    private FrameSide? _handleConnectorSide;
    private Guid _handleConnectorShapeId;
    private PointD _handleConnectorStartWorld;
    private PointD _handleConnectorStartScreen;
    private PointD _handleConnectorWorld;
    private bool _handleConnectorDragged;

    // The connectors bound to something the gesture is moving that are not
    // themselves selected. They are recomputed rather than transformed, and go
    // into the same command, so one undo puts everything back where it was.
    private ConnectorBoardObject[] _gestureConnectors = [];
    private bool _isInsertOptionsOpen;
    private bool _isSelectOptionsOpen;
    // The label editor is the other text edit on the editor layer: a plain box
    // in the label's own font, with no title bar and nothing to highlight. Only
    // one of the two is ever open, and committing either commits both.
    private FreeTextBoardObject? _labelEditBefore;
    private FreeTextBoardObject? _labelEditCurrent;
    private bool _labelEditIsNew;
    private bool _updatingLabelEditor;

    // A shape carries its own text rather than holding a label, so the same box
    // is opened over the shape itself. Only one of the three edits is ever open.
    private ShapeBoardObject? _shapeEditBefore;
    private ShapeBoardObject? _shapeEditCurrent;
    private TextBoardObject? _textEditBefore;
    private InkStrokeObject[] _textEditLinkedBefore = [];
    private RectD _textEditBounds;
    private bool _updatingTextEditor;
    private bool _updatingLanguageChip;
    private string _textEditLanguageId = TextLanguageIds.Plain;
    private readonly TextClassificationColorizer _textColorizer = new();
    private readonly PromptBulletGenerator _promptBulletGenerator = new();
    private readonly DispatcherTimer _textHighlightTimer;
    private CancellationTokenSource? _textAnalysisCancellation;
    private bool _formattingRequestOpen;
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
        SelectionPropertyBar.ColorChosen += ApplySelectionColor;
        SelectionPropertyBar.ThicknessChosen += ApplySelectionThickness;
        SelectionPropertyBar.FillChosen += ApplySelectionFill;
        SessionBar.ShapeRequested += ChooseShapeTool;
        SessionBar.ConnectorRequested += ChooseConnectorTool;
        SelectionPropertyBar.ConnectorKindChosen += ApplySelectionConnectorKind;
        SelectionPropertyBar.AnchorModeChosen += ApplySelectionAnchorMode;
        SelectionPropertyBar.FontChosen += ApplySelectionFont;
        SelectionPropertyBar.FontSizeChosen += ApplySelectionFontSize;
        SelectionPropertyBar.FontStyleChosen += ApplySelectionFontStyle;
        SelectionPropertyBar.RotationStepped += StepSelectionRotation;
        SelectionPropertyBar.TextColorChosen += ApplySelectionTextColor;
        SelectionPropertyBar.TextEditRequested += BeginSelectedShapeTextEdit;
        SelectionPropertyBar.CommandChosen += RunSelectionCommand;
        UpdateWindowTitle();
        _initialBoardPath = initialBoardPath;
        TextEditorLanguageCombo.ItemsSource = TextLanguageRegistry.All;
        LanguageChipCombo.ItemsSource = TextLanguageRegistry.All;
        TextEditor.TextArea.TextView.LineTransformers.Add(_textColorizer);
        TextEditor.TextArea.TextView.ElementGenerators.Add(_promptBulletGenerator);
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
        _autosaveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = AutosaveInterval,
        };
        _autosaveTimer.Tick += AutosaveTimer_Tick;
        InkSurface.HoverTracker.Hovered += HoverTracker_Hovered;
        SourceInitialized += MainWindow_SourceInitialized;
        Loaded += MainWindow_Loaded;
        _document.Changed += Document_Changed;
        _history.Changed += History_Changed;
        SceneSurface.Configure(_document, _camera);
        SceneSurface.LiveViewImageSourceProvider = GetLiveViewImageSource;
        InkSurface.Cursor = Cursors.Arrow;
        _settings = AppSettingsStore.Load();
        _connectorKind = _settings.Connector.Kind;
        LoadInkFromSettings();
        ApplyLaserSettings();
        ApplyToolbarPlacement();
        ApplyCalligraphyAccess();
        ApplyPointerModes();
        ApplyGrid();
        ApplyInsertOnToolbar();
        ApplyInsertPalette();
        UpdateSelectButtonGlyph();
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
            // A file named on the command line is what the person asked for, and outranks
            // whatever the last session happened to be holding.
            await OpenPathAsync(_initialBoardPath, confirmDiscard: false);
        }
        else
        {
            await RestoreSessionAsync();
        }

        SessionStore.Prune();
        _autosaveTimer.Start();
    }

    /// <summary>
    /// Brings back what the last copy of the application to close was holding: silently when
    /// it closed on purpose, and by asking when it did not.
    /// </summary>
    private async Task RestoreSessionAsync()
    {
        IReadOnlyList<AbandonedSession> abandoned = SessionStore.FindAbandoned();
        if (abandoned.Count == 0)
        {
            return;
        }

        // Newest first, and one window holds one board. A slot left behind by a crash keeps
        // its place and is offered again at the next start; one that exited cleanly will
        // never be the newest again, so it is dropped rather than left to pile up a board
        // copy per start until the thirty days run out.
        AbandonedSession candidate = abandoned[0];
        foreach (AbandonedSession stale in abandoned.Skip(1).Where(item => item.State.ExitedCleanly))
        {
            SessionStore.Forget(stale.SlotId);
        }

        if (candidate.State.ExitedCleanly)
        {
            if (!_settings.RestoreLastSession)
            {
                SessionStore.Forget(candidate.SlotId);
                return;
            }
        }
        else if (!ConfirmRecovery(abandoned))
        {
            // Declined, so it goes. Keeping it would put the same question in front of the
            // same person at every start until the thirty days ran out, and the question
            // says plainly what No means.
            SessionStore.Forget(candidate.SlotId);
            return;
        }

        if (!await AdoptSessionAsync(candidate))
        {
            return;
        }

        // Take a copy into this copy's own slot before letting go of the old one, so a
        // second crash before the first autosave cannot lose what was just recovered.
        await WriteSessionAsync(candidate.State.Modified, exitedCleanly: false);
        SessionStore.Forget(candidate.SlotId);
    }

    private bool ConfirmRecovery(IReadOnlyList<AbandonedSession> abandoned)
    {
        var crashed = abandoned.Count(item => !item.State.ExitedCleanly);
        var message = crashed > 1
            ? $"SQLBI Whiteboard closed unexpectedly with {crashed} boards open. The most " +
              "recent one can be recovered now, and the others are offered the next time " +
              "you start.\n\nRecover it? Choosing No discards it."
            : "SQLBI Whiteboard closed unexpectedly while a board was open.\n\n" +
              "Recover it? Choosing No discards it.";

        return MessageBox.Show(
            this,
            message,
            "SQLBI Whiteboard",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    /// <summary>
    /// Opens what a slot was holding. An unmodified slot kept only a file name, because the
    /// file is the better copy of a board that matched it - and may have been edited
    /// elsewhere since.
    /// </summary>
    private async Task<bool> AdoptSessionAsync(AbandonedSession session)
    {
        SessionState state = session.State;
        var fileStillThere = state.BoardPath is not null && File.Exists(state.BoardPath);
        if (!state.Modified)
        {
            if (!fileStillThere)
            {
                return false;
            }

            await LoadBoardAsync(state.BoardPath!);
            RestoreCamera(state);
            return true;
        }

        if (session.BoardPath is null)
        {
            return false;
        }

        try
        {
            await using var stream = new FileStream(
                session.BoardPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                useAsync: true);
            var loaded = await BoardArchive.LoadAsync(stream);
            ReplaceDocument(loaded);
            _currentBoardPath = fileStillThere ? state.BoardPath : null;
            ResetBoardView();
            MarkSaved();

            // It never matched the file it came from, and both the title marker and the
            // question asked on the way out have to go on saying so.
            MarkDirtyOutsideHistory();
            RestoreCamera(state);
            return true;
        }
        catch (Exception exception)
        {
            ShowError("Could not restore the previous session", exception);
            return false;
        }
    }

    private void RestoreCamera(SessionState state)
    {
        _camera.Restore(new PointD(state.CameraCenterX, state.CameraCenterY), state.CameraZoom);
        CameraChanged();
    }

    // Only ever writes to the session slot. The board the person named is theirs, and
    // nothing here is allowed to write to it without being asked.
    private async void AutosaveTimer_Tick(object? sender, EventArgs e)
    {
        if (_autosaveRunning || !_autosaveDirty || !IsModified)
        {
            return;
        }

        // Not while anything is in contact with the glass. The zip runs on a worker, but
        // the snapshot does not, and a stroke is the one thing that must never stutter.
        if (_penInContact ||
            _stylusAction != PointerAction.None ||
            _mouseAction != PointerAction.None ||
            _touchPoints.Count > 0)
        {
            return;
        }

        _autosaveRunning = true;
        try
        {
            _autosaveDirty = false;
            await WriteSessionAsync(keepBoard: true, exitedCleanly: false);
        }
        finally
        {
            _autosaveRunning = false;
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
        UpdateLabelEditorOverlay();
        PositionInsertPalette();
    }

    private void Document_Changed(object? sender, EventArgs e)
    {
        _autosaveDirty = true;
        var liveViewIds = _document.Objects.OfType<LiveViewBoardObject>()
            .Select(item => item.Id)
            .ToHashSet();
        foreach (var removedId in _liveViewPresenters.Keys
                     .Where(id => !liveViewIds.Contains(id))
                     .ToArray())
        {
            DisposeLiveViewPresenter(removedId);
        }

        _selectedObjectIds.RemoveWhere(id => _document.Objects.All(item => item.Id != id));
        PublishSelection();
        SceneSurface.InvalidateVisual();
        UpdateLiveViewActionOverlay();
        UpdateTextEditorOverlay();
    }

    private void History_Changed(object? sender, EventArgs e)
    {
        SessionBar.SetEditEnabled(_history.CanUndo, _history.CanRedo);
        UpdateWindowTitle();
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
        else if (EffectiveTool == BoardTool.Shape)
        {
            BeginShapeGesture(screen);
            _stylusAction = PointerAction.Shape;
            InkSurface.CaptureStylus();
            e.Handled = true;
        }
        else if (EffectiveTool == BoardTool.Connector)
        {
            BeginConnectorGesture(screen);
            _stylusAction = PointerAction.Connector;
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
        else if (EffectiveTool == BoardTool.Text)
        {
            InsertLabelAt(screen);
            _stylusAction = PointerAction.None;
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
            case PointerAction.Shape:
                UpdateShapeGesture(screen);
                e.Handled = true;
                break;
            case PointerAction.Connector:
                UpdateConnectorGesture(screen);
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
            case PointerAction.Shape:
                CompleteShapeGesture(screen);
                e.Handled = true;
                break;
            case PointerAction.Connector:
                CompleteConnectorGesture(screen);
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
        // The Insert palette can be dragged over the toolbar and is drawn on top
        // of it. This hit test asks the toolbar alone, which would answer for a
        // press that never reached it.
        if (InsertPalette.Visibility == Visibility.Visible &&
            InsertPalette.InputHitTest(e.GetPosition(InsertPalette)) is not null)
        {
            return false;
        }

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
            // Before the click, which is what makes Select the active tool: the
            // hold is only offered by a press that was not already on Select.
            BeginSelectHold(button);
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
                 EffectiveTool != BoardTool.Text &&
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
        else if (e.ChangedButton == MouseButton.Left &&
                 EffectiveTool == BoardTool.Text &&
                 !Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            // The Text tool answers a plain left click whether or not the mouse
            // draws, as the Laser does. Ctrl still borrows Select, so a label
            // can be moved with the mouse without leaving the tool.
            InsertLabelAt(screen);
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
            case BoardTool.Shape:
                BeginShapeGesture(screen);
                _mouseAction = PointerAction.Shape;
                break;
            case BoardTool.Connector:
                BeginConnectorGesture(screen);
                _mouseAction = PointerAction.Connector;
                break;
            case BoardTool.Text:
                InsertLabelAt(screen);
                _mouseAction = PointerAction.None;
                break;
            default:
                BeginMouseInk(screen);
                _mouseAction = PointerAction.Ink;
                break;
        }
    }

    // Picking a tool from the toolbar with the mouse is the one moment the
    // application can be sure the question is worth asking: a pen user reaches
    // for the palette with the pen. The offer is queued rather than shown from
    // here, so the click first does what it came to do - the tool is chosen,
    // and the dialog then explains why it may not behave as expected.
    private void ToolPalette_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.StylusDevice is not null ||
            _mouseModeOffered ||
            !_settings.SuggestMouseMode ||
            IsMouseModeEffective)
        {
            return;
        }

        // Asked once a session however many tools are picked afterwards. The
        // checkbox on the offer is what answers it for every session after
        // this one.
        _mouseModeOffered = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(OfferMouseMode));
    }

    private void OfferMouseMode()
    {
        // The dialog takes the press with it, so a hold counted from that press
        // would switch the area tool behind it.
        CancelSelectHold();
        var offer = new MouseModeOfferWindow { Owner = this };
        offer.ShowDialog();
        var changed = false;
        if (offer.EnableRequested)
        {
            _settings.MouseMode = MouseMode.On;
            ApplyPointerModes();
            changed = true;
        }

        if (offer.DoNotShowAgain)
        {
            _settings.SuggestMouseMode = false;
            changed = true;
        }

        if (changed)
        {
            PersistSettings();
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
        else if (EffectiveTool is BoardTool.Shape or BoardTool.Connector)
        {
            // The crosshair says the next press drags something out rather than
            // taking hold of what is already on the board.
            HidePointerDot();
            InkSurface.Cursor = Cursors.Cross;
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
            case PointerAction.Shape:
                UpdateShapeGesture(screen);
                break;
            case PointerAction.Connector:
                UpdateConnectorGesture(screen);
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
        else if (_mouseAction == PointerAction.Shape)
        {
            ResetShapeGesture();
        }
        else if (_mouseAction == PointerAction.Connector)
        {
            ResetConnectorGesture();
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
            case PointerAction.Shape:
                CompleteShapeGesture(screen);
                break;
            case PointerAction.Connector:
                CompleteConnectorGesture(screen);
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

    private void ApplyPointerModes()
    {
        var fingerInk = IsFingerModeEffective;
        if (!fingerInk)
        {
            CancelFingerTool();
        }

        InkSurface.SetAllowTouchInk(fingerInk);
        ApplyExtraTools();
    }

    // Finger drawing and mouse drawing are separate settings that need the same
    // two toolbar buttons, for the same reason: erasing is the pen's reverse end
    // and panning is touch or Space, and a device with neither has nowhere else
    // to reach them. The Eraser has a third reason of its own - a pen whose back
    // end is not an eraser - so it can be asked for on its own, and Pan cannot.
    private void ApplyExtraTools()
    {
        var extraTools = IsFingerModeEffective || IsMouseModeEffective;
        var eraserTool = extraTools || _settings.ShowEraserButton;
        var dual = IsDualLayout;
        PlaceEraserButton(dual);
        if (EraserToolButton is not null)
        {
            EraserToolButton.Visibility = eraserTool ? Visibility.Visible : Visibility.Collapsed;
        }

        if (PanToolButton is not null)
        {
            PanToolButton.Visibility = extraTools ? Visibility.Visible : Visibility.Collapsed;
        }

        if (ExtraToolsRow is not null)
        {
            // The row is Pan's alone once the Eraser has moved up into the bar,
            // so it goes away with Pan rather than with either button.
            var wanted = dual ? eraserTool : extraTools;
            ExtraToolsRow.Visibility = wanted ? Visibility.Visible : Visibility.Collapsed;
        }

        if ((!eraserTool && _activeTool is BoardTool.Eraser) ||
            (!extraTools && _activeTool is BoardTool.Pan))
        {
            SetActiveTool(_lastDrawingTool);
        }
    }

    // Where the Eraser sits is a question about the layout, not about why it is
    // there. The dual palette is stacked groups already, so a row beneath it
    // reads as one more group; the compact bar is a single line of tools, and a
    // second line holding one button doubles the toolbar's height to say very
    // little.
    private void PlaceEraserButton(bool dual)
    {
        if (EraserToolButton is null || ToolButtonsRow is null || ExtraToolsRow is null)
        {
            return;
        }

        Panel host = dual ? ExtraToolsRow : ToolButtonsRow;
        if (ReferenceEquals(EraserToolButton.Parent, host))
        {
            return;
        }

        if (EraserToolButton.Parent is Panel previous)
        {
            previous.Children.Remove(EraserToolButton);
        }

        if (dual)
        {
            // Ahead of Pan, which is the order the two have always been in.
            host.Children.Insert(0, EraserToolButton);
        }
        else
        {
            host.Children.Add(EraserToolButton);
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
            case PointerAction.Shape:
                ResetShapeGesture();
                break;
            case PointerAction.Connector:
                ResetConnectorGesture();
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
        UpdateLabelEditorOverlay();
        UpdateSelectionPropertyBar();
    }

    private void ClearSelection()
    {
        if (_selectedObjectIds.Count == 0 &&
            SceneSurface.SelectedObjectIds.Count == 0 &&
            SceneSurface.HoveredObjectId is null)
        {
            return;
        }

        _selectedObjectIds.Clear();
        SceneSurface.HoveredObjectId = null;
        PublishSelection();
        SceneSurface.InvalidateVisual();
        UpdateLiveViewActionOverlay();
    }

    /// <summary>
    /// The surface and the property bar, brought up to what the set now holds.
    /// Nothing changes the selection without ending here.
    /// </summary>
    private void PublishSelection()
    {
        // A label being typed has the editor's own border, so the surface is
        // left to draw the board rather than a second outline around it.
        SceneSurface.SelectedObjectIds = _labelEditBefore is null
            ? new HashSet<Guid>(_selectedObjectIds)
            : new HashSet<Guid>();
        UpdateSelectionPropertyBar();
    }

    private void SelectOnly(Guid? objectId)
    {
        _selectedObjectIds.Clear();
        if (objectId is Guid id)
        {
            _selectedObjectIds.Add(id);
        }

        PublishSelection();
    }

    private void SelectMany(IEnumerable<Guid> objectIds, bool extend)
    {
        if (!extend)
        {
            _selectedObjectIds.Clear();
        }

        foreach (var id in objectIds)
        {
            _selectedObjectIds.Add(id);
        }

        PublishSelection();
    }

    /// <summary>
    /// The one selected object, when exactly one is selected. Everything that
    /// only makes sense for a single thing - F2, the language chip, the
    /// LiveView overlay - asks for it and does nothing when there is none.
    /// </summary>
    private T? SingleSelected<T>()
        where T : BoardObject =>
        _selectedObjectIds.Count == 1
            ? _document.Objects.FirstOrDefault(item => item.Id == _selectedObjectIds.First()) as T
            : null;

    private BoardObject[] SelectedObjects() =>
        _document.Objects.Where(item => _selectedObjectIds.Contains(item.Id)).ToArray();

    private RectD? SelectionBounds()
    {
        BoardObject[] selected = SelectedObjects();
        return selected.Length == 0 ? null : UnionBounds(selected);
    }

    private static RectD UnionBounds(IReadOnlyList<BoardObject> items)
    {
        var left = items.Min(item => item.Bounds.Left);
        var top = items.Min(item => item.Bounds.Top);
        var right = items.Max(item => item.Bounds.Right);
        var bottom = items.Max(item => item.Bounds.Bottom);
        return new RectD(left, top, right - left, bottom - top);
    }

    private void FrameContentAt(PointD screenPoint)
    {
        var container = _document.HitTestTopContainer(_camera.ScreenToWorld(screenPoint), _camera.Zoom);
        if (container is not null)
        {
            SelectOnly(container.Id);
            _camera.Frame(container.Bounds);
        }
        else
        {
            SelectOnly(null);
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
            .Where(stroke => stroke.HitTestWithin(worldPoint, radius))
            .ToArray();

        foreach (var stroke in hits)
        {
            _erasedObjects.Add(stroke);
            _document.RemoveObject(stroke.Id);
            if (_selectedObjectIds.Remove(stroke.Id))
            {
                PublishSelection();
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

    /// <summary>
    /// A press with Select. It takes hold of the corner handle, a text
    /// container's width handle, whatever is under the point, or - when nothing
    /// is - the empty canvas, which starts a rubber band or a lasso.
    /// </summary>
    private void BeginContainerGesture(PointD screenPoint)
    {
        var worldPoint = _camera.ScreenToWorld(screenPoint);
        var extend = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        ResetContainerGesture();

        // A lone connector has two endpoint handles where everything else has
        // one corner handle, so they are asked for first, and for the same
        // reason: they sit over whatever the line crosses.
        if (!extend && BeginConnectorEndpointGesture(screenPoint))
        {
            return;
        }

        // The rotation handle stands clear of the object's own top edge, which
        // can be anywhere once the object has been turned, so it is asked
        // before the corner handle and before anything is hit tested.
        if (!extend && BeginRotationGesture(screenPoint))
        {
            return;
        }

        // The connector handles stand outside the shape's own sides, for the
        // same reason and in the same way, so they are asked next.
        if (!extend && BeginConnectorHandleGesture(screenPoint))
        {
            return;
        }

        // The handle belongs to the selection's own rectangle and sits outside
        // everything inside it, so it is asked before anything is hit tested.
        if (!extend &&
            SingleSelected<ConnectorBoardObject>() is null &&
            SelectionBounds() is RectD bounds &&
            IsOverHandle(screenPoint, bounds))
        {
            _gestureIsResize = true;

            // Shift on the handle of a lone text container changes its width in
            // columns and reflows the text, keeping the size; a plain drag
            // scales it like a picture, as it does every container.
            _gestureReflowTarget = SingleSelected<TextBoardObject>();
            _gestureReflow = _gestureReflowTarget is not null &&
                             Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

            // A shape is the one container allowed to change proportion, so its
            // handle follows the pointer in both directions and Shift is what
            // keeps the aspect - the opposite way round from a picture.
            _gestureFreeResize = SingleSelected<ShapeBoardObject>() is not null;
            BeginSelectionGesture(worldPoint);
            return;
        }

        if (!extend && FindTextContainerAtRightEdge(screenPoint) is { } edged)
        {
            SelectOnly(edged.Id);
            _gestureIsResize = true;
            _gestureReflow = true;
            _gestureReflowTarget = edged;
            BeginSelectionGesture(worldPoint);
            SceneSurface.InvalidateVisual();
            UpdateLiveViewActionOverlay();
            return;
        }

        BoardObject? hit = _document.HitTestTopSelectable(worldPoint, _camera.Zoom);
        if (hit is null)
        {
            BeginAreaGesture(screenPoint, worldPoint, extend);
            SceneSurface.InvalidateVisual();
            return;
        }

        if (extend)
        {
            _selectedObjectIds.Add(hit.Id);
            PublishSelection();
        }
        else if (!_selectedObjectIds.Contains(hit.Id))
        {
            // A press on something already selected keeps the set, so dragging
            // any member of it drags all of them.
            SelectOnly(hit.Id);
        }

        BeginSelectionGesture(worldPoint);
        SceneSurface.InvalidateVisual();
        UpdateLiveViewActionOverlay();
    }

    private void BeginSelectionGesture(PointD worldPoint)
    {
        BoardObject[] selected = SelectedObjects();
        if (selected.Length == 0)
        {
            ResetContainerGesture();
            return;
        }

        HashSet<Guid> ids = selected.Select(item => item.Id).ToHashSet();
        InkStrokeObject[] linked = selected
            .Where(item => item is IBoardContainer)
            .SelectMany(item => _document.LinkedStrokes(item.Id))
            .Where(stroke => !ids.Contains(stroke.Id))
            .DistinctBy(stroke => stroke.Id)
            .ToArray();
        // Every connector bound to something that is moving and not selected
        // itself. They are recomputed from their anchors rather than carried by
        // the box, and they go into the same command, so an undo puts the
        // arrows back where the shapes put them.
        _gestureConnectors = selected
            .SelectMany(item => _document.ConnectorsAttachedTo(item.Id))
            .Where(connector => !ids.Contains(connector.Id))
            .DistinctBy(connector => connector.Id)
            .ToArray();
        _gestureBefore = [.. selected, .. linked];
        _gestureAfter = [.. _gestureBefore, .. _gestureConnectors];
        _gestureBounds = UnionBounds(selected);
        _gestureAfterBounds = _gestureBounds;
        _gestureStartWorld = worldPoint;
        SceneSurface.GestureInProgress = true;
        HideSelectionPropertyBar();
    }

    private void UpdateContainerGesture(PointD worldPoint)
    {
        if (_areaActive)
        {
            UpdateAreaGesture(worldPoint);
            return;
        }

        if (_endpointBefore is not null)
        {
            UpdateConnectorEndpointGesture(worldPoint);
            return;
        }

        if (_rotationBefore is not null)
        {
            UpdateRotationGesture(worldPoint);
            return;
        }

        if (_handleConnectorSide is not null)
        {
            UpdateConnectorHandleGesture(worldPoint);
            return;
        }

        if (_gestureBefore.Length == 0)
        {
            return;
        }

        if (_gestureReflow && _gestureReflowTarget is { } reflowed)
        {
            double pixelsPerDip = VisualTreeHelper.GetDpi(SceneSurface).PixelsPerDip;
            double width = Math.Max(
                TextContainerVisual.MinimumWidth * reflowed.VisualScale,
                worldPoint.X - reflowed.Bounds.Left);
            double height = TextContainerVisual.MeasureDesiredHeight(
                reflowed.Text,
                width,
                reflowed.VisualScale,
                pixelsPerDip,
                reflowed.LanguageId);
            var reflowedBounds = new RectD(reflowed.Bounds.Left, reflowed.Bounds.Top, width, height);
            SceneSurface.HandleLabel = TextContainerVisual.ColumnsFor(
                width,
                reflowed.VisualScale,
                reflowed.LanguageId,
                pixelsPerDip) + " columns";
            _gestureAfterBounds = reflowedBounds;
            _gestureAfter = WithFollowingConnectors(_gestureBefore
                .Select(item => item.Id == reflowed.Id
                    ? reflowed with { Bounds = reflowedBounds }
                    : TransformInGesture(item, reflowed.Bounds, reflowedBounds))
                .ToArray());
            _document.ReplaceObjects(_gestureAfter);
            return;
        }

        RectD after = _gestureIsResize
            ? ResizedSelection(worldPoint)
            : _gestureBounds.Translate(worldPoint - _gestureStartWorld);
        if (after == _gestureAfterBounds)
        {
            return;
        }

        _gestureAfterBounds = after;
        _gestureAfter = WithFollowingConnectors(_gestureBefore
            .Select(item => TransformInGesture(item, _gestureBounds, after))
            .ToArray());
        _document.ReplaceObjects(_gestureAfter);
    }

    /// <summary>
    /// The gesture's own objects, and behind them every connector bound to one
    /// of them brought to where its anchors now are. A connector is recomputed
    /// rather than transformed: the anchor says where on the object the line
    /// ends, and that is true whatever the gesture did to the object.
    /// </summary>
    private BoardObject[] WithFollowingConnectors(BoardObject[] transformed)
    {
        if (_gestureConnectors.Length == 0)
        {
            return transformed;
        }

        Dictionary<Guid, BoardObject> movedById = transformed.ToDictionary(item => item.Id);
        return
        [
            .. transformed,
            .. _gestureConnectors.Select(connector => (BoardObject)FollowAndReroute(connector, movedById)),
        ];
    }

    /// <summary>
    /// One connector brought to where what it points at now is: each bound end
    /// recomputed from its anchor, and then, for one that routes itself, the
    /// anchors chosen again for the sides that now face each other. The
    /// gesture's own objects answer for themselves, since the board is a move
    /// behind them while the hand is still down; anything else is where the
    /// board has it.
    /// </summary>
    private ConnectorBoardObject FollowAndReroute(
        ConnectorBoardObject connector,
        IReadOnlyDictionary<Guid, BoardObject> moved)
    {
        BoardObject? startObject = InGestureOrBoard(connector.StartAnchor, moved);
        BoardObject? endObject = InGestureOrBoard(connector.EndAnchor, moved);
        ConnectorBoardObject followed = connector;
        if (startObject is not null)
        {
            followed = followed.Follow(startObject);
        }

        if (endObject is not null)
        {
            followed = followed.Follow(endObject);
        }

        return followed.Reroute(startObject?.AnchorFrame, endObject?.AnchorFrame);
    }

    private BoardObject? InGestureOrBoard(
        ConnectorAnchor? anchor,
        IReadOnlyDictionary<Guid, BoardObject> moved) => anchor is { } bound
        ? moved.TryGetValue(bound.ObjectId, out BoardObject? inGesture)
            ? inGesture
            : _document.Objects.FirstOrDefault(item => item.Id == bound.ObjectId)
        : null;

    /// <summary>
    /// The selection under the corner handle. A lone shape takes the width and
    /// the height the pointer is at, and keeps its aspect with Shift; everything
    /// else scales with the aspect preserved, as a picture always has.
    /// </summary>
    private RectD ResizedSelection(PointD worldPoint) =>
        _gestureFreeResize && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
            ? StretchedSelection(worldPoint)
            : ScaledSelection(worldPoint);

    private RectD StretchedSelection(PointD worldPoint)
    {
        RectD bounds = _gestureBounds;
        var minimum = 32 / _camera.Zoom;
        return bounds.WithSize(
            Math.Max(minimum, worldPoint.X - bounds.Left),
            Math.Max(minimum, worldPoint.Y - bounds.Top));
    }

    /// <summary>
    /// The selection scaled about its top-left corner, aspect preserved, by how
    /// far the handle has been dragged along the diagonal. It is the rule a
    /// single container has always followed, asked of the whole set.
    /// </summary>
    private RectD ScaledSelection(PointD worldPoint)
    {
        RectD bounds = _gestureBounds;
        var width = Math.Max(0.000001, bounds.Width);
        var height = Math.Max(0.000001, bounds.Height);
        var minimumWorldSize = 32 / _camera.Zoom;
        var requestedScale =
            (((worldPoint.X - bounds.Left) * width) + ((worldPoint.Y - bounds.Top) * height)) /
            ((width * width) + (height * height));
        var minimumScale = Math.Max(minimumWorldSize / width, minimumWorldSize / height);
        var scale = Math.Max(minimumScale, requestedScale);
        return bounds.WithSize(width * scale, height * scale);
    }

    /// <summary>
    /// One member of the gesture, carried from where the selection was to where
    /// it now is. A stroke follows the points themselves; everything else
    /// follows its box.
    /// </summary>
    private static BoardObject TransformInGesture(BoardObject item, RectD before, RectD after) =>
        item is InkStrokeObject stroke
            ? stroke.TransformWithContainer(before, after)
            : item.WithBounds(MapRectangle(item.Bounds, before, after));

    private static RectD MapRectangle(RectD bounds, RectD before, RectD after)
    {
        var scaleX = after.Width / Math.Max(0.000001, before.Width);
        var scaleY = after.Height / Math.Max(0.000001, before.Height);
        return new RectD(
            after.Left + ((bounds.Left - before.Left) * scaleX),
            after.Top + ((bounds.Top - before.Top) * scaleY),
            bounds.Width * scaleX,
            bounds.Height * scaleY);
    }

    // Screen pixels around the rotation handle that take hold of it, and the
    // step Shift holds the angle to while it is being dragged.
    private const double RotationHandleReach = 16;
    private const double RotationSnapDegrees = 15;

    // Windows offers no rotation cursor, so the hand says what it says
    // everywhere else: this is something to take hold of.
    private static readonly Cursor RotationCursor = Cursors.Hand;

    /// <summary>
    /// The object the rotation handle belongs to, when there is one: a lone
    /// shape or a lone label. Everything else is either without an angle or
    /// selected with something else, and offers no handle.
    /// </summary>
    private BoardObject? RotationTarget() =>
        SingleSelected<ShapeBoardObject>() ?? (BoardObject?)SingleSelected<FreeTextBoardObject>();

    /// <summary>
    /// Whether the pointer has hold of the rotation handle. A shape's top
    /// connector handle stands on the same line out of the same edge, closer
    /// in, and the two reaches meet: whichever of them the pointer is nearer
    /// takes it, so neither can bury the other.
    /// </summary>
    private bool IsOverRotationHandle(PointD screen)
    {
        if (RotationTarget() is not { } target)
        {
            return false;
        }

        var toRotation = Distance(
            ToPoint(_camera.WorldToScreen(target.AnchorFrame.RotationHandle(_camera.Zoom))),
            ToPoint(screen));
        return toRotation <= RotationHandleReach &&
               (ConnectorHandleAt(screen) is not { } near ||
                Distance(ToPoint(_camera.WorldToScreen(near.Handle.Point)), ToPoint(screen)) > toRotation);
    }

    /// <summary>
    /// A press on the rotation handle. The angle the hand is at when it takes
    /// hold is remembered against the angle the object already has, so the
    /// first move turns the object by how far the hand has travelled rather
    /// than swinging it round to meet the pointer.
    /// </summary>
    private bool BeginRotationGesture(PointD screenPoint)
    {
        if (RotationTarget() is not { } target || !IsOverRotationHandle(screenPoint))
        {
            return false;
        }

        // Taken once, at the press: every move turns these from where they
        // started rather than from where the last move left them, which is what
        // keeps a turn a turn rather than a turn on a turn.
        _rotationBefore = target;
        _rotationStrokes = _document.LinkedStrokes(target.Id).ToArray();
        _rotationConnectors = _document.ConnectorsAttachedTo(target.Id).ToArray();
        _rotationStartAngle = AngleOf(target);
        _rotationAngle = _rotationStartAngle;
        _rotationGrabAngle =
            PointerAngle(target.AnchorFrame.Layout.Center, _camera.ScreenToWorld(screenPoint)) -
            _rotationStartAngle;
        SceneSurface.GestureInProgress = true;
        HideSelectionPropertyBar();
        return true;
    }

    private void UpdateRotationGesture(PointD worldPoint)
    {
        if (_rotationBefore is not { } before)
        {
            return;
        }

        var angle = PointerAngle(before.AnchorFrame.Layout.Center, worldPoint) - _rotationGrabAngle;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            angle = Math.Round(angle / RotationSnapDegrees) * RotationSnapDegrees;
        }

        angle = RotatedRectangle.NormalizeAngle(angle);
        if (angle == _rotationAngle)
        {
            return;
        }

        _rotationAngle = angle;
        SceneSurface.HandleLabel = ((int)Math.Round(angle)) + "°";
        _document.ReplaceObjects(RotatedTo(before, angle).After);
        SceneSurface.InvalidateVisual();
    }

    private void CompleteRotationGesture()
    {
        if (_rotationBefore is not { } before || _rotationAngle == _rotationStartAngle)
        {
            return;
        }

        (BoardObject[] from, BoardObject[] to) = RotatedTo(before, _rotationAngle);
        _document.ReplaceObjects(to);
        _history.RecordExecuted(new ReplaceObjectsCommand(from, to));
        SceneSurface.InvalidateVisual();
    }

    /// <summary>
    /// The object at that angle, and with it everything tied to it. The gesture
    /// asks for it on every move to show the turn, and once more on release for
    /// the one command that records it.
    /// </summary>
    private (BoardObject[] Before, BoardObject[] After) RotatedTo(BoardObject item, double angleDegrees)
    {
        BoardObject turned = item switch
        {
            FreeTextBoardObject label => Remeasure(label.WithAngle(angleDegrees)),
            ShapeBoardObject shape => shape.WithAngle(angleDegrees),
            _ => item,
        };
        var before = new List<BoardObject> { item };
        var after = new List<BoardObject> { turned };
        AddRotationFollowers(
            [(item, angleDegrees - AngleOf(item))],
            new Dictionary<Guid, BoardObject> { [turned.Id] = turned },
            _rotationStrokes,
            _rotationConnectors,
            before,
            after);
        return (before.ToArray(), after.ToArray());
    }

    /// <summary>
    /// Everything carried round by a turn: the ink linked to each object that
    /// turned, about that object's own centre, and the connectors bound to one
    /// of them, recomputed from their anchors. The handle and the property
    /// bar's quarter turns share it, so ink and arrows follow a shape whichever
    /// way it was asked to turn, and everything lands in one command.
    /// </summary>
    private void AddRotationFollowers(
        IReadOnlyList<(BoardObject Item, double Degrees)> turns,
        IReadOnlyDictionary<Guid, BoardObject> turnedById,
        IReadOnlyList<InkStrokeObject> linked,
        IReadOnlyList<ConnectorBoardObject> attached,
        List<BoardObject> before,
        List<BoardObject> after)
    {
        Dictionary<Guid, (double Degrees, PointD Center)> turnById = turns.ToDictionary(
            turn => turn.Item.Id,
            turn => (turn.Degrees, turn.Item.AnchorFrame.Layout.Center));
        foreach (InkStrokeObject stroke in linked)
        {
            if (stroke.ContainerId is not { } containerId ||
                turnedById.ContainsKey(stroke.Id) ||
                !turnById.TryGetValue(containerId, out (double Degrees, PointD Center) turn))
            {
                continue;
            }

            before.Add(stroke);
            after.Add(stroke.Rotate(turn.Center, turn.Degrees));
        }

        foreach (ConnectorBoardObject connector in attached)
        {
            if (turnedById.ContainsKey(connector.Id))
            {
                continue;
            }

            ConnectorBoardObject followed = FollowAndReroute(connector, turnedById);
            if (followed != connector)
            {
                before.Add(connector);
                after.Add(followed);
            }
        }
    }

    private static double AngleOf(BoardObject item) => item switch
    {
        FreeTextBoardObject label => label.AngleDegrees,
        ShapeBoardObject shape => shape.AngleDegrees,
        _ => 0,
    };

    /// <summary>
    /// Where the hand is, as an angle about the object's centre, measured the
    /// way the board turns everything else: clockwise from the right.
    /// </summary>
    private static double PointerAngle(PointD center, PointD worldPoint)
    {
        PointD offset = worldPoint - center;
        return Math.Atan2(offset.Y, offset.X) * 180 / Math.PI;
    }

    // Screen pixels around a connector handle that take hold of it. A little
    // less than the rotation handle's reach, because there are four of them and
    // the corner handle is not far away.
    private const double ConnectorHandleReach = 12;

    /// <summary>
    /// The connector handle the pointer is nearest, when a lone shape is
    /// selected and it is on one of them. Only a shape offers them for now: a
    /// picture or a label would take the same four arrows and the same gesture,
    /// which is the frame and the two lines that ask for it, but nothing in a
    /// diagram is drawn out of one yet.
    /// </summary>
    private (ShapeBoardObject Shape, ConnectorHandle Handle)? ConnectorHandleAt(PointD screen)
    {
        // Nothing while the shape's own text is being typed: the editor's box
        // stands over it, the arrows are not drawn, and a press there commits
        // the text first, which is when the handles come back.
        if (_shapeEditBefore is not null || SingleSelected<ShapeBoardObject>() is not { } shape)
        {
            return null;
        }

        (ShapeBoardObject Shape, ConnectorHandle Handle)? nearest = null;
        var bestDistance = ConnectorHandleReach;
        foreach (ConnectorHandle handle in shape.AnchorFrame.ConnectorHandles(_camera.Zoom))
        {
            var distance = Distance(ToPoint(_camera.WorldToScreen(handle.Point)), ToPoint(screen));
            if (distance <= bestDistance)
            {
                bestDistance = distance;
                nearest = (shape, handle);
            }
        }

        return nearest;
    }

    /// <summary>
    /// A press on one of a shape's connector handles. It draws a new arrow out
    /// of that side rather than moving anything, which is what removes the trip
    /// to the Insert row for the common case: two shapes and a line between
    /// them.
    /// </summary>
    private bool BeginConnectorHandleGesture(PointD screenPoint)
    {
        if (ConnectorHandleAt(screenPoint) is not { } found)
        {
            return false;
        }

        _handleConnectorSide = found.Handle.Side;
        _handleConnectorShapeId = found.Shape.Id;
        _handleConnectorStartWorld = ConnectorGeometry.PointOn(
            found.Shape.AnchorFrame,
            ConnectorGeometry.SideAnchor(found.Shape.Id, found.Handle.Side));
        _handleConnectorStartScreen = screenPoint;
        _handleConnectorWorld = _camera.ScreenToWorld(screenPoint);
        _handleConnectorDragged = false;
        SceneSurface.GestureInProgress = true;
        HideSelectionPropertyBar();
        return true;
    }

    private void UpdateConnectorHandleGesture(PointD worldPoint)
    {
        if (_handleConnectorSide is not { } side)
        {
            return;
        }

        _handleConnectorWorld = worldPoint;
        if (!_handleConnectorDragged &&
            Distance(ToPoint(_camera.WorldToScreen(worldPoint)), ToPoint(_handleConnectorStartScreen)) >
            AreaDragThreshold)
        {
            _handleConnectorDragged = true;
        }

        if (_handleConnectorDragged)
        {
            ShowConnectorDrag(
                _handleConnectorStartWorld,
                ConnectorGeometry.SideAnchor(_handleConnectorShapeId, side),
                worldPoint,
                ConnectorKind.Arrow);
        }

        SceneSurface.InvalidateVisual();
    }

    /// <summary>
    /// The arrow the drag drew: always an Arrow, in the colour and thickness the
    /// connector tool would use, bound where it started and bound at the far end
    /// to whatever it was let go over. A press that never moved makes nothing,
    /// so a tap on a handle is how somebody finds out what it does. The arrow
    /// becomes the selection and the tool stays Select, so the next thing the
    /// hand does is to the arrow rather than to another one.
    /// </summary>
    private void CompleteConnectorHandleGesture()
    {
        if (_handleConnectorSide is not { } side || !_handleConnectorDragged)
        {
            return;
        }

        BoardObject? target = BindingTargetAt(_handleConnectorWorld);
        (ConnectorAnchor start, ConnectorAnchor? end) = ConnectorGeometry.ConnectorHandleAnchors(
            _handleConnectorShapeId,
            side,
            target is null
                ? null
                : (target.Id, target.AnchorFrame, (target as ShapeBoardObject)?.Outline()),
            _handleConnectorWorld,
            Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
        ConnectorBoardObject connector = NewConnector(
            _handleConnectorStartWorld,
            _handleConnectorWorld,
            start,
            end,
            ConnectorKind.Arrow);
        _history.Execute(new AddObjectCommand(connector), _document);
        SelectOnly(connector.Id);
        UpdateLiveViewActionOverlay();
    }

    private void CompleteContainerGesture()
    {
        if (_areaActive)
        {
            CompleteAreaGesture();
            return;
        }

        if (_endpointBefore is not null)
        {
            CompleteConnectorEndpointGesture();
            ResetContainerGesture();
            return;
        }

        if (_rotationBefore is not null)
        {
            CompleteRotationGesture();
            ResetContainerGesture();
            UpdateSelectionPropertyBar();
            return;
        }

        if (_handleConnectorSide is not null)
        {
            CompleteConnectorHandleGesture();
            ResetContainerGesture();
            UpdateSelectionPropertyBar();
            return;
        }

        if (_gestureBefore.Length > 0 && _gestureAfterBounds != _gestureBounds)
        {
            BoardObject[] after = _gestureAfter;

            // A connector dragged by its body was taken away from what it
            // joined: it keeps the shape the hand gave it rather than springing
            // back the next time either object moves.
            if (!_gestureIsResize && after is [ConnectorBoardObject moved])
            {
                after = [moved.Detach()];
                _document.ReplaceObjects(after);
            }

            _history.RecordExecuted(
                new ReplaceObjectsCommand([.. _gestureBefore, .. _gestureConnectors], after));
        }

        ResetContainerGesture();
        UpdateSelectionPropertyBar();
    }

    private void ResetContainerGesture()
    {
        _gestureBefore = [];
        _gestureAfter = [];
        _gestureConnectors = [];
        _endpointBefore = null;
        _rotationBefore = null;
        _rotationStrokes = [];
        _rotationConnectors = [];
        _handleConnectorSide = null;
        _handleConnectorDragged = false;
        SceneSurface.GestureInProgress = false;
        if (SceneSurface.BindingDots is not null || SceneSurface.PendingConnector is not null)
        {
            SceneSurface.PendingConnector = null;
            ClearBindingFeedback();
            SceneSurface.InvalidateVisual();
        }

        _gestureBounds = default;
        _gestureAfterBounds = default;
        _gestureIsResize = false;
        _gestureFreeResize = false;
        _gestureReflow = false;
        _gestureReflowTarget = null;
        _areaActive = false;
        _areaExtends = false;
        _areaDragged = false;
        _areaPoints.Clear();
        if (SceneSurface.PendingArea is not null)
        {
            SceneSurface.PendingArea = null;
            SceneSurface.InvalidateVisual();
        }

        if (SceneSurface.HandleLabel is not null)
        {
            SceneSurface.HandleLabel = null;
            SceneSurface.InvalidateVisual();
        }
    }

    // How far the pointer has to travel before a press on empty canvas is an
    // area rather than a tap. Below it the press clears the selection, which is
    // what a tap on nothing has always done.
    private const double AreaDragThreshold = 3;

    // Screen pixels between the points a lasso keeps.
    private const double LassoPointSpacing = 3;

    private void BeginAreaGesture(PointD screenPoint, PointD worldPoint, bool extend)
    {
        _areaActive = true;
        _areaExtends = extend;
        _areaDragged = false;
        _areaStartScreen = screenPoint;
        _areaPoints.Clear();
        _areaPoints.Add(worldPoint);
        SceneSurface.GestureInProgress = true;
        HideSelectionPropertyBar();
    }

    private void UpdateAreaGesture(PointD worldPoint)
    {
        PointD screen = _camera.WorldToScreen(worldPoint);
        if (!_areaDragged &&
            Distance(ToPoint(screen), ToPoint(_areaStartScreen)) > AreaDragThreshold)
        {
            _areaDragged = true;
        }

        if (!IsLassoArea)
        {
            // A rubber band is its two corners, so the second is replaced rather
            // than appended and the band follows the pointer back.
            if (_areaPoints.Count < 2)
            {
                _areaPoints.Add(worldPoint);
            }
            else
            {
                _areaPoints[1] = worldPoint;
            }
        }
        else if (Distance(ToPoint(screen), ToPoint(_camera.WorldToScreen(_areaPoints[^1]))) >=
                 LassoPointSpacing)
        {
            // A point every few screen pixels rather than every packet. The
            // outline is the same to look at, and every object the lasso is
            // tested against walks it once.
            _areaPoints.Add(worldPoint);
        }

        SceneSurface.PendingArea = _areaDragged ? AreaOutline() : null;
        SceneSurface.InvalidateVisual();
    }

    private void CompleteAreaGesture()
    {
        var extend = _areaExtends;
        var dragged = _areaDragged;
        SelectionArea area = CurrentArea();
        ResetContainerGesture();

        if (!dragged)
        {
            if (!extend)
            {
                ClearSelection();
            }

            return;
        }

        IEnumerable<Guid> taken = _document
            .ObjectsInArea(area, _settings.AreaSelection)
            .Select(item => item.Id);
        SelectMany(_document.GrowSelection(taken, _settings.ExtendSelection), extend);
        SceneSurface.InvalidateVisual();
        UpdateLiveViewActionOverlay();
    }

    private bool IsLassoArea => _settings.AreaSelectionTool == AreaSelectionTool.Lasso;

    /// <summary>
    /// Ctrl+A takes everything an area could take; Ctrl+Shift+A takes the ink
    /// strokes alone. It goes through the same set the area gestures fill, so
    /// the property bar, Delete, Copy, the z-order commands, and the group
    /// gesture all follow without being told about it. Asking for the set with
    /// another tool in hand is asking for those gestures too, so the tool comes
    /// back to Select first.
    /// </summary>
    private void SelectAll(bool strokesOnly)
    {
        if (_activeTool != BoardTool.Select)
        {
            ChooseTool(BoardTool.Select);
        }

        SelectMany(_document.AllSelectable(strokesOnly).Select(item => item.Id), extend: false);
        SceneSurface.InvalidateVisual();
        UpdateLiveViewActionOverlay();
    }

    private RectD CurrentAreaRectangle()
    {
        PointD start = _areaPoints[0];
        PointD end = _areaPoints[^1];
        return new RectD(
            Math.Min(start.X, end.X),
            Math.Min(start.Y, end.Y),
            Math.Abs(end.X - start.X),
            Math.Abs(end.Y - start.Y));
    }

    private SelectionArea CurrentArea() =>
        IsLassoArea
            ? SelectionArea.Lasso(_areaPoints)
            : SelectionArea.Rectangle(CurrentAreaRectangle());

    private IReadOnlyList<PointD> AreaOutline()
    {
        if (IsLassoArea)
        {
            return [.. _areaPoints, _areaPoints[0]];
        }

        IReadOnlyList<PointD> corners = Polygon.Corners(CurrentAreaRectangle());
        return [.. corners, corners[0]];
    }

    // What a tap with the Shape tool inserts, in screen pixels: a shape big
    // enough to read and to take hold of, at whatever zoom the board is at.
    private const double TappedShapeWidth = 160;
    private const double TappedShapeHeight = 120;

    /// <summary>
    /// A shape tool picked from the Insert row or the toolbar flyout. The kind
    /// is remembered, so the tool that stays after a drag draws the same thing
    /// again.
    /// </summary>
    private void ChooseShapeTool(ShapeKind kind)
    {
        _shapeKind = kind;
        SetInsertOptionsOpen(false);
        ChooseTool(BoardTool.Shape);
    }

    private void BeginShapeGesture(PointD screen)
    {
        _shapeActive = true;
        _shapeDragged = false;
        _shapeStartScreen = screen;
        _shapeStartWorld = _camera.ScreenToWorld(screen);
        HideSelectionPropertyBar();
    }

    private void UpdateShapeGesture(PointD screen)
    {
        if (!_shapeActive)
        {
            return;
        }

        if (!_shapeDragged &&
            Distance(ToPoint(screen), ToPoint(_shapeStartScreen)) > AreaDragThreshold)
        {
            _shapeDragged = true;
        }

        SceneSurface.PendingShape = _shapeDragged ? NewShape(DraggedShapeBounds(screen)) : null;
        SceneSurface.InvalidateVisual();
    }

    private void CompleteShapeGesture(PointD screen)
    {
        if (!_shapeActive)
        {
            return;
        }

        RectD bounds = _shapeDragged ? DraggedShapeBounds(screen) : TappedShapeBounds();
        ResetShapeGesture();
        ShapeBoardObject shape = NewShape(bounds);
        _history.Execute(new AddObjectCommand(shape), _document);

        // The new shape is the selection, so the property bar is there to
        // recolor or fill it without anything else being picked up first.
        SelectOnly(shape.Id);
        SceneSurface.InvalidateVisual();
        UpdateLiveViewActionOverlay();
        ReturnToSelectAfterInsert();
    }

    private void ResetShapeGesture()
    {
        _shapeActive = false;
        _shapeDragged = false;
        if (SceneSurface.PendingShape is not null)
        {
            SceneSurface.PendingShape = null;
            SceneSurface.InvalidateVisual();
        }
    }

    /// <summary>
    /// The box between the press and the pointer. Shift constrains it to a
    /// square, measured on the longer side so the shape follows the hand rather
    /// than shrinking under it.
    /// </summary>
    private RectD DraggedShapeBounds(PointD screen)
    {
        PointD world = _camera.ScreenToWorld(screen);
        var deltaX = world.X - _shapeStartWorld.X;
        var deltaY = world.Y - _shapeStartWorld.Y;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            var side = Math.Max(Math.Abs(deltaX), Math.Abs(deltaY));
            deltaX = deltaX < 0 ? -side : side;
            deltaY = deltaY < 0 ? -side : side;
        }

        return new RectD(
            Math.Min(_shapeStartWorld.X, _shapeStartWorld.X + deltaX),
            Math.Min(_shapeStartWorld.Y, _shapeStartWorld.Y + deltaY),
            Math.Abs(deltaX),
            Math.Abs(deltaY));
    }

    private RectD TappedShapeBounds()
    {
        var width = TappedShapeWidth / _camera.Zoom;
        var height = TappedShapeHeight / _camera.Zoom;
        return new RectD(
            _shapeStartWorld.X - (width / 2),
            _shapeStartWorld.Y - (height / 2),
            width,
            height);
    }

    /// <summary>
    /// A new shape, saying nothing yet but ready to be typed in: its text takes
    /// the same defaults the next label would, since a shape's words and a
    /// label's are written from one set of choices.
    /// </summary>
    private ShapeBoardObject NewShape(RectD bounds) => ShapeBoardObject.Create(
        Guid.NewGuid(),
        _document.NextZIndex,
        bounds,
        _shapeKind,
        _settings.Shape.OutlineArgb,
        _settings.Shape.FillArgb,
        _settings.Shape.Thickness) with
    {
        FontFamily = _settings.Label.FontFamily,
        FontSize = _settings.Label.FontSize,
        TextArgb = _settings.Label.Argb,
        Bold = _settings.Label.Bold,
        Italic = _settings.Label.Italic,
        Underline = _settings.Label.Underline,
    };

    /// <summary>
    /// A connector tool picked from the Insert row or the toolbar flyout. The
    /// kind is remembered here and in the settings, so the tool that stays after
    /// a drag draws the same thing again and so does the next session.
    /// </summary>
    private void ChooseConnectorTool(ConnectorKind kind)
    {
        _connectorKind = kind;
        _settings.Connector.Kind = kind;
        PersistSettings();
        SetInsertOptionsOpen(false);
        ChooseTool(BoardTool.Connector);
    }

    private void BeginConnectorGesture(PointD screen)
    {
        _connectorActive = true;
        _connectorDragged = false;
        _connectorStartScreen = screen;
        _connectorStartWorld = _camera.ScreenToWorld(screen);
        HideSelectionPropertyBar();
    }

    private void UpdateConnectorGesture(PointD screen)
    {
        if (!_connectorActive)
        {
            return;
        }

        if (!_connectorDragged &&
            Distance(ToPoint(screen), ToPoint(_connectorStartScreen)) > AreaDragThreshold)
        {
            _connectorDragged = true;
        }

        PointD world = _camera.ScreenToWorld(screen);
        if (_connectorDragged)
        {
            ShowConnectorDrag(_connectorStartWorld, BindingAt(_connectorStartWorld), world, _connectorKind);
        }
        else
        {
            SceneSurface.PendingConnector = null;
            ClearBindingFeedback();
        }

        SceneSurface.InvalidateVisual();
    }

    /// <summary>
    /// The connector the drag drew, bound at whichever ends were let go near
    /// something. A press that never moved makes nothing: a connector with no
    /// length says nothing about what it joins, and a tap is how somebody finds
    /// out what the tool does.
    /// </summary>
    private void CompleteConnectorGesture(PointD screen)
    {
        if (!_connectorActive)
        {
            return;
        }

        var dragged = _connectorDragged;
        PointD world = _camera.ScreenToWorld(screen);
        ResetConnectorGesture();
        if (!dragged)
        {
            return;
        }

        ConnectorBoardObject connector = NewConnector(
            _connectorStartWorld,
            world,
            BindingAt(_connectorStartWorld),
            BindingAt(world));
        _history.Execute(new AddObjectCommand(connector), _document);

        // The new connector is the selection, so the property bar is there to
        // recolor it or change its kind without anything else being picked up.
        SelectOnly(connector.Id);
        SceneSurface.InvalidateVisual();
        UpdateLiveViewActionOverlay();
        ReturnToSelectAfterInsert();
    }

    private void ResetConnectorGesture()
    {
        _connectorActive = false;
        _connectorDragged = false;
        if (SceneSurface.PendingConnector is not null || SceneSurface.BindingDots is not null)
        {
            SceneSurface.PendingConnector = null;
            ClearBindingFeedback();
            SceneSurface.InvalidateVisual();
        }
    }

    /// <summary>
    /// What a connector drag in progress shows, whether the tool started it or
    /// a shape's handle did: the line as the release would record it, with the
    /// anchors each end has found, so each end is drawn on its binding point
    /// rather than under the pointer and a curve already leaves the side it
    /// will leave. The dots belong to the end being dragged, which is the one
    /// the hand is asking about.
    /// </summary>
    private void ShowConnectorDrag(
        PointD start,
        ConnectorAnchor? startAnchor,
        PointD world,
        ConnectorKind kind)
    {
        SceneSurface.PendingConnector = NewConnector(start, world, startAnchor, BindingAt(world), kind);
        ShowBindingFeedback(world);
    }

    private ConnectorBoardObject NewConnector(
        PointD start,
        PointD end,
        ConnectorAnchor? startAnchor,
        ConnectorAnchor? endAnchor,
        ConnectorKind? kind = null) => _document.Reroute(ConnectorBoardObject.Create(
        Guid.NewGuid(),
        _document.NextZIndex,
        kind ?? _connectorKind,
        startAnchor is { } bound ? AnchorPoint(bound, start) : start,
        endAnchor is { } endBound ? AnchorPoint(endBound, end) : end,
        _settings.Connector.Argb,
        _settings.Connector.Thickness,
        startAnchor,
        endAnchor,
        _settings.Connector.AutoRoute));

    private PointD AnchorPoint(ConnectorAnchor anchor, PointD fallback) =>
        _document.Objects.FirstOrDefault(item => item.Id == anchor.ObjectId) is { } target
            ? ConnectorGeometry.PointOn(target.AnchorFrame, anchor)
            : fallback;

    /// <summary>
    /// What an endpoint dropped here would bind to: the topmost eligible object
    /// whose box, out by the binding reach, the pointer is inside. A shape's
    /// interior counts, even though a tap there is not a hit on the shape, since
    /// an arrow let go in the middle of a box plainly means that box. A frame, a
    /// stroke, and another connector are not things an arrow points at.
    /// </summary>
    private BoardObject? BindingTargetAt(PointD worldPoint)
    {
        var reach = ConnectorBoardObject.BindingReach / _camera.Zoom;
        return _document.Objects
            .Where(item => ConnectorBoardObject.CanBind(item) &&
                           ConnectorGeometry.IsWithinBindingReach(item.AnchorFrame, worldPoint, reach))
            .OrderByDescending(item => item.ZIndex)
            .FirstOrDefault();
    }

    /// <summary>
    /// The target under the pointer and the point on it this end would take.
    /// One answer serves the preview, the dots, the tint, and the release, so
    /// what is shown while the end is dragged is what is recorded when it is
    /// let go. Ctrl asks for the nearest point anywhere on the border instead.
    /// </summary>
    private (BoardObject Target, ConnectorGeometry.BindingCandidate Candidate)? BindingCandidateAt(
        PointD worldPoint)
    {
        if (BindingTargetAt(worldPoint) is not { } target)
        {
            return null;
        }

        return (target, ConnectorGeometry.BindingCandidateFor(
            target.Id,
            target.AnchorFrame,
            (target as ShapeBoardObject)?.Outline(),
            worldPoint,
            Keyboard.Modifiers.HasFlag(ModifierKeys.Control)));
    }

    /// <summary>
    /// The anchor an endpoint let go here takes, or nothing at all when there is
    /// nothing eligible under the pointer and the end stays where the hand put
    /// it.
    /// </summary>
    private ConnectorAnchor? BindingAt(PointD worldPoint) =>
        BindingCandidateAt(worldPoint)?.Candidate.Anchor;

    /// <summary>
    /// What the surface draws while an end is being dragged: the eight points of
    /// the target under the pointer, which of them would be taken, and the
    /// target itself. They are shown rather than described because where an
    /// arrow will land is the whole question while it is in the air.
    /// </summary>
    private void ShowBindingFeedback(PointD worldPoint)
    {
        if (BindingCandidateAt(worldPoint) is not { } found)
        {
            ClearBindingFeedback();
            return;
        }

        SceneSurface.BindingDots = ConnectorGeometry.BindingPoints(found.Target.AnchorFrame);
        SceneSurface.BindingDotIndex = found.Candidate.DotIndex == ConnectorGeometry.NoDot
            ? null
            : found.Candidate.DotIndex;
        SceneSurface.BindingTargetId = found.Target.Id;
    }

    private void ClearBindingFeedback()
    {
        SceneSurface.BindingDots = null;
        SceneSurface.BindingDotIndex = null;
        SceneSurface.BindingTargetId = null;
    }

    /// <summary>
    /// A press on one end of the selected connector, which is what that
    /// connector offers instead of the corner handle.
    /// </summary>
    private bool BeginConnectorEndpointGesture(PointD screenPoint)
    {
        if (SingleSelected<ConnectorBoardObject>() is not { } connector)
        {
            return false;
        }

        var toStart = Distance(ToPoint(_camera.WorldToScreen(connector.Start)), ToPoint(screenPoint));
        var toEnd = Distance(ToPoint(_camera.WorldToScreen(connector.End)), ToPoint(screenPoint));
        if (Math.Min(toStart, toEnd) > ConnectorBoardObject.BindingReach)
        {
            return false;
        }

        _endpointBefore = connector;
        _endpointIsStart = toStart <= toEnd;
        _endpointWorld = _camera.ScreenToWorld(screenPoint);
        SceneSurface.GestureInProgress = true;
        HideSelectionPropertyBar();
        return true;
    }

    private void UpdateConnectorEndpointGesture(PointD worldPoint)
    {
        if (_endpointBefore is not { } before)
        {
            return;
        }

        _endpointWorld = worldPoint;

        // The end snaps to what it is over while it is being dragged, so the
        // handle sits where the release will leave it.
        _document.ReplaceObject(MovedEndpoint(before, worldPoint, BindingAt(worldPoint)));
        ShowBindingFeedback(worldPoint);
        SceneSurface.InvalidateVisual();
    }

    private void CompleteConnectorEndpointGesture()
    {
        if (_endpointBefore is not { } before)
        {
            return;
        }

        ConnectorBoardObject after = _document.Reroute(
            MovedEndpoint(before, _endpointWorld, BindingAt(_endpointWorld)));
        _endpointBefore = null;
        ClearBindingFeedback();
        _document.ReplaceObject(after);
        if (after != before)
        {
            _history.RecordExecuted(new ReplaceObjectCommand(before, after));
        }

        SceneSurface.InvalidateVisual();
        UpdateSelectionPropertyBar();
    }

    /// <summary>
    /// The connector with the end being dragged where the pointer is, bound to
    /// what it was let go on. The other end is left exactly as it was, anchor
    /// and all.
    /// </summary>
    private ConnectorBoardObject MovedEndpoint(
        ConnectorBoardObject connector,
        PointD worldPoint,
        ConnectorAnchor? anchor)
    {
        PointD point = anchor is { } bound ? AnchorPoint(bound, worldPoint) : worldPoint;
        return _endpointIsStart
            ? connector.WithEndpoints(point, connector.End, anchor, connector.EndAnchor)
            : connector.WithEndpoints(connector.Start, point, connector.StartAnchor, anchor);
    }

    /// <summary>
    /// A connector kind for every selected connector, as one step, and what the
    /// next one is drawn as.
    /// </summary>
    private void ApplySelectionConnectorKind(ConnectorKind kind)
    {
        _connectorKind = kind;
        _settings.Connector.Kind = kind;
        PersistSettings();
        RestyleSelection(null, null, connector => connector.WithKind(kind));
    }

    /// <summary>
    /// Fixed or Auto for every selected connector, as one step, and what the
    /// next one is drawn with. Auto takes hold at once rather than waiting for
    /// the next time a shape moves, so the bar's answer is the one on the board.
    /// </summary>
    private void ApplySelectionAnchorMode(bool auto)
    {
        _settings.Connector.AutoRoute = auto;
        PersistSettings();
        RestyleSelection(null, null, connector => _document.Reroute(connector with { AutoRoute = auto }));
    }

    /// <summary>
    /// Deleting these objects, and freeing the connectors that pointed at them.
    /// An arrow whose shape is deleted stays where it was drawn rather than
    /// going with it, and the two changes are one step, so one undo restores
    /// both.
    /// </summary>
    private IBoardCommand DeleteCommandFor(IReadOnlyList<BoardObject> deletionGroup)
    {
        HashSet<Guid> removed = deletionGroup.Select(item => item.Id).ToHashSet();
        ConnectorBoardObject[] attached = removed
            .SelectMany(_document.ConnectorsAttachedTo)
            .Where(connector => !removed.Contains(connector.Id))
            .DistinctBy(connector => connector.Id)
            .ToArray();
        if (attached.Length == 0)
        {
            return new RemoveObjectsCommand(deletionGroup);
        }

        BoardObject[] detached = attached
            .Select(connector => (BoardObject)removed.Aggregate(
                connector,
                static (current, id) => current.Detach(id)))
            .ToArray();
        return new CompositeCommand(
        [
            new RemoveObjectsCommand(deletionGroup),
            new ReplaceObjectsCommand(attached, detached),
        ]);
    }

    private bool IsOverHandle(PointD screen, RectD bounds)
    {
        PointD handle = _camera.WorldToScreen(new PointD(bounds.Right, bounds.Bottom));
        return Distance(ToPoint(handle), ToPoint(screen)) <= 16;
    }

    /// <summary>
    /// The View row and the property bar's overflow, told what the selection can
    /// still do with its depth. Both offer the same four commands, so they are
    /// answered in one place rather than asking the same question twice.
    /// </summary>
    private void UpdateZOrderCommands()
    {
        BoardObject[] group = _textEditBefore is null ? SelectedZGroup() : [];
        var canBringToFront = group.Length > 0 && !IsZGroupAtFront(group);
        var canSendToBack = group.Length > 0 && !IsZGroupAtBack(group);
        SessionBar.SetZOrderEnabled(canBringToFront, canSendToBack);
        SelectionPropertyBar?.SetZOrderEnabled(canBringToFront, canSendToBack);
    }

    /// <summary>
    /// The selection and the strokes linked to it, in their own order. Reorder
    /// moves the whole block and keeps that order, so what was drawn over what
    /// inside the selection stays as it was.
    /// </summary>
    private BoardObject[] SelectedZGroup() =>
        _selectedObjectIds.Count == 0
            ? []
            : _document.GetDeletionGroup(_selectedObjectIds)
                .OrderBy(item => item.ZIndex)
                .ToArray();

    private bool IsZGroupAtFront(IReadOnlyList<BoardObject> group) =>
        ZOrder.IsAtFront(_document.Objects, group.Select(item => item.Id).ToArray());

    private bool IsZGroupAtBack(IReadOnlyList<BoardObject> group) =>
        ZOrder.IsAtBack(_document.Objects, group.Select(item => item.Id).ToArray());

    private void BringSelectionForward()
    {
        MoveSelectedZGroup(forward: true);
    }

    private void SendSelectionBackward()
    {
        MoveSelectedZGroup(forward: false);
    }

    /// <summary>
    /// The selection past the one object it meets next in the depth, as one
    /// step of the history. Bring to front and Send to back move it all the way;
    /// this is the same block moving by one.
    /// </summary>
    private void MoveSelectedZGroup(bool forward)
    {
        BoardObject[] group = SelectedZGroup();
        if (group.Length == 0)
        {
            return;
        }

        (IReadOnlyList<BoardObject> before, IReadOnlyList<BoardObject> after) = ZOrder.Step(
            _document.Objects,
            group.Select(item => item.Id).ToArray(),
            forward);
        if (before.Count == 0)
        {
            return;
        }

        _history.Execute(new ReplaceObjectsCommand(before, after), _document);
        UpdateZOrderCommands();
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
        BoardObject[] before = SelectedZGroup();
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
            .Select((item, index) => item.WithZIndex(start + index))
            .ToArray();
        if (before.SequenceEqual(after))
        {
            return;
        }

        _history.Execute(new ReplaceObjectsCommand(before, after), _document);
        UpdateZOrderCommands();
    }

    /// <summary>
    /// What the overflow menu asks for. Every item here is something the
    /// keyboard or the View row already does, called through the same method, so
    /// the menu cannot drift away from what the shortcut does.
    /// </summary>
    private void RunSelectionCommand(SelectionCommand command)
    {
        switch (command)
        {
            case SelectionCommand.Delete:
                DeleteSelection();
                break;
            case SelectionCommand.Copy:
                CopySelectionToClipboard();
                break;
            case SelectionCommand.Duplicate:
                DuplicateSelection();
                break;
            case SelectionCommand.BringToFront:
                BringSelectedContainerToFront();
                break;
            case SelectionCommand.BringForward:
                BringSelectionForward();
                break;
            case SelectionCommand.SendBackward:
                SendSelectionBackward();
                break;
            case SelectionCommand.SendToBack:
                SendSelectedContainerToBack();
                break;
        }
    }

    private void DeleteSelection()
    {
        if (_selectedObjectIds.Count == 0)
        {
            return;
        }

        IReadOnlyList<BoardObject> deletionGroup = _document.GetDeletionGroup(_selectedObjectIds);
        if (deletionGroup.Count == 0)
        {
            return;
        }

        _history.Execute(DeleteCommandFor(deletionGroup), _document);
        SelectOnly(null);
        SceneSurface.InvalidateVisual();
    }

    /// <summary>
    /// A copy of the selection, 24 screen pixels down and to the right of it and
    /// above everything on the board, which becomes the selection: pressing the
    /// shortcut again therefore lays the next copy 24 pixels further on. The
    /// assets are left alone, since a picture and its copy are the same picture.
    /// </summary>
    private void DuplicateSelection()
    {
        if (_selectedObjectIds.Count == 0 ||
            _textEditBefore is not null ||
            _labelEditBefore is not null ||
            _shapeEditBefore is not null)
        {
            return;
        }

        try
        {
            BoardObject[] selected = SelectedObjects();
            InkStrokeObject[] linkedStrokes = selected
                .Where(item => item is IBoardContainer)
                .SelectMany(item => _document.LinkedStrokes(item.Id))
                .Where(stroke => !_selectedObjectIds.Contains(stroke.Id))
                .DistinctBy(stroke => stroke.Id)
                .ToArray();
            var offset = 24 / Math.Max(_camera.Zoom, 0.000001);
            IReadOnlyList<BoardObject> copies = SelectionDuplicator.Duplicate(
                selected,
                linkedStrokes,
                selected.OfType<ConnectorBoardObject>().ToArray(),
                new PointD(offset, offset),
                _document.NextZIndex);
            if (copies.Count == 0)
            {
                return;
            }

            _history.Execute(new AddImportCommand(copies, []), _document);

            // The copies come back in the order they were given, so the first of
            // them are the ones the selection asked for; a stroke that came along
            // with its container is no more selected than it was before.
            SelectMany(copies.Take(selected.Length).Select(item => item.Id), extend: false);
            SceneSurface.InvalidateVisual();
        }
        catch (Exception exception)
        {
            ShowError("Could not duplicate the selection", exception);
        }
    }

    private double SurfacePixelsPerDip => VisualTreeHelper.GetDpi(SceneSurface).PixelsPerDip;

    /// <summary>
    /// A label where the Text tool was clicked, written in whatever the last
    /// label was written in, with its editor open on the empty text. The click
    /// is the top-left of the text, as it is in PowerPoint.
    /// </summary>
    private void InsertLabelAt(PointD screenPoint)
    {
        CommitTextEdit();
        LabelSettings defaults = _settings.Label;
        Size layout = LabelVisual.Measure(
            string.Empty,
            defaults.FontFamily,
            defaults.FontSize,
            defaults.Bold,
            defaults.Italic,
            SurfacePixelsPerDip);
        PointD topLeft = _camera.ScreenToWorld(screenPoint);
        var label = FreeTextBoardObject.Create(
            Guid.NewGuid(),
            _document.NextZIndex,
            RotatedRectangle.CenterFromTopLeft(topLeft, layout.Width, layout.Height, 0),
            string.Empty,
            defaults.FontFamily,
            defaults.FontSize,
            defaults.Argb,
            defaults.Bold,
            defaults.Italic,
            defaults.Underline,
            0,
            layout.Width,
            layout.Height);

        // Added outside the history on purpose: a label that is thought better
        // of leaves nothing behind, so the step is recorded on commit.
        _document.AddObject(label);
        BeginLabelEdit(label, isNew: true);
    }

    private void BeginLabelEdit(FreeTextBoardObject label, bool isNew)
    {
        if (_labelEditBefore?.Id == label.Id)
        {
            LabelEditor.Focus();
            return;
        }

        CommitTextEdit();
        ResetContainerGesture();
        _labelEditBefore = label;
        _labelEditCurrent = label;
        _labelEditIsNew = isNew;
        SceneSurface.HiddenObjectId = label.Id;
        SceneSurface.HoveredObjectId = null;
        SelectOnly(label.Id);

        OpenEditorOn(label.Text);
        UpdateLabelEditorOverlay();
        SceneSurface.InvalidateVisual();
        UpdateLiveViewActionOverlay();
        FocusEditor();
    }

    /// <summary>
    /// The editor's box filled with the text it starts on, without that reading
    /// as something typed.
    /// </summary>
    private void OpenEditorOn(string text)
    {
        _updatingLabelEditor = true;
        LabelEditor.Text = text;
        _updatingLabelEditor = false;
    }

    /// <summary>
    /// The keyboard in the editor, now and again once the layout has settled.
    /// Now, because a key that opened the editor carries a character behind it
    /// that has to land in the box; again, because the box is placed and sized
    /// after this and focus taken before that has been known to be given up.
    /// </summary>
    private void FocusEditor()
    {
        LabelEditor.Focus();
        Keyboard.Focus(LabelEditor);
        LabelEditor.CaretIndex = LabelEditor.Text.Length;
        _ = Dispatcher.InvokeAsync(
            () =>
            {
                LabelEditor.Focus();
                Keyboard.Focus(LabelEditor);
                LabelEditor.CaretIndex = LabelEditor.Text.Length;
            },
            DispatcherPriority.Input);
    }

    /// <summary>
    /// The editor written in the same hand as what it is editing, at the size
    /// that hand takes on screen.
    /// </summary>
    private void StyleEditor(
        string fontFamily,
        double fontSize,
        bool bold,
        bool italic,
        bool underline,
        uint argb)
    {
        LabelEditor.FontFamily = new FontFamily(fontFamily);
        LabelEditor.FontSize = Math.Max(1, fontSize);
        LabelEditor.FontWeight = bold ? FontWeights.Bold : FontWeights.Normal;
        LabelEditor.FontStyle = italic ? FontStyles.Italic : FontStyles.Normal;
        LabelEditor.TextDecorations = underline ? TextDecorations.Underline : null;
        LabelEditor.Foreground = LabelVisual.Brush(argb);
        LabelEditor.CaretBrush = LabelEditor.Foreground;
    }

    /// <summary>
    /// The label as it now stands, recorded as one step. A label with nothing
    /// in it is not a label: a new one leaves no trace, and one that had text
    /// before is removed as a step that can be undone.
    /// </summary>
    private void CommitLabelEdit()
    {
        if (_labelEditBefore is not { } before || _labelEditCurrent is not { } current)
        {
            return;
        }

        var wasNew = _labelEditIsNew;
        EndLabelEditVisual();
        if (string.IsNullOrWhiteSpace(current.Text))
        {
            if (wasNew)
            {
                _document.RemoveObject(current.Id);
            }
            else
            {
                _document.ReplaceObject(before);
                _history.Execute(DeleteCommandFor([before]), _document);
            }

            SelectOnly(null);
            SceneSurface.InvalidateVisual();
            return;
        }

        if (wasNew)
        {
            _history.RecordExecuted(new AddObjectCommand(current));
        }
        else if (current != before)
        {
            _history.RecordExecuted(new ReplaceObjectCommand(before, current));
        }

        SelectOnly(current.Id);
        SceneSurface.InvalidateVisual();
        ReturnToSelectAfterInsert();
    }

    /// <summary>
    /// What the Insert tools do once one object has been made. Handing the tool
    /// back is the default, so the new object is what the next tap picks up;
    /// the preference keeps the tool for anyone drawing a row of them.
    /// </summary>
    private void ReturnToSelectAfterInsert()
    {
        if (_settings.AfterInsert == AfterInsert.ReturnToSelect &&
            _activeTool is BoardTool.Shape or BoardTool.Connector or BoardTool.Text)
        {
            SetActiveTool(BoardTool.Select);
        }
    }

    private void CancelLabelEdit()
    {
        if (_labelEditBefore is not { } before || _labelEditCurrent is not { } current)
        {
            return;
        }

        var wasNew = _labelEditIsNew;
        EndLabelEditVisual();
        if (wasNew)
        {
            _document.RemoveObject(current.Id);
            SelectOnly(null);
        }
        else
        {
            _document.ReplaceObject(before);
            SelectOnly(before.Id);
        }

        SceneSurface.InvalidateVisual();
    }

    private void EndLabelEditVisual()
    {
        _labelEditBefore = null;
        _labelEditCurrent = null;
        _labelEditIsNew = false;
        _updatingLabelEditor = false;
        LabelEditor.Visibility = Visibility.Collapsed;
        SceneSurface.HiddenObjectId = null;
        InkSurface.Focus();
    }

    private void LabelEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingLabelEditor)
        {
            return;
        }

        if (_shapeEditCurrent is { } shape)
        {
            ApplyShapeDuringEdit(shape with { Text = LabelEditor.Text });
            return;
        }

        if (_labelEditCurrent is not { } label)
        {
            return;
        }

        ApplyLabelDuringEdit(Remeasure(label with { Text = LabelEditor.Text }));
    }

    /// <summary>
    /// The label re-measured for what it now says and how it is written. Only
    /// the window can measure text, so every change to either comes through
    /// here before the object is put back.
    /// </summary>
    private FreeTextBoardObject Remeasure(FreeTextBoardObject label)
    {
        Size layout = LabelVisual.Measure(label, SurfacePixelsPerDip);
        return label.WithLayout(label.Text, layout.Width, layout.Height);
    }

    /// <summary>
    /// A change to the label being typed. It goes straight into the document
    /// rather than through the history, because the whole edit is one step and
    /// that step is recorded when the editor closes.
    /// </summary>
    private void ApplyLabelDuringEdit(FreeTextBoardObject label)
    {
        _labelEditCurrent = label;
        _document.ReplaceObject(label);
        UpdateLabelEditorOverlay();
        UpdateSelectionPropertyBar();
    }

    /// <summary>
    /// The editor over the label it is editing: the same font at the same size
    /// on screen, turned by the same angle. The box is positioned by the
    /// label's own bounds, which are the bounds of the turned rectangle, and so
    /// are the bounds of the turned editor.
    /// </summary>
    private void UpdateLabelEditorOverlay()
    {
        // The same box serves a shape's own text, over the shape rather than
        // over a label of its own.
        if (_shapeEditCurrent is not null)
        {
            UpdateShapeTextEditorOverlay();
            return;
        }

        if (_labelEditCurrent is not { } label)
        {
            LabelEditor.Visibility = Visibility.Collapsed;
            return;
        }

        double zoom = _camera.Zoom;
        StyleEditor(
            label.FontFamily,
            label.FontSize * zoom,
            label.Bold,
            label.Italic,
            label.Underline,
            label.Argb);
        LabelEditor.TextWrapping = TextWrapping.NoWrap;
        LabelEditor.TextAlignment = TextAlignment.Left;
        LabelEditor.VerticalContentAlignment = VerticalAlignment.Top;
        LabelEditor.MinHeight = 0;

        // Room for the border, the box's own inset, and the caret at the end of
        // the longest line, none of which the measured text accounts for. The
        // box is a little wider than the label it stands over; what matters is
        // that the text is never clipped while it is being typed.
        LabelEditor.Width = (label.LayoutWidth * zoom) + 12;
        LabelEditor.Height = (label.LayoutHeight * zoom) + 6;
        LabelEditorRotation.Angle = label.AngleDegrees;
        PointD topLeft = _camera.WorldToScreen(new PointD(label.Bounds.Left, label.Bounds.Top));
        Canvas.SetLeft(LabelEditor, topLeft.X);
        Canvas.SetTop(LabelEditor, topLeft.Y);
        LabelEditor.Visibility = Visibility.Visible;
    }

    private void ApplySelectionFont(string fontFamily)
    {
        var font = LabelStyles.NormalizeFont(fontFamily);
        _settings.Label.FontFamily = font;
        PersistSettings();
        RestyleSelectedLabels(label => label with { FontFamily = font });
        RestyleSelectedShapeText(shape => shape with { FontFamily = font });
    }

    private void ApplySelectionFontSize(double fontSize)
    {
        _settings.Label.FontSize = LabelStyles.NormalizeFontSize(fontSize);
        PersistSettings();
        RestyleSelectedLabels(label => label with { FontSize = fontSize });
        RestyleSelectedShapeText(shape => shape with { FontSize = fontSize });
    }

    private void ApplySelectionFontStyle(LabelFontStyle style, bool on)
    {
        switch (style)
        {
            case LabelFontStyle.Bold:
                _settings.Label.Bold = on;
                break;
            case LabelFontStyle.Italic:
                _settings.Label.Italic = on;
                break;
            default:
                _settings.Label.Underline = on;
                break;
        }

        PersistSettings();
        RestyleSelectedLabels(label => style switch
        {
            LabelFontStyle.Bold => label with { Bold = on },
            LabelFontStyle.Italic => label with { Italic = on },
            _ => label with { Underline = on },
        });
        RestyleSelectedShapeText(shape => style switch
        {
            LabelFontStyle.Bold => shape with { Bold = on },
            LabelFontStyle.Italic => shape with { Italic = on },
            _ => shape with { Underline = on },
        });
    }

    /// <summary>
    /// A quarter turn from the property bar, for every selected label and
    /// shape, as one step - with the ink linked to what turned and the
    /// connectors bound to it brought round too, since a turn moves the points
    /// an arrow is tied to as surely as a move does. The step lands on the
    /// nearest multiple of itself, so an object turned freely by the handle
    /// comes back onto the grid with one press.
    /// </summary>
    private void StepSelectionRotation(double degrees)
    {
        if (_labelEditCurrent is not null)
        {
            RestyleSelectedLabels(label =>
                label.WithAngle(RotatedRectangle.StepAngle(label.AngleDegrees, degrees)));
            return;
        }

        var before = new List<BoardObject>();
        var after = new List<BoardObject>();
        var turns = new List<(BoardObject Item, double Degrees)>();
        foreach (BoardObject item in SelectedObjects())
        {
            if (item is not (FreeTextBoardObject or ShapeBoardObject))
            {
                continue;
            }

            var angle = RotatedRectangle.StepAngle(AngleOf(item), degrees);
            BoardObject turned = item switch
            {
                FreeTextBoardObject label => Remeasure(label.WithAngle(angle)),
                _ => ((ShapeBoardObject)item).WithAngle(angle),
            };
            if (turned == item)
            {
                continue;
            }

            before.Add(item);
            after.Add(turned);
            turns.Add((item, angle - AngleOf(item)));
        }

        if (before.Count == 0)
        {
            return;
        }

        AddRotationFollowers(
            turns,
            after.ToDictionary(item => item.Id),
            turns.SelectMany(turn => _document.LinkedStrokes(turn.Item.Id)).DistinctBy(stroke => stroke.Id).ToArray(),
            turns.SelectMany(turn => _document.ConnectorsAttachedTo(turn.Item.Id))
                .DistinctBy(connector => connector.Id)
                .ToArray(),
            before,
            after);
        _history.Execute(new ReplaceObjectsCommand(before.ToArray(), after.ToArray()), _document);
        SceneSurface.InvalidateVisual();
        UpdateSelectionPropertyBar();
        InkSurface.Focus();
    }

    /// <summary>
    /// A change from the font row, applied to every selected label as one step
    /// - or, while one is being typed, to that one as part of the edit, so the
    /// box under the hand changes with it.
    /// </summary>
    private void RestyleSelectedLabels(Func<FreeTextBoardObject, FreeTextBoardObject> restyle)
    {
        if (_labelEditCurrent is { } editing)
        {
            ApplyLabelDuringEdit(Remeasure(restyle(editing)));
            LabelEditor.Focus();
            return;
        }

        FreeTextBoardObject[] before = SelectedObjects().OfType<FreeTextBoardObject>().ToArray();
        if (before.Length == 0)
        {
            return;
        }

        BoardObject[] after = before.Select(label => (BoardObject)Remeasure(restyle(label))).ToArray();
        if (after.SequenceEqual<BoardObject>(before))
        {
            return;
        }

        _history.Execute(new ReplaceObjectsCommand(before, after), _document);
        SceneSurface.InvalidateVisual();
        InkSurface.Focus();
    }

    /// <summary>
    /// The Text button on the property bar, which is offered for one shape at a
    /// time because there is one editor and it stands over one shape.
    /// </summary>
    private void BeginSelectedShapeTextEdit()
    {
        if (SingleSelected<ShapeBoardObject>() is { } shape)
        {
            BeginShapeTextEdit(shape, replaceText: false);
        }
    }

    /// <summary>
    /// The label's editor, opened over a shape's own text box instead: the same
    /// box, the same commit and cancel, and the shape underneath left drawn
    /// except for the words being typed. With <paramref name="replaceText"/> the
    /// shape starts the edit saying nothing, which is what typing on a selected
    /// shape does - the character that opened the editor is then the whole text,
    /// as it is in PowerPoint.
    /// </summary>
    private void BeginShapeTextEdit(ShapeBoardObject shape, bool replaceText)
    {
        if (_shapeEditBefore?.Id == shape.Id)
        {
            LabelEditor.Focus();
            return;
        }

        CommitTextEdit();
        ResetContainerGesture();
        ShapeBoardObject current = replaceText ? shape with { Text = string.Empty } : shape;
        _shapeEditBefore = shape;
        _shapeEditCurrent = current;
        SceneSurface.HiddenTextObjectId = shape.Id;
        SceneSurface.HoveredObjectId = null;
        SelectOnly(shape.Id);
        if (current != shape)
        {
            _document.ReplaceObject(current);
        }

        OpenEditorOn(current.Text);
        UpdateLabelEditorOverlay();
        SceneSurface.InvalidateVisual();
        UpdateLiveViewActionOverlay();
        FocusEditor();
    }

    /// <summary>
    /// The shape as it now reads, recorded as one step. An empty text is an
    /// answer like any other: the shape stays, saying nothing, which is where it
    /// started from.
    /// </summary>
    private void CommitShapeTextEdit()
    {
        if (_shapeEditBefore is not { } before || _shapeEditCurrent is not { } current)
        {
            return;
        }

        EndShapeTextEditVisual();
        _document.ReplaceObject(current);
        if (current != before)
        {
            _history.RecordExecuted(new ReplaceObjectCommand(before, current));
        }

        SelectOnly(current.Id);
        SceneSurface.InvalidateVisual();
    }

    private void CancelShapeTextEdit()
    {
        if (_shapeEditBefore is not { } before)
        {
            return;
        }

        EndShapeTextEditVisual();
        _document.ReplaceObject(before);
        SelectOnly(before.Id);
        SceneSurface.InvalidateVisual();
    }

    private void EndShapeTextEditVisual()
    {
        _shapeEditBefore = null;
        _shapeEditCurrent = null;
        _updatingLabelEditor = false;
        LabelEditor.Visibility = Visibility.Collapsed;
        SceneSurface.HiddenTextObjectId = null;
        InkSurface.Focus();
    }

    /// <summary>
    /// A change to the shape being typed in. As with a label it goes straight
    /// into the document rather than through the history, because the whole edit
    /// is one step and that step is recorded when the editor closes.
    /// </summary>
    private void ApplyShapeDuringEdit(ShapeBoardObject shape)
    {
        _shapeEditCurrent = shape;
        _document.ReplaceObject(shape);
        UpdateLabelEditorOverlay();
        UpdateSelectionPropertyBar();
    }

    /// <summary>
    /// The editor over the shape's text box: the same rectangle the board lays
    /// the text out in, turned by the shape's own angle, with the text centred
    /// across it and down it. The box grows downward when there is more text
    /// than room, which is what the board does with it too.
    /// </summary>
    private void UpdateShapeTextEditorOverlay()
    {
        if (_shapeEditCurrent is not { } shape)
        {
            LabelEditor.Visibility = Visibility.Collapsed;
            return;
        }

        double zoom = _camera.Zoom;
        StyleEditor(
            shape.FontFamily,
            shape.FontSize * zoom,
            shape.Bold,
            shape.Italic,
            shape.Underline,
            shape.TextArgb);
        LabelEditor.TextWrapping = TextWrapping.Wrap;
        LabelEditor.TextAlignment = TextAlignment.Center;
        LabelEditor.VerticalContentAlignment = VerticalAlignment.Center;

        // The border is the one pixel the text box has that the text box on the
        // board has not, so the content inside it is the width the words wrap
        // at there and the lines break in the same places.
        const double border = 1;
        RectD box = shape.TextBounds;
        LabelEditor.Width = (box.Width * zoom) + (2 * border);
        LabelEditor.Height = double.NaN;
        LabelEditor.MinHeight = (box.Height * zoom) + (2 * border);
        LabelEditorRotation.Angle = shape.AngleDegrees;

        // The box is placed by the corner of its own turned bounds, as the
        // label's is, because that is what a layout transform leaves on the
        // canvas.
        PointD center = shape.AnchorFrame.ToWorld(box.Center);
        RectD turned = RotatedRectangle.Bounds(center, box.Width, box.Height, shape.AngleDegrees);
        PointD topLeft = _camera.WorldToScreen(new PointD(turned.Left, turned.Top));
        Canvas.SetLeft(LabelEditor, topLeft.X - border);
        Canvas.SetTop(LabelEditor, topLeft.Y - border);
        LabelEditor.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// A change from the font row or the text swatches, applied to every
    /// selected shape as one step - or, while one is being typed in, to that one
    /// as part of the edit, so the box under the hand changes with it.
    /// </summary>
    private void RestyleSelectedShapeText(Func<ShapeBoardObject, ShapeBoardObject> restyle)
    {
        if (_shapeEditCurrent is { } editing)
        {
            ApplyShapeDuringEdit(restyle(editing));
            LabelEditor.Focus();
            return;
        }

        ShapeBoardObject[] before = SelectedObjects().OfType<ShapeBoardObject>().ToArray();
        if (before.Length == 0)
        {
            return;
        }

        BoardObject[] after = before.Select(shape => (BoardObject)restyle(shape)).ToArray();
        if (after.SequenceEqual<BoardObject>(before))
        {
            return;
        }

        _history.Execute(new ReplaceObjectsCommand(before, after), _document);
        SceneSurface.InvalidateVisual();
        InkSurface.Focus();
    }

    /// <summary>
    /// A color for the words inside every selected shape, remembered as what the
    /// next text is written in. The outline keeps the color row above it.
    /// </summary>
    private void ApplySelectionTextColor(uint argb)
    {
        _settings.Label.Argb = argb;
        PersistSettings();
        RestyleSelectedShapeText(shape => shape with { TextArgb = argb });
    }

    /// <summary>
    /// Typing on a lone selected shape starts its text, as PowerPoint does. The
    /// key is left unhandled on purpose: the character it carries arrives as
    /// text input straight after, and the editor has the keyboard by then, so
    /// every layout, dead key, and input method puts in what it would anywhere
    /// else. Space is not one of these keys - it is the temporary pan, and has
    /// been since long before a shape could be typed in.
    /// </summary>
    private void StartShapeTextTyping(KeyEventArgs e)
    {
        if (_activeTool != BoardTool.Select ||
            Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ||
            Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) ||
            SessionBar.IsCommandRowOpen ||
            IsControlFocused() ||
            !IsTypingKey(e.Key) ||
            SingleSelected<ShapeBoardObject>() is not { } shape)
        {
            return;
        }

        BeginShapeTextEdit(shape, replaceText: true);
    }

    /// <summary>
    /// Whether this key writes a character: the letters, the digits, the number
    /// pad, and the punctuation keys, whatever they carry on this keyboard. The
    /// ranges are the enum's own order, so a layout nobody here has is covered
    /// by the key it sits on rather than by the character it produces.
    /// </summary>
    private static bool IsTypingKey(Key key) =>
        key is >= Key.D0 and <= Key.Z ||
        key is >= Key.NumPad0 and <= Key.Divide ||
        key is >= Key.Oem1 and <= Key.OemBackslash;

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
        _selectedObjectIds.Clear();
        _selectedObjectIds.Add(textObject.Id);
        SceneSurface.SelectedObjectIds = new HashSet<Guid>();
        SceneSurface.HoveredObjectId = null;
        HideSelectionPropertyBar();
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
        // Every click-away, tool change, and save already comes through here,
        // and a label - or a shape's own text - is the same edit by another
        // editor, so they settle here too rather than at each of those call
        // sites again.
        CommitLabelEdit();
        CommitShapeTextEdit();
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
        SelectOnly(selectedObjectId);
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
        _promptBulletGenerator.IsEnabled = language.Id == TextLanguageIds.Prompt;
        TextEditor.TextArea.TextView.Redraw();
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

    /// <summary>
    /// The one answer F6 gives a language with no formatter, shared by both
    /// entry points. A language that is only colored offers its voting issue;
    /// the rest fall through to their own formatter, so invalid DAX stays a
    /// format that did nothing rather than a prompt.
    /// </summary>
    private bool OfferFormattingRequest(ITextLanguageService language)
    {
        if (language.FormattingRequestUri is not { } requestUri)
        {
            return false;
        }

        if (_formattingRequestOpen)
        {
            return true;
        }

        _formattingRequestOpen = true;
        try
        {
            new FormattingRequestWindow(language.DisplayName, requestUri) { Owner = this }.ShowDialog();
        }
        finally
        {
            _formattingRequestOpen = false;
        }

        if (_textEditBefore is not null)
        {
            TextEditor.Focus();
            Keyboard.Focus(TextEditor);
        }
        else
        {
            InkSurface.Focus();
        }

        return true;
    }

    private void FormatTextEdit()
    {
        if (_textEditBefore is null)
        {
            return;
        }

        ITextLanguageService language = TextLanguageRegistry.Resolve(_textEditLanguageId);
        if (OfferFormattingRequest(language))
        {
            return;
        }

        int editColumns = TextContainerVisual.ColumnsFor(
            _textEditBounds.Width,
            _textEditBefore.VisualScale,
            _textEditLanguageId,
            VisualTreeHelper.GetDpi(SceneSurface).PixelsPerDip);
        if (!language.CanFormat ||
            !language.TryFormat(TextEditor.Text, editColumns, out string formatted) ||
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
        if (OfferFormattingRequest(language))
        {
            return;
        }

        // Formatted to the columns the container shows, so the lines fit it:
        // the container's width is the line width of the snippet.
        int columns = TextContainerVisual.ColumnsFor(textObject, VisualTreeHelper.GetDpi(SceneSurface).PixelsPerDip);
        if (!language.CanFormat ||
            !language.TryFormat(textObject.Text, columns, out string formatted) ||
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
            ChooseTool(BoardTool.Pen);
            return;
        }

        if (_activeTool is BoardTool.Pen or BoardTool.Calligraphy)
        {
            ToggleInkOptions();
            ChooseTool(_activeTool);
            return;
        }

        ChooseTool(
            _lastDrawingTool == BoardTool.Calligraphy
                ? BoardTool.Calligraphy
                : BoardTool.Pen);
    }

    private void HighlighterToolButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem)
        {
            ChooseTool(BoardTool.Highlighter);
            return;
        }

        if (_activeTool == BoardTool.Highlighter)
        {
            ToggleInkOptions();
            ChooseTool(BoardTool.Highlighter);
            return;
        }

        ChooseTool(BoardTool.Highlighter);
    }

    private void CalligraphyToolButton_Click(object sender, RoutedEventArgs e) =>
        ChooseTool(BoardTool.Calligraphy);

    private void PenNibButton_Click(object sender, RoutedEventArgs e)
    {
        SetNibPickerOpen(false);
        ChooseTool(BoardTool.Pen);
    }

    private void CalligraphyNibButton_Click(object sender, RoutedEventArgs e)
    {
        SetNibPickerOpen(false);
        ChooseTool(BoardTool.Calligraphy);
    }

    private void PenChevronButton_Click(object sender, RoutedEventArgs e) =>
        SetNibPickerOpen(!_isNibPickerOpen);

    private void EraserToolButton_Click(object sender, RoutedEventArgs e)
    {
        LeaveLaserIfActive();
        ChooseTool(BoardTool.Eraser);
    }

    private void SelectToolButton_Click(object sender, RoutedEventArgs e)
    {
        LeaveLaserIfActive();

        // The style clicks on press, so this is the press. A press while Select
        // is already in hand switches the area tool, which is the mouse's way to
        // it: nobody with a mouse will wait out a long press.
        var switchArea = _activeTool == BoardTool.Select;
        SetActiveTool(BoardTool.Select);
        if (switchArea)
        {
            ToggleAreaSelectionTool();
        }
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
        ChooseTool(BoardTool.Pen);
    }

    private void DualCalligraphyButton_Click(object sender, RoutedEventArgs e)
    {
        LeaveLaserIfActive();
        ChooseTool(BoardTool.Calligraphy);
    }

    private void DualHighlighterButton_Click(object sender, RoutedEventArgs e)
    {
        LeaveLaserIfActive();
        ChooseTool(BoardTool.Highlighter);
    }

    /// <summary>
    /// A tool picked by hand. Reaching for something to draw with says the
    /// selection is finished with, so it goes - unlike the tool handed back
    /// after a borrowed mouse gesture, which is not a choice anybody made.
    /// </summary>
    private void ChooseTool(BoardTool tool)
    {
        if (tool is not (BoardTool.Select or BoardTool.Pan or BoardTool.Laser))
        {
            ClearSelection();
        }

        SetActiveTool(tool);
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

        if (tool is not (BoardTool.Shape or BoardTool.Connector or BoardTool.Text))
        {
            SetInsertOptionsOpen(false);
        }

        if (tool != BoardTool.Shape)
        {
            ResetShapeGesture();
        }

        if (tool != BoardTool.Connector)
        {
            ResetConnectorGesture();
        }

        SetSelectOptionsOpen(false);

        if (tool == BoardTool.Select)
        {
            HidePointerDot();
            InkSurface.Cursor = Cursors.Arrow;
        }
        else if (tool is BoardTool.Shape or BoardTool.Connector)
        {
            HidePointerDot();
            InkSurface.Cursor = Cursors.Cross;
        }

        if (tool == BoardTool.Text)
        {
            HidePointerDot();
            InkSurface.Cursor = Cursors.IBeam;
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
        UpdateInsertButtonChecks();
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
        }
        else
        {
            ApplyInkOptionsWidth();
            if (_isInkOptionsOpen)
            {
                RebuildInkOptions();
            }
        }

        // The Eraser sits in the bar in the compact layouts and under it in the
        // dual palette, so changing the layout moves it.
        ApplyExtraTools();
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
        ChooseTool(tool);
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
        ChooseTool(BoardTool.Highlighter);
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
        if (_gestureBefore.Length > 0 || _areaActive)
        {
            return;
        }

        UpdateConnectorHandleHover(screen);

        BoardObject? hovered =
            _document.HitTestTopSelectable(_camera.ScreenToWorld(screen), _camera.Zoom)
            ?? FindTextContainerAtRightEdge(screen);
        var hoveredId = hovered?.Id;
        if (SceneSurface.HoveredObjectId == hoveredId)
        {
            return;
        }

        SceneSurface.HoveredObjectId = hoveredId;
        SceneSurface.InvalidateVisual();
    }

    /// <summary>
    /// The connector handle the pointer is on, drawn filled so that the one an
    /// arrow would come out of is the one that answers.
    /// </summary>
    private void UpdateConnectorHandleHover(PointD screen)
    {
        FrameSide? side = IsOverRotationHandle(screen) ? null : ConnectorHandleAt(screen)?.Handle.Side;
        if (SceneSurface.HoveredConnectorHandle == side)
        {
            return;
        }

        SceneSurface.HoveredConnectorHandle = side;
        SceneSurface.InvalidateVisual();
    }

    private Cursor SelectCursorAt(PointD screen)
    {
        if (IsOverRotationHandle(screen))
        {
            return RotationCursor;
        }

        // Windows has no "start an arrow here" cursor either; the crosshair is
        // what every drawing tool means by it.
        if (ConnectorHandleAt(screen) is not null)
        {
            return Cursors.Cross;
        }

        if (IsOverResizeHandle(screen))
        {
            return Cursors.SizeNWSE;
        }

        if (FindTextContainerAtRightEdge(screen) is not null)
        {
            return Cursors.SizeWE;
        }

        return _document.HitTestTopSelectable(_camera.ScreenToWorld(screen), _camera.Zoom) is null
            ? Cursors.Arrow
            : Cursors.SizeAll;
    }

    /// <summary>
    /// The right edge of a text container is its width handle: dragging it
    /// changes the columns and reflows, as Shift on the corner does, but with
    /// a cursor that says so on hover. The corner itself stays the scale handle.
    /// </summary>
    private const double EdgeHandleTolerance = 8;

    private TextBoardObject? FindTextContainerAtRightEdge(PointD screen)
    {
        if (IsOverResizeHandle(screen))
        {
            return null;
        }

        foreach (var text in _document.Objects.OfType<TextBoardObject>().OrderByDescending(item => item.ZIndex))
        {
            var topRight = _camera.WorldToScreen(new PointD(text.Bounds.Right, text.Bounds.Top));
            var bottom = _camera.WorldToScreen(new PointD(text.Bounds.Right, text.Bounds.Bottom)).Y;
            if (Math.Abs(screen.X - topRight.X) <= EdgeHandleTolerance &&
                screen.Y >= topRight.Y - EdgeHandleTolerance &&
                screen.Y <= bottom - 16)
            {
                return text;
            }
        }

        return null;
    }

    // A lone connector has no corner handle: its box is a box around a line,
    // and what it offers instead is its two ends.
    private bool IsOverResizeHandle(PointD screen) =>
        SingleSelected<ConnectorBoardObject>() is null &&
        SelectionBounds() is RectD bounds &&
        IsOverHandle(screen, bounds);

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

    /// <summary>
    /// The shape the next area gesture draws. The Select button remembers it,
    /// so the choice outlives the gesture and the session.
    /// </summary>
    private void ToggleAreaSelectionTool() => ApplyAreaSelectionTool(
        IsLassoArea ? AreaSelectionTool.Rectangle : AreaSelectionTool.Lasso);

    /// <summary>
    /// The area tool, wherever it was chosen: the hold on Select, the second
    /// tap, or the chevron's flyout. All three end here, so the button's glyph
    /// and the flyout say the same thing however the choice was made.
    /// </summary>
    private void ApplyAreaSelectionTool(AreaSelectionTool tool)
    {
        _settings.AreaSelectionTool = tool;
        PersistSettings();
        UpdateSelectButtonGlyph();
        if (_isSelectOptionsOpen)
        {
            RebuildSelectOptions();
        }
    }

    /// <summary>
    /// Which of the two the Select button is holding. The glyph is the only
    /// place the mode is written down now that the Edit row's toggle has gone,
    /// so it is swapped in place, at the same size, rather than badged.
    /// </summary>
    private void UpdateSelectButtonGlyph()
    {
        var lasso = IsLassoArea;
        var geometry = (Geometry)FindResource(lasso ? "LassoGeometry" : "ImageSelectGeometry");
        var tooltip = lasso
            ? "Lasso. Hold, or tap again, for Rectangle"
            : "Select. Hold, or tap again, for Lasso";
        var name = lasso ? "Lasso" : "Select";
        if (SelectToolIcon is not null)
        {
            SelectToolIcon.Data = geometry;
            SelectToolButton.ToolTip = tooltip;
            AutomationProperties.SetName(SelectToolButton, name);
        }

        if (DualSelectIcon is not null)
        {
            DualSelectIcon.Data = geometry;
            DualSelectButton.ToolTip = tooltip;
            AutomationProperties.SetName(DualSelectButton, name);
        }
    }

    // The Windows touch long press. Holding Select this long is how a pen and a
    // finger reach the other area tool, on a toolbar that is not allowed to grow
    // a second button for it.
    private static readonly TimeSpan SelectHoldDelay = TimeSpan.FromMilliseconds(600);

    private DispatcherTimer? _selectHoldTimer;

    /// <summary>
    /// A press on Select, from any input. The press has already chosen the tool
    /// - the style clicks on press - so the timer adds only the switch, and only
    /// when Select was not already in hand: when it was, the press itself has
    /// switched the area tool and a hold would switch it straight back. A press
    /// already being counted is left alone, so the mouse event a pen tap is
    /// promoted to does not restart the count.
    /// </summary>
    private void BeginSelectHold(object source)
    {
        if (_selectHoldTimer is not null ||
            _activeTool == BoardTool.Select ||
            (!ReferenceEquals(source, SelectToolButton) && !ReferenceEquals(source, DualSelectButton)))
        {
            return;
        }

        _selectHoldTimer = new DispatcherTimer { Interval = SelectHoldDelay };
        _selectHoldTimer.Tick += SelectHoldTimer_Tick;
        _selectHoldTimer.Start();
    }

    private void CancelSelectHold()
    {
        if (_selectHoldTimer is not { } timer)
        {
            return;
        }

        timer.Stop();
        timer.Tick -= SelectHoldTimer_Tick;
        _selectHoldTimer = null;
    }

    private void SelectHoldTimer_Tick(object? sender, EventArgs e)
    {
        CancelSelectHold();
        ToggleAreaSelectionTool();
    }

    private void SelectButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        BeginSelectHold(sender);

    private void SelectButton_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
        CancelSelectHold();

    private void SelectButton_MouseLeave(object sender, MouseEventArgs e) =>
        CancelSelectHold();

    private void SelectButton_LostMouseCapture(object sender, MouseEventArgs e) =>
        CancelSelectHold();

    // The palette promotes a pen or finger tap itself, so the button never sees
    // the stylus press that started the hold and cannot see the lift either.
    private void Window_PreviewStylusUp(object sender, StylusEventArgs e) =>
        CancelSelectHold();

    /// <summary>
    /// The Insert button beside Select and the chevron on Select, which exist
    /// only while the preference asks for them. Collapsed they take no width, so
    /// the toolbar the default setup shows is the one it has always been - which
    /// matters because it sits under a presenter picture-in-picture.
    /// </summary>
    private void ApplyInsertOnToolbar()
    {
        var on = _settings.InsertOnToolbar;
        Visibility visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (InsertToolButton is not null)
        {
            InsertToolButton.Visibility = visibility;
        }

        if (DualInsertButton is not null)
        {
            DualInsertButton.Visibility = visibility;
        }

        if (SelectChevronButton is not null)
        {
            SelectChevronButton.Visibility = visibility;
        }

        if (!on)
        {
            SetInsertOptionsOpen(false);
            SetSelectOptionsOpen(false);
        }
    }

    private void InsertToolButton_Click(object sender, RoutedEventArgs e)
    {
        SetSelectOptionsOpen(false);
        SetInsertOptionsOpen(!_isInsertOptionsOpen);
    }

    private void SelectChevronButton_Click(object sender, RoutedEventArgs e)
    {
        SetInsertOptionsOpen(false);
        SetSelectOptionsOpen(!_isSelectOptionsOpen);
    }

    private void SetInsertOptionsOpen(bool open)
    {
        _isInsertOptionsOpen = open && _settings.InsertOnToolbar;
        if (InsertOptionsPanel is not null)
        {
            InsertOptionsPanel.Visibility = _isInsertOptionsOpen
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        if (_isInsertOptionsOpen)
        {
            RebuildInsertOptions();
        }

        UpdateInsertButtonChecks();
    }

    private void SetSelectOptionsOpen(bool open)
    {
        _isSelectOptionsOpen = open && _settings.InsertOnToolbar;
        if (SelectOptionsPanel is not null)
        {
            SelectOptionsPanel.Visibility = _isSelectOptionsOpen
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        if (SelectChevronButton is not null)
        {
            SelectChevronButton.IsChecked = _isSelectOptionsOpen;
        }

        if (_isSelectOptionsOpen)
        {
            RebuildSelectOptions();
        }
    }

    /// <summary>
    /// The Insert button reads as on while its flyout is open as well as while
    /// the shape tool is active: the flyout is the button's own state, and the
    /// tool may not have changed yet.
    /// </summary>
    private void UpdateInsertButtonChecks()
    {
        var on = _activeTool is BoardTool.Shape or BoardTool.Connector or BoardTool.Text ||
                 _isInsertOptionsOpen;
        if (InsertToolButton is not null)
        {
            InsertToolButton.IsChecked = on;
        }

        if (DualInsertButton is not null)
        {
            DualInsertButton.IsChecked = on;
        }

        if (_settings.InsertPaletteShown)
        {
            RebuildInsertPalette();
        }
    }

    // Four to a row, so the eight shapes are two rows no wider than the ink
    // options under the same toolbar.
    private const int InsertOptionsColumns = 4;

    /// <summary>
    /// What the Insert flyout offers, in the order the Insert row offers it. The
    /// connectors and Text join this list, and the flyout follows from it.
    /// </summary>
    private static IReadOnlyList<ShapeKind> InsertShapes { get; } = Enum.GetValues<ShapeKind>();

    private void RebuildInsertOptions()
    {
        if (InsertOptionsHost is null)
        {
            return;
        }

        InsertOptionsHost.Children.Clear();
        StackPanel? row = null;
        var index = 0;
        foreach (ShapeKind kind in InsertShapes)
        {
            if (index % InsertOptionsColumns == 0)
            {
                row = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, index == 0 ? 0 : 2, 0, 0),
                };
                InsertOptionsHost.Children.Add(row);
            }

            row?.Children.Add(CreateInsertButton(kind));
            index++;
        }

        // The connectors, then Text, each on its own row: the flyout follows the
        // Insert row, where a separator stands between the three groups.
        row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
        };
        InsertOptionsHost.Children.Add(row);
        foreach ((ConnectorKind kind, string name) in PropertyBar.ConnectorKinds)
        {
            row.Children.Add(CreateConnectorButton(kind, name));
        }

        row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
        };
        InsertOptionsHost.Children.Add(row);
        row.Children.Add(CreateTextToolButton());
    }

    private ToggleButton CreateConnectorButton(ConnectorKind kind, string name)
    {
        var button = new ToggleButton
        {
            Style = (Style)FindResource("ToolbarIconButton"),
            Content = new System.Windows.Shapes.Path
            {
                Width = 20,
                Height = 20,
                Stretch = Stretch.Uniform,
                Fill = (Brush)FindResource("ToolbarIconBrush"),
                Data = (Geometry)FindResource(PropertyBar.ConnectorGeometryKey(kind)),
            },
            ToolTip = name,
            IsChecked = _activeTool == BoardTool.Connector && _connectorKind == kind,
        };
        button.Click += (_, _) => ChooseConnectorTool(kind);
        return button;
    }

    private ToggleButton CreateTextToolButton()
    {
        var button = new ToggleButton
        {
            Style = (Style)FindResource("ToolbarIconButton"),
            Content = new System.Windows.Shapes.Path
            {
                Width = 20,
                Height = 20,
                Stretch = Stretch.Uniform,
                Fill = (Brush)FindResource("ToolbarIconBrush"),
                Data = (Geometry)FindResource("TextGeometry"),
            },
            ToolTip = "Text",
            IsChecked = _activeTool == BoardTool.Text,
        };
        button.Click += (_, _) =>
        {
            SetInsertOptionsOpen(false);
            ChooseTool(BoardTool.Text);
        };
        return button;
    }

    private ToggleButton CreateInsertButton(ShapeKind kind)
    {
        var button = new ToggleButton
        {
            Style = (Style)FindResource("ToolbarIconButton"),
            Content = new System.Windows.Shapes.Path
            {
                Width = 20,
                Height = 20,
                Stretch = Stretch.Uniform,
                Fill = (Brush)FindResource("ToolbarIconBrush"),
                Data = (Geometry)FindResource(kind.ToString() + "ShapeGeometry"),
            },
            ToolTip = ShapeName(kind),
            IsChecked = _activeTool == BoardTool.Shape && _shapeKind == kind,
        };
        button.Click += (_, _) => ChooseShapeTool(kind);
        return button;
    }

    private void RebuildSelectOptions()
    {
        if (SelectOptionsHost is null)
        {
            return;
        }

        SelectOptionsHost.Children.Clear();
        SelectOptionsHost.Children.Add(CreateAreaToolButton(
            AreaSelectionTool.Rectangle,
            "ImageSelectGeometry",
            "Drag on empty canvas to draw a rectangle"));
        SelectOptionsHost.Children.Add(CreateAreaToolButton(
            AreaSelectionTool.Lasso,
            "LassoGeometry",
            "Drag on empty canvas to draw a lasso"));
    }

    private ToggleButton CreateAreaToolButton(
        AreaSelectionTool tool,
        string geometryKey,
        string tooltip)
    {
        var button = new ToggleButton
        {
            Style = (Style)FindResource("ToolbarIconButton"),
            Content = new System.Windows.Shapes.Path
            {
                Width = 20,
                Height = 20,
                Stretch = Stretch.Uniform,
                Fill = (Brush)FindResource("ToolbarIconBrush"),
                Data = (Geometry)FindResource(geometryKey),
            },
            ToolTip = tooltip,
            IsChecked = _settings.AreaSelectionTool == tool,
        };
        button.Click += (_, _) => ChooseAreaSelectionTool(tool);
        return button;
    }

    /// <summary>
    /// The chevron's choice is the Select button's own, so whichever of the two
    /// is used the other shows what was chosen.
    /// </summary>
    private void ChooseAreaSelectionTool(AreaSelectionTool tool)
    {
        ApplyAreaSelectionTool(tool);
        SetActiveTool(BoardTool.Select);
    }

    private static string ShapeName(ShapeKind kind) => kind switch
    {
        ShapeKind.RoundedRectangle => "Rounded rectangle",
        ShapeKind.BlockArrow => "Block arrow",
        _ => kind.ToString(),
    };

    // The Insert palette. Everything about it lives here: the Insert row keeps
    // its own buttons, and the palette borrows them through the list above.
    private bool _isInsertPaletteHidden;

    private bool _insertPaletteDragging;

    private Point _insertPaletteGrab;

    /// <summary>
    /// The palette follows one setting, which the pin on the Insert row and the
    /// Preferences row both write, so neither can disagree with what is on the
    /// board.
    /// </summary>
    private void ApplyInsertPalette()
    {
        var shown = _settings.InsertPaletteShown;
        SessionBar.SetInsertPaletteChecked(shown);
        InsertPalette.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        if (!shown)
        {
            EndInsertPaletteDrag();
            return;
        }

        RebuildInsertPalette();
        ApplyInsertPaletteChrome();
        PositionInsertPalette();

        // A palette that has never been moved is placed from the toolbar's own
        // rectangle, and a preference that moved the toolbar has not been laid
        // out yet when this runs.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(PositionInsertPalette));
    }

    private void ToggleInsertPalette()
    {
        _settings.InsertPaletteShown = !_settings.InsertPaletteShown;

        // Right-clicking the palette away and then asking for it again should
        // bring back the palette rather than an invisible panel.
        _isInsertPaletteHidden = false;
        ApplyInsertPalette();
        PersistSettings();
    }

    /// <summary>
    /// The eight shapes, then the three connectors and Text: the Insert row in
    /// two short rows, from the one list the Insert flyout is built from. The
    /// buttons carry the active tool, so they are made again when it changes.
    /// </summary>
    private void RebuildInsertPalette()
    {
        if (InsertPaletteHost is null)
        {
            return;
        }

        InsertPaletteHost.Children.Clear();
        var shapes = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (ShapeKind kind in InsertShapes)
        {
            shapes.Children.Add(CreateInsertButton(kind));
        }

        InsertPaletteHost.Children.Add(shapes);
        var rest = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 2, 0, 0),
        };
        foreach ((ConnectorKind kind, string name) in PropertyBar.ConnectorKinds)
        {
            rest.Children.Add(CreateConnectorButton(kind, name));
        }

        rest.Children.Add(CreateTextToolButton());
        InsertPaletteHost.Children.Add(rest);
    }

    // Right-click hides and shows this palette as it does the tool palette, and
    // the border stays where it was so there is something left to right-click.
    private void ApplyInsertPaletteChrome()
    {
        if (_isInsertPaletteHidden)
        {
            InsertPaletteContents.Visibility = Visibility.Hidden;
            InsertPalette.Background = Brushes.Transparent;
            InsertPalette.BorderBrush = Brushes.Transparent;
            InsertPalette.Effect = null;
            return;
        }

        InsertPaletteContents.Visibility = Visibility.Visible;
        InsertPalette.Background = (Brush)FindResource("ToolbarBackgroundBrush");
        InsertPalette.BorderBrush = (Brush)FindResource("ToolbarBorderBrush");
        InsertPalette.Effect = new DropShadowEffect
        {
            BlurRadius = 18,
            ShadowDepth = 1,
            Direction = 270,
            Opacity = 0.16,
            Color = Colors.Black,
        };
    }

    private void PositionInsertPalette()
    {
        if (!_settings.InsertPaletteShown ||
            InsertPalette.ActualWidth <= 0 ||
            RootGrid.ActualWidth <= 0)
        {
            return;
        }

        var position = _settings.InsertPaletteX is { } x && _settings.InsertPaletteY is { } y
            ? InsertPalettePlacement.FromFraction(
                new PointD(x, y),
                RootGrid.ActualWidth,
                RootGrid.ActualHeight)
            : InsertPalettePlacement.Default(
                _settings.ToolbarPlacement,
                RootGrid.ActualWidth,
                RootGrid.ActualHeight,
                ToolPaletteBounds(),
                InsertPalette.ActualWidth,
                InsertPalette.ActualHeight);
        MoveInsertPaletteTo(position);
    }

    private void MoveInsertPaletteTo(PointD position)
    {
        var clamped = InsertPalettePlacement.Clamp(
            position,
            RootGrid.ActualWidth,
            RootGrid.ActualHeight,
            InsertPalette.ActualWidth,
            InsertPalette.ActualHeight);
        InsertPalette.Margin = new Thickness(clamped.X, clamped.Y, 0, 0);
    }

    /// <summary>
    /// Where the tool palette is in the window. It is asked of the layout rather
    /// than worked out from the placement, so the default keeps clear of the
    /// toolbar whatever the layout preference has made of it.
    /// </summary>
    private RectD ToolPaletteBounds()
    {
        if (ToolPalette.ActualWidth <= 0)
        {
            return RectD.Empty;
        }

        var origin = ToolPalette.TransformToAncestor(RootGrid).Transform(new Point(0, 0));
        return new RectD(origin.X, origin.Y, ToolPalette.ActualWidth, ToolPalette.ActualHeight);
    }

    private void InsertPalette_SizeChanged(object sender, SizeChangedEventArgs e) =>
        PositionInsertPalette();

    private void InsertPaletteCloseButton_Click(object sender, RoutedEventArgs e) =>
        ToggleInsertPalette();

    private void InsertPalette_PreviewMouseRightButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.StylusDevice is not null)
        {
            return;
        }

        _isInsertPaletteHidden = !_isInsertPaletteHidden;
        ApplyInsertPaletteChrome();
        e.Handled = true;
    }

    /// <summary>
    /// The pen and the finger reach this palette the way they reach the tool
    /// palette: the board holds the capture, so the press is turned into the
    /// press it meant rather than left waiting for a promotion that never comes.
    /// </summary>
    private void InsertPalette_PreviewStylusDown(object sender, StylusDownEventArgs e)
    {
        SessionBar.CollapseIfTransient();
        if (InsertPaletteGrip.InputHitTest(e.GetPosition(InsertPaletteGrip)) is not null)
        {
            ReleaseBoardPointerCapture(e.StylusDevice);
            LaserTrail.Lift();
            CancelFingerTool();
            BeginInsertPaletteDrag(e.GetPosition(RootGrid));
            e.StylusDevice.Capture(InsertPaletteGrip);
            e.Handled = true;
            return;
        }

        if (FindPaletteButton(e.OriginalSource as DependencyObject) is not { } button)
        {
            return;
        }

        ReleaseBoardPointerCapture(e.StylusDevice);
        LaserTrail.Lift();
        CancelFingerTool();
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        e.Handled = true;
    }

    private static ButtonBase? FindPaletteButton(DependencyObject? node)
    {
        while (node is not null)
        {
            if (node is ButtonBase button)
            {
                return button;
            }

            node = node is Visual visual ? VisualTreeHelper.GetParent(visual) : null;
        }

        return null;
    }

    private void InsertPaletteGrip_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.StylusDevice is not null)
        {
            return;
        }

        BeginInsertPaletteDrag(e.GetPosition(RootGrid));
        InsertPaletteGrip.CaptureMouse();
        e.Handled = true;
    }

    private void InsertPaletteGrip_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.StylusDevice is not null || !_insertPaletteDragging)
        {
            return;
        }

        MoveInsertPaletteUnder(e.GetPosition(RootGrid));
        e.Handled = true;
    }

    private void InsertPaletteGrip_PreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.StylusDevice is not null || !_insertPaletteDragging)
        {
            return;
        }

        InsertPaletteGrip.ReleaseMouseCapture();
        EndInsertPaletteDrag();
        e.Handled = true;
    }

    private void InsertPaletteGrip_PreviewStylusMove(object sender, StylusEventArgs e)
    {
        if (!_insertPaletteDragging)
        {
            return;
        }

        MoveInsertPaletteUnder(e.GetPosition(RootGrid));
        e.Handled = true;
    }

    private void InsertPaletteGrip_PreviewStylusUp(object sender, StylusEventArgs e)
    {
        if (!_insertPaletteDragging)
        {
            return;
        }

        e.StylusDevice.Capture(null);
        EndInsertPaletteDrag();
        e.Handled = true;
    }

    private void BeginInsertPaletteDrag(Point windowPoint)
    {
        _insertPaletteDragging = true;
        _insertPaletteGrab = new Point(
            windowPoint.X - InsertPalette.Margin.Left,
            windowPoint.Y - InsertPalette.Margin.Top);
    }

    private void MoveInsertPaletteUnder(Point windowPoint) =>
        MoveInsertPaletteTo(new PointD(
            windowPoint.X - _insertPaletteGrab.X,
            windowPoint.Y - _insertPaletteGrab.Y));

    /// <summary>
    /// The place is kept as a fraction of the window, so another window size or
    /// another monitor puts the palette back roughly where it was left.
    /// </summary>
    private void EndInsertPaletteDrag()
    {
        if (!_insertPaletteDragging)
        {
            return;
        }

        _insertPaletteDragging = false;
        var fraction = InsertPalettePlacement.ToFraction(
            new PointD(InsertPalette.Margin.Left, InsertPalette.Margin.Top),
            RootGrid.ActualWidth,
            RootGrid.ActualHeight);
        _settings.InsertPaletteX = fraction.X;
        _settings.InsertPaletteY = fraction.Y;
        PersistSettings();
    }

    private void ApplyPreferences()
    {
        ApplyToolbarPlacement();
        ApplyCalligraphyAccess();
        ApplyLaserSettings();
        ApplyPointerModes();
        UpdateSelectButtonGlyph();
        ApplyGrid();
        ApplyInsertOnToolbar();
        ApplyInsertPalette();
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

    private void ApplyGrid()
    {
        SceneSurface.GridStyle = _settings.Grid;
        SessionBar.SetGridChecked(_settings.Grid != GridStyle.Off);
        SceneSurface.InvalidateVisual();
    }

    /// <summary>
    /// The View row's toggle is between Off and whatever style was last chosen,
    /// so it never asks which of the two grids someone meant.
    /// </summary>
    private void ToggleGrid()
    {
        _settings.Grid = _settings.Grid == GridStyle.Off
            ? _settings.LastGridStyle
            : GridStyle.Off;
        if (_settings.Grid != GridStyle.Off)
        {
            _settings.LastGridStyle = _settings.Grid;
        }

        ApplyGrid();
        PersistSettings();
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
            case SessionCommand.Export:
                ShowExportDialog();
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
            case SessionCommand.ToggleGrid:
                ToggleGrid();
                break;
            case SessionCommand.BringToFront:
                BringSelectedContainerToFront();
                break;
            case SessionCommand.BringForward:
                BringSelectionForward();
                break;
            case SessionCommand.SendBackward:
                SendSelectionBackward();
                break;
            case SessionCommand.SendToBack:
                SendSelectedContainerToBack();
                break;
            case SessionCommand.AddLiveView:
                AddLiveViewMenuItem_Click(this, new RoutedEventArgs());
                break;
            case SessionCommand.AddFrame:
                AddFrame();
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
            case SessionCommand.InsertText:
                ChooseTool(BoardTool.Text);
                break;
            case SessionCommand.ToggleInsertPalette:
                ToggleInsertPalette();
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

    /// <summary>
    /// A frame the size of what is on screen, inset so that its edge is under
    /// the pen rather than under the window's own edge. It is selected on
    /// arrival, so the next gesture moves or resizes it.
    /// </summary>
    private void AddFrame()
    {
        CommitTextEdit();
        var visible = _camera.VisibleWorldBounds;
        const double inset = 0.08;
        var bounds = new RectD(
            visible.Left + (visible.Width * inset),
            visible.Top + (visible.Height * inset),
            visible.Width * (1 - (2 * inset)),
            visible.Height * (1 - (2 * inset)));
        var frame = new FrameBoardObject(
            Guid.NewGuid(),
            _document.NextZIndex,
            bounds,
            $"Slide {_document.Frames.Count() + 1}");
        _history.Execute(new AddObjectCommand(frame), _document);
        SelectOnly(frame.Id);
        SetActiveTool(BoardTool.Select);
        SceneSurface.InvalidateVisual();
        UpdateZOrderCommands();
        UpdateLiveViewActionOverlay();
    }

    private void RenameFrame(FrameBoardObject frame)
    {
        var dialog = new FrameTitleWindow(frame.Title);
        ShowOwnedDialog(dialog);
        if (dialog.Result is not { } title || title == frame.Title)
        {
            return;
        }

        _history.Execute(new ReplaceObjectCommand(frame, frame with { Title = title }), _document);
        SceneSurface.InvalidateVisual();
    }

    private void ShowExportDialog()
    {
        CommitTextEdit();
        if (_document.ContentBounds is null)
        {
            MessageBox.Show(
                this,
                "There is nothing on the board to export.",
                "Export",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        RefreshLiveViewSnapshots();
        ShowOwnedDialog(new ExportWindow(
            _document,
            _settings.Export,
            GetLiveViewImageSource,
            ResolveExportTitle,
            _currentBoardPath,
            PersistSettings));
    }

    // The same title the container shows on screen: a DAX or SQL container is
    // named after the object it defines when the language service finds one.
    private static string? ResolveExportTitle(BoardObject item) => item switch
    {
        TextBoardObject text => TextLanguageRegistry
            .Resolve(text.LanguageId)
            .Analyze(text.Text, text.Title)
            .Title,
        _ => null,
    };

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
        MarkSaved();
    }

    /// <summary>
    /// Whether the board differs from what the file on disk holds - or, for a board that
    /// has never been saved, from the empty board it started as.
    /// </summary>
    private bool IsModified => !_history.IsAtSavePoint || _dirtyOutsideHistory;

    /// <summary>
    /// Records a change the command history will not see. See <see cref="_dirtyOutsideHistory"/>.
    /// </summary>
    private void MarkDirtyOutsideHistory()
    {
        if (_dirtyOutsideHistory)
        {
            return;
        }

        _dirtyOutsideHistory = true;
        UpdateWindowTitle();
    }

    /// <summary>
    /// Records that the board as it stands is what <see cref="_currentBoardPath"/> holds.
    /// </summary>
    private void MarkSaved()
    {
        _history.MarkSaved();
        _dirtyOutsideHistory = false;
        UpdateWindowTitle();
    }

    private bool ConfirmDiscardUnsaved(string message) =>
        !IsModified ||
        MessageBox.Show(
            this,
            message,
            "SQLBI Whiteboard",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;

    // The board's name and whether it has strayed from the file, in the one place every
    // window already shows what it is holding. Without it the question asked on the way out
    // arrives with nothing on screen to explain what prompted it.
    private void UpdateWindowTitle()
    {
        var name = _currentBoardPath is null
            ? "Untitled board"
            : Path.GetFileName(_currentBoardPath);
        var marker = IsModified ? " *" : string.Empty;
        Title = $"{name}{marker} - SQLBI Whiteboard{AppChannel.WindowTitleSuffix}";
    }

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
            // Explicit Prompt Assistant metadata takes precedence over an accompanying
            // picture or code-like text. Untagged clipboard data keeps its usual priority.
            if (ClipboardPrompt.TryGetText(Clipboard.GetDataObject(), out string prompt))
            {
                AddText(prompt, languageId: TextLanguageIds.Prompt);
                return;
            }

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
        SelectOnly(image.Id);
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
        double pixelsPerDip = VisualTreeHelper.GetDpi(SceneSurface).PixelsPerDip;
        double width = Math.Min(
            TextContainerVisual.DefaultWidth(pixelsPerDip),
            Math.Max(320, visibleWidth * 0.7));
        string resolvedLanguageId = TextLanguageIds.Normalize(languageId);
        double height = TextContainerVisual.MeasureDesiredHeight(
            text,
            width,
            1,
            pixelsPerDip,
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
        SelectOnly(textObject.Id);
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
            SelectOnly(liveView.Id);
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

    private LiveViewBoardObject? GetSelectedLiveView() => SingleSelected<LiveViewBoardObject>();

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

    private TextBoardObject? GetSelectedText() => SingleSelected<TextBoardObject>();

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

    /// <summary>
    /// The property bar, above the selection's rectangle and flipped below it
    /// when there is no room. It stays away during a gesture and a text edit:
    /// both are about where something is, and neither wants a bar moving under
    /// the hand.
    /// </summary>
    private void UpdateSelectionPropertyBar()
    {
        if (SelectionPropertyBar is null)
        {
            return;
        }

        // The overflow offers the same four depth commands the View row does, so
        // they are answered here too, whether or not the bar ends up shown.
        UpdateZOrderCommands();

        if (_textEditBefore is not null ||
            _gestureBefore.Length > 0 ||
            _areaActive ||
            SelectionBounds() is not RectD bounds ||
            !SelectionPropertyBar.Update(SelectedObjects()))
        {
            HideSelectionPropertyBar();
            return;
        }

        SelectionPropertyBar.Visibility = Visibility.Visible;
        SelectionPropertyBar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Size size = SelectionPropertyBar.DesiredSize;
        PointD topLeft = _camera.WorldToScreen(new PointD(bounds.Left, bounds.Top));
        PointD bottomRight = _camera.WorldToScreen(new PointD(bounds.Right, bounds.Bottom));
        const double gap = 8;
        double left = Math.Max(
            0,
            Math.Min(
                ((topLeft.X + bottomRight.X) / 2) - (size.Width / 2),
                Math.Max(0, TextEditorLayer.ActualWidth - size.Width)));
        double top = topLeft.Y - size.Height - gap;
        if (top < 0)
        {
            double below = bottomRight.Y + gap;
            top = below + size.Height <= TextEditorLayer.ActualHeight ? below : 0;
        }

        Canvas.SetLeft(SelectionPropertyBar, left);
        Canvas.SetTop(SelectionPropertyBar, top);
    }

    private void HideSelectionPropertyBar()
    {
        if (SelectionPropertyBar is not null)
        {
            SelectionPropertyBar.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// A color for every selected stroke and shape, as one step. A stroke's kind
    /// is untouched, so recoloring a highlighter leaves it a highlighter with its
    /// own transparency rather than turning it into a pen.
    /// </summary>
    private void ApplySelectionColor(uint argb)
    {
        RestyleSelection(
            style => style with { Argb = argb },
            shape => shape with { OutlineArgb = argb },
            connector => connector with { Argb = argb });
        _settings.Shape.OutlineArgb = argb;
        _settings.Connector.Argb = argb;
        _settings.Label.Argb = argb;
        PersistSettings();

        // A label goes through its own path, which is also the one that changes
        // the box being typed into while a label is open.
        RestyleSelectedLabels(label => label with { Argb = argb });
    }

    private void ApplySelectionThickness(double thickness)
    {
        RestyleSelection(
            style => style with { Thickness = thickness },
            shape => shape with { Thickness = thickness },
            // Rebuilt rather than copied: the arrowhead is a multiple of the
            // thickness, so a thicker line reaches further than the one it
            // replaces.
            connector => connector.WithThickness(thickness));
        _settings.Shape.Thickness = thickness;
        _settings.Connector.Thickness = thickness;
        PersistSettings();
    }

    private void ApplySelectionFill(uint? fill)
    {
        RestyleSelection(null, shape => shape with { FillArgb = fill }, null);
        _settings.Shape.FillArgb = fill;
        PersistSettings();
    }

    /// <summary>
    /// One property applied to everything selected that has it, as a single
    /// step, and remembered as what the next shape is drawn with. A change that
    /// leaves an object as it was is left out, so a mixed selection records only
    /// what it actually altered.
    /// </summary>
    private void RestyleSelection(
        Func<PenStyle, PenStyle>? restyleStroke,
        Func<ShapeBoardObject, ShapeBoardObject>? restyleShape,
        Func<ConnectorBoardObject, ConnectorBoardObject>? restyleConnector)
    {
        // A shape being typed in is part of that edit, not a step of its own, so
        // its outline, thickness, and fill change with the words rather than
        // behind them.
        if (_shapeEditCurrent is { } editing && restyleShape is not null)
        {
            ApplyShapeDuringEdit(restyleShape(editing));
            LabelEditor.Focus();
            return;
        }

        var before = new List<BoardObject>();
        var after = new List<BoardObject>();
        foreach (BoardObject item in SelectedObjects())
        {
            BoardObject? replacement = item switch
            {
                // Rebuilt rather than copied with a new style: the bounds carry
                // half the nib, so a thicker stroke covers more board than the
                // one it replaces.
                InkStrokeObject stroke when restyleStroke is not null => InkStrokeObject.Create(
                    stroke.Points,
                    restyleStroke(stroke.Style),
                    stroke.ZIndex,
                    stroke.Id,
                    stroke.ContainerId),
                ShapeBoardObject shape when restyleShape is not null => restyleShape(shape),
                ConnectorBoardObject connector when restyleConnector is not null => restyleConnector(connector),
                _ => null,
            };
            if (replacement is null || replacement == item)
            {
                continue;
            }

            before.Add(item);
            after.Add(replacement);
        }

        if (before.Count == 0)
        {
            return;
        }

        _history.Execute(
            new ReplaceObjectsCommand(before.ToArray(), after.ToArray()),
            _document);
        SceneSurface.InvalidateVisual();
        UpdateSelectionPropertyBar();
        InkSurface.Focus();
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
            MarkSaved();
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

        // Every way into a board arrives here - the Open dialog, a drop, and a double-click
        // in Explorer by way of the command line - so this is the one place that has to ask
        // whether something newer than the file is waiting for it.
        if (await TryOpenRecoveredAsync(filePath))
        {
            return;
        }

        await LoadBoardAsync(filePath);
    }

    /// <summary>
    /// Offers what a copy that closed unexpectedly was holding for this exact file. True
    /// when the open has been dealt with and the file itself should not be loaded.
    /// </summary>
    private async Task<bool> TryOpenRecoveredAsync(string filePath)
    {
        AbandonedSession? match = SessionStore.FindAbandoned().FirstOrDefault(item =>
            item.State.Modified &&
            !item.State.ExitedCleanly &&
            item.BoardPath is not null &&
            item.State.BoardPath is not null &&
            SessionStore.IsSameFile(item.State.BoardPath, filePath));

        if (match is null)
        {
            return false;
        }

        var when = match.State.WrittenUtc.ToLocalTime().ToString("f");
        MessageBoxResult answer = MessageBox.Show(
            this,
            $"{Path.GetFileName(filePath)} has unsaved changes from {when}, left behind when " +
            "SQLBI Whiteboard closed unexpectedly.\n\n" +
            "Open those changes instead of the saved file? Choosing No opens the saved file " +
            "and discards them.",
            "SQLBI Whiteboard",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (answer == MessageBoxResult.Cancel)
        {
            // Neither the changes nor the file: the slot keeps its place and the board on
            // screen is left alone.
            return true;
        }

        if (answer != MessageBoxResult.Yes)
        {
            // The saved file was asked for by name, so the changes it was offered against
            // have been answered and must not be offered again.
            SessionStore.Forget(match.SlotId);
            return false;
        }

        if (!await AdoptSessionAsync(match))
        {
            return false;
        }

        await WriteSessionAsync(keepBoard: true, exitedCleanly: false);
        SessionStore.Forget(match.SlotId);
        return true;
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
            MarkSaved();
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
        SelectOnly(null);
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

            if (_selectedObjectIds.Count > 1)
            {
                CopySelectionAsImage();
                return;
            }

            if (SingleSelected<BoardObject>() is not { } selected)
            {
                return;
            }

            if (selected is TextBoardObject text)
            {
                Clipboard.SetText(text.Text, TextDataFormat.UnicodeText);
                return;
            }

            if (selected is FreeTextBoardObject label)
            {
                Clipboard.SetText(label.Text, TextDataFormat.UnicodeText);
                return;
            }

            // A shape copies what is written in it, and a shape with nothing
            // written in it copies nothing rather than emptying the clipboard.
            if (selected is ShapeBoardObject shape)
            {
                if (shape.Text.Length > 0)
                {
                    Clipboard.SetText(shape.Text, TextDataFormat.UnicodeText);
                }

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

    /// <summary>
    /// Several things at once go out as a picture of themselves, drawn by the
    /// same rasterizer Export uses so that what lands in a slide is what the
    /// board shows. A lone text container or picture still copies as itself.
    /// </summary>
    private void CopySelectionAsImage()
    {
        BoardObject[] selected = SelectedObjects();
        if (selected.Length == 0)
        {
            return;
        }

        HashSet<Guid> ids = _document.GetDeletionGroup(_selectedObjectIds)
            .Select(item => item.Id)
            .ToHashSet();
        RectD bounds = UnionBounds(selected);

        // Drawn at the zoom it is being looked at, within reason, so a copy of
        // something small is not a poster and a copy of a wall is not a file
        // nothing will paste.
        double scale = Math.Clamp(_camera.Zoom, 0.5, 2);
        var width = (int)Math.Clamp(Math.Round(bounds.Width * scale), 1, 4096);
        var height = (int)Math.Clamp(Math.Round(bounds.Height * scale), 1, 4096);
        Clipboard.SetImage(BoardRasterizer.Render(
            _document,
            bounds,
            width,
            height,
            GetLiveViewImageSource,
            paddingFraction: 0,
            objectFilter: item => ids.Contains(item.Id)));
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
                    TextContainerVisual.DefaultWidth(dpi),
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

        var rects = ImportLayout.Place(
            sizes,
            originTopLeft,
            _settings.Import.HorizontalSpacing,
            _settings.Import.VerticalSpacing);
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
            // The recipe built a board no file holds, and clearing the history has just
            // said the opposite. Nothing else reaches the save point, so say it here.
            command.Execute(_document);
            _history.Clear();
            MarkDirtyOutsideHistory();
        }

        SceneSurface.InvalidateAssets();
        if (objects.Count > 0)
        {
            SelectOnly(objects[^1].Id);
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

        // Not while something is being typed: a row that stays open - Edit,
        // Insert - would otherwise read the letters of a label or a snippet as
        // its own access keys, and a word with a T in it would pick a tool.
        if (SessionBar.IsCommandRowOpen &&
            !controlDown &&
            !altDown &&
            _labelEditBefore is null &&
            _shapeEditBefore is null &&
            _textEditBefore is null)
        {
            var isMove = mnemonicKey is Key.Left or Key.Right or Key.Home or Key.End;
            if ((isMove || !e.IsRepeat) && SessionBar.TryHandleCommandKey(mnemonicKey))
            {
                e.Handled = true;
                return;
            }
        }
        if (_labelEditBefore is not null)
        {
            if (controlDown && e.Key == Key.Enter)
            {
                CommitLabelEdit();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CancelLabelEdit();
                e.Handled = true;
            }
            else if (controlDown && e.Key == Key.S)
            {
                CommitLabelEdit();
                _ = SaveBoardAsync();
                e.Handled = true;
            }

            return;
        }

        if (_shapeEditBefore is not null)
        {
            if (controlDown && e.Key == Key.Enter)
            {
                CommitShapeTextEdit();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CancelShapeTextEdit();
                e.Handled = true;
            }
            else if (controlDown && e.Key == Key.S)
            {
                CommitShapeTextEdit();
                _ = SaveBoardAsync();
                e.Handled = true;
            }

            return;
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
            else if (controlDown && e.Key == Key.E)
            {
                CommitTextEdit();
                ShowExportDialog();
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
        else if (e.Key == Key.F2 && SingleSelected<TextBoardObject>() is { } textObject)
        {
            BeginTextEdit(textObject);
            e.Handled = true;
        }
        else if (e.Key == Key.F2 && SingleSelected<FreeTextBoardObject>() is { } selectedLabel)
        {
            BeginLabelEdit(selectedLabel, isNew: false);
            e.Handled = true;
        }
        else if (e.Key == Key.F2 && SingleSelected<ShapeBoardObject>() is { } selectedShape)
        {
            BeginShapeTextEdit(selectedShape, replaceText: false);
            e.Handled = true;
        }
        else if (e.Key == Key.F2 && SingleSelected<FrameBoardObject>() is { } selectedFrame)
        {
            RenameFrame(selectedFrame);
            e.Handled = true;
        }
        else if (shiftDown && e.Key == Key.F12)
        {
            _ = SaveBoardAsync(saveAs: true);
            e.Handled = true;
        }
        else if (controlDown && e.Key == Key.A)
        {
            SelectAll(strokesOnly: shiftDown);
            e.Handled = true;
        }
        else if (controlDown && e.Key == Key.C)
        {
            CopySelectionToClipboard();
            e.Handled = true;
        }
        else if (controlDown && e.Key == Key.D)
        {
            DuplicateSelection();
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
        else if (controlDown && e.Key == Key.E)
        {
            ShowExportDialog();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && _selectedObjectIds.Count > 0)
        {
            DeleteSelection();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _activeTool is BoardTool.Shape or BoardTool.Connector)
        {
            SetActiveTool(BoardTool.Select);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _selectedObjectIds.Count > 0)
        {
            ClearSelection();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _activeTool == BoardTool.Text)
        {
            // The tool stays after a label is typed, so Escape is what puts it
            // down again.
            SetActiveTool(BoardTool.Select);
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
        else
        {
            // Last, so that every shortcut above keeps the key it has: what is
            // left of the keyboard writes into the one selected shape.
            StartShapeTextTyping(e);
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

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        CommitTextEdit();
        if (!_closeConfirmed)
        {
            // Closing runs synchronously, while asking about unsaved changes and writing
            // the session do not. So the first pass always calls the close off, finishes
            // the work, and closes again - and nothing below this is reached until it has.
            e.Cancel = true;
            bool proceed;
            try
            {
                proceed = await PrepareToCloseAsync();
            }
            catch (Exception exception)
            {
                // Whatever went wrong, it must not be what leaves somebody unable to close
                // the window. The session is the thing being given up here, not the board.
                Debug.WriteLine($"[Session] Could not prepare to close: {exception.Message}");
                proceed = true;
            }

            if (proceed)
            {
                _closeConfirmed = true;
                Close();
            }

            return;
        }

        _autosaveTimer.Stop();
        // Unregister Vortice's retained Window.Closed callbacks and unload the
        // D3D surfaces before the Closed event begins. This guarantees one
        // native teardown path for both live and already-paused presenters.
        DisposeAllLiveViewPresenters();
        _session?.Dispose();
    }

    /// <summary>
    /// Asks about unsaved changes where there is something to ask about, then records what
    /// the next start should come back to. False calls the close off.
    /// </summary>
    private async Task<bool> PrepareToCloseAsync()
    {
        var keepBoard = IsModified;
        if (_currentBoardPath is not null && IsModified)
        {
            var dialog = new UnsavedChangesWindow(
                Path.GetFileName(_currentBoardPath),
                _document.Objects.OfType<LiveViewBoardObject>().Any())
            {
                Owner = this,
            };
            dialog.ShowDialog();
            switch (dialog.Result)
            {
                case UnsavedChangesAnswer.Cancel:
                    return false;

                case UnsavedChangesAnswer.Save:
                    await SaveBoardAsync();
                    if (IsModified)
                    {
                        // Nothing was written - the save failed, or the file dialog was
                        // dismissed - so closing now would lose what was being saved.
                        return false;
                    }

                    keepBoard = false;
                    break;

                case UnsavedChangesAnswer.Discard:
                    keepBoard = false;
                    break;
            }
        }

        if (keepBoard)
        {
            // Worth the one pass on the way out, so a restored LiveView container shows
            // the frame it was showing rather than the one it started with.
            RefreshLiveViewSnapshots();
        }

        await WriteSessionAsync(keepBoard, exitedCleanly: true);
        return true;
    }

    /// <summary>
    /// Copies the board into this copy's session slot, or clears the slot when there is
    /// nothing worth coming back to.
    /// </summary>
    private async Task WriteSessionAsync(bool keepBoard, bool exitedCleanly)
    {
        if (_session is null)
        {
            return;
        }

        try
        {
            if (!keepBoard && _currentBoardPath is null)
            {
                // An untitled board nobody has drawn on. Restoring it would be
                // indistinguishable from starting normally.
                _session.Clear();
                return;
            }

            var state = new SessionState
            {
                BoardPath = _currentBoardPath,
                Modified = keepBoard,
                ExitedCleanly = exitedCleanly,
                CameraCenterX = _camera.Center.X,
                CameraCenterY = _camera.Center.Y,
                CameraZoom = _camera.Zoom,
            };

            // Snapshotting here and writing on a worker keeps the zip off the pen's thread.
            BoardDocument? snapshot = keepBoard ? _document.Snapshot() : null;
            await Task.Run(() => _session.WriteAsync(snapshot, state));
        }
        catch (Exception exception)
        {
            // Never in the way of closing, and never a dialog: this is the safety net
            // rather than the save, and the file the person asked for is already written.
            Debug.WriteLine($"[Session] Could not write the session: {exception.Message}");
        }
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
