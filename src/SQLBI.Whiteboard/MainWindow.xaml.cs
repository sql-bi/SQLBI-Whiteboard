using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Microsoft.Win32;
using SQLBI.Whiteboard.Core.Commands;
using SQLBI.Whiteboard.Core.Geometry;
using SQLBI.Whiteboard.Core.Model;
using SQLBI.Whiteboard.Core.Persistence;
using SQLBI.Whiteboard.Core.Settings;
using SQLBI.Whiteboard.Core.Viewport;

namespace SQLBI.Whiteboard;

public partial class MainWindow : Window
{
    private static readonly HashSet<string> ImageFileExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".png",
            ".jpg",
            ".jpeg",
            ".bmp",
            ".gif",
        };

    private enum BoardTool
    {
        Pen,
        Highlighter,
        Calligraphy,
        Eraser,
        Select,
        Pan,
    }

    private enum PointerAction
    {
        None,
        Erase,
        Pan,
        Image,
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

    private BoardDocument _document = new();
    private string? _currentBoardPath;
    private BoardTool _activeTool = BoardTool.Pen;
    private BoardTool _lastDrawingTool = BoardTool.Pen;
    private BoardTool _toolBeforeSpace = BoardTool.Pen;
    private PenStyle _penStyle = InkPalettes.DefaultPen;
    private PointerAction _stylusAction;
    private PointerAction _mouseAction;
    private PointD _lastPanPoint;
    private bool _penInContact;
    private bool _spaceTemporaryPan;
    private Guid? _selectedObjectId;
    private ImageBoardObject? _imageGestureBefore;
    private ImageBoardObject? _imageGestureCurrent;
    private InkStrokeObject[] _imageGestureLinkedBefore = [];
    private InkStrokeObject[] _imageGestureLinkedCurrent = [];
    private PointD _imageGestureStartWorld;
    private bool _imageGestureIsResize;
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

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += MainWindow_SourceInitialized;
        _document.Changed += Document_Changed;
        _history.Changed += History_Changed;
        SceneSurface.Configure(_document, _camera);
        InkSurface.Cursor = Cursors.Arrow;
        _settings = AppSettingsStore.Load();
        LoadInkFromSettings();
        ApplyToolbarPlacement();
        ApplyCalligraphyAccess();
        ApplyDrawingAttributes();
        SetActiveTool(BoardTool.Pen);
        InkSurface.Focus();
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        MonitorStartupPlacement.PlaceMaximizedOnWacom(this);
    }

    private void BoardViewport_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        _camera.Resize(e.NewSize.Width, e.NewSize.Height);
        SceneSurface.InvalidateVisual();
    }

    private void Document_Changed(object? sender, EventArgs e)
    {
        SceneSurface.SelectedObjectId = _selectedObjectId;
        SceneSurface.InvalidateVisual();
    }

    private void History_Changed(object? sender, EventArgs e)
    {
        UndoMenuItem.IsEnabled = _history.CanUndo;
        RedoMenuItem.IsEnabled = _history.CanRedo;
    }

    private void InkSurface_StrokeCollected(object sender, InkCanvasStrokeCollectedEventArgs e)
    {
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
        if (IsTouchStylus(e))
        {
            InkSurface.RegisterTouchTablet(e.StylusDevice.TabletDevice.Id);
            BeginTouchNavigation(e);
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
            BeginImageGesture(screen);
            _stylusAction = PointerAction.Image;
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
            case PointerAction.Image:
                UpdateImageGesture(_camera.ScreenToWorld(screen));
                e.Handled = true;
                break;
        }

        if (EffectiveTool == BoardTool.Select)
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
            UsePenCursor();
            ShowPointerDot(e.GetPosition(RootGrid));
        }

    }

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
            case PointerAction.Image:
                UpdateImageGesture(_camera.ScreenToWorld(screen));
                CompleteImageGesture();
                e.Handled = true;
                break;
        }

        if (_stylusAction != PointerAction.None)
        {
            InkSurface.ReleaseStylusCapture();
        }

        _stylusAction = PointerAction.None;
        _penInContact = false;
        if (EffectiveTool == BoardTool.Select)
        {
            var hoverScreen = ToPointD(e.GetPosition(InkSurface));
            UpdateSelectHover(hoverScreen);
            InkSurface.Cursor = SelectCursorAt(hoverScreen);
            HidePointerDot();
        }
        else
        {
            UsePenCursor();
            ShowPointerDot(e.GetPosition(RootGrid));
        }
        Debug.WriteLine("[WpfInk] stylus-up reached WPF");
    }

    private void InkSurface_StylusEnter(object sender, StylusEventArgs e)
    {
        if (IsTouchStylus(e))
        {
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
            ShowPointerDot(e.GetPosition(RootGrid));
        }
    }

    private void InkSurface_StylusLeave(object sender, StylusEventArgs e)
    {
        if (!IsTouchStylus(e) && !_penInContact)
        {
            HidePointerDot();
        }
    }

    private void InkSurface_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.StylusDevice is not null)
        {
            return;
        }

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
        else if (e.ChangedButton == MouseButton.Left)
        {
            SetActiveTool(BoardTool.Select);
            BeginImageGesture(screen);
            _mouseAction = PointerAction.Image;
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
        if (EffectiveTool == BoardTool.Select && _mouseAction == PointerAction.None)
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
            case PointerAction.Image:
                UpdateImageGesture(_camera.ScreenToWorld(screen));
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
        else if (_mouseAction == PointerAction.Image)
        {
            CompleteImageGesture();
        }

        _mouseAction = PointerAction.None;
        if (hadMouseAction)
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
            case PointerAction.Image:
                UpdateImageGesture(_camera.ScreenToWorld(screen));
                CompleteImageGesture();
                break;
        }

        _mouseAction = PointerAction.None;
        if (Mouse.Captured == InkSurface)
        {
            Mouse.Capture(null);
        }

        SetActiveTool(_lastDrawingTool);
    }

    private void InkSurface_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.StylusDevice is not null)
        {
            return;
        }

        var screen = ToPointD(e.GetPosition(InkSurface));
        var factor = Math.Pow(1.0015, e.Delta);
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
    }

    private void FrameContentAt(PointD screenPoint)
    {
        var image = _document.HitTestTopImage(_camera.ScreenToWorld(screenPoint));
        if (image is not null)
        {
            _selectedObjectId = image.Id;
            SceneSurface.SelectedObjectId = image.Id;
            _camera.Frame(image.Bounds);
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

    private void BeginImageGesture(PointD screenPoint)
    {
        var worldPoint = _camera.ScreenToWorld(screenPoint);
        var selected = _document.HitTestTopImage(worldPoint);
        _imageGestureIsResize = false;

        if (_selectedObjectId is Guid existingId &&
            _document.Objects.FirstOrDefault(item => item.Id == existingId) is ImageBoardObject existing)
        {
            var handle = _camera.WorldToScreen(
                new PointD(existing.Bounds.Right, existing.Bounds.Bottom));
            if (Distance(ToPoint(handle), ToPoint(screenPoint)) <= 16)
            {
                selected = existing;
                _imageGestureIsResize = true;
            }
        }

        _selectedObjectId = selected?.Id;
        SceneSurface.SelectedObjectId = _selectedObjectId;
        if (selected is null)
        {
            ResetImageGesture();
            SceneSurface.InvalidateVisual();
            return;
        }

        _imageGestureBefore = selected;
        _imageGestureCurrent = selected;
        _imageGestureLinkedBefore = _document.LinkedStrokes(selected.Id).ToArray();
        _imageGestureLinkedCurrent = _imageGestureLinkedBefore;
        _imageGestureStartWorld = worldPoint;
        SceneSurface.InvalidateVisual();
    }

    private void UpdateImageGesture(PointD worldPoint)
    {
        if (_imageGestureBefore is null)
        {
            return;
        }

        var bounds = _imageGestureBefore.Bounds;
        if (_imageGestureIsResize)
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
            bounds = bounds.Translate(worldPoint - _imageGestureStartWorld);
        }

        _imageGestureCurrent = _imageGestureBefore with { Bounds = bounds };
        _imageGestureLinkedCurrent = _imageGestureLinkedBefore
            .Select(stroke => stroke.TransformWithContainer(_imageGestureBefore.Bounds, bounds))
            .ToArray();
        BoardObject[] replacements =
            [_imageGestureCurrent, .. _imageGestureLinkedCurrent];
        _document.ReplaceObjects(replacements);
    }

    private void CompleteImageGesture()
    {
        if (_imageGestureBefore is not null &&
            _imageGestureCurrent is not null &&
            _imageGestureBefore.Bounds != _imageGestureCurrent.Bounds)
        {
            BoardObject[] before =
                [_imageGestureBefore, .. _imageGestureLinkedBefore];
            BoardObject[] after =
                [_imageGestureCurrent, .. _imageGestureLinkedCurrent];
            _history.RecordExecuted(new ReplaceObjectsCommand(before, after));
        }

        ResetImageGesture();
    }

    private void ResetImageGesture()
    {
        _imageGestureBefore = null;
        _imageGestureCurrent = null;
        _imageGestureLinkedBefore = [];
        _imageGestureLinkedCurrent = [];
        _imageGestureIsResize = false;
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
        SetActiveTool(BoardTool.Select);
    }

    private void PanToolButton_Click(object sender, RoutedEventArgs e) =>
        SetActiveTool(BoardTool.Pan);

    private BoardTool EffectiveTool =>
        _spaceTemporaryPan ? BoardTool.Pan : _activeTool;

    private bool IsDualLayout =>
        _settings.CalligraphyAccess == CalligraphyAccess.DualPalette;

    private void DualPenButton_Click(object sender, RoutedEventArgs e) =>
        SetActiveTool(BoardTool.Pen);

    private void DualCalligraphyButton_Click(object sender, RoutedEventArgs e) =>
        SetActiveTool(BoardTool.Calligraphy);

    private void DualHighlighterButton_Click(object sender, RoutedEventArgs e) =>
        SetActiveTool(BoardTool.Highlighter);

    private void SetActiveTool(BoardTool tool)
    {
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

        var penFamilyActive = tool is BoardTool.Pen or BoardTool.Calligraphy;
        PenToolButton.IsChecked = penFamilyActive;
        HighlighterToolButton.IsChecked = tool == BoardTool.Highlighter;
        SelectToolButton.IsChecked = tool == BoardTool.Select;
        UpdateDualToolChecks();
        PenToolMenuItem.IsChecked = tool == BoardTool.Pen;
        HighlighterToolMenuItem.IsChecked = tool == BoardTool.Highlighter;
        CalligraphyToolMenuItem.IsChecked = tool == BoardTool.Calligraphy;
        InkSurface.EditingMode =
            tool is BoardTool.Pen or BoardTool.Highlighter or BoardTool.Calligraphy
            ? InkCanvasEditingMode.Ink
            : InkCanvasEditingMode.None;
        InkSurface.EditingModeInverted = InkCanvasEditingMode.None;
        UpdatePenButtonGlyph();
        ApplyDrawingAttributes();
        if (IsDualLayout)
        {
            RebuildDualPalette();
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

        InkSurface.DefaultDrawingAttributes =
            InkDrawingAttributes.Create(_penStyle, _camera.Zoom);
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
            RebuildDualPalette();
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

        CommitInkStyle(style);
        SetActiveTool(tool);
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

        CommitInkStyle(style);
        SetActiveTool(BoardTool.Highlighter);
        InkSurface.Focus();
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
            CommitInkStyle(_penStyle with { Argb = argb });
            InkSurface.Focus();
        }
    }

    private void SizeChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: double thickness })
        {
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
        if (_imageGestureBefore is not null)
        {
            return;
        }

        var image = _document.HitTestTopImage(_camera.ScreenToWorld(screen));
        var hoveredId = image?.Id;
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

        return _document.HitTestTopImage(_camera.ScreenToWorld(screen)) is null
            ? Cursors.Arrow
            : Cursors.SizeAll;
    }

    private bool IsOverResizeHandle(PointD screen)
    {
        if (_selectedObjectId is not Guid existingId ||
            _document.Objects.FirstOrDefault(item => item.Id == existingId) is not ImageBoardObject existing)
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

    private void PreferencesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PreferencesWindow(
            _settings.ToolbarPlacement,
            _settings.CalligraphyAccess,
            SetToolbarPlacement,
            SetCalligraphyAccess)
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

    private async void PasteButton_Click(object sender, RoutedEventArgs e) =>
        await PasteFromClipboardAsync();

    private async void OpenButton_Click(object sender, RoutedEventArgs e) =>
        await OpenBoardAsync();

    private async void SaveButton_Click(object sender, RoutedEventArgs e) =>
        await SaveBoardAsync();

    private void NewMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((_document.Objects.Count > 0 || _document.Assets.Count > 0) &&
            MessageBox.Show(
                this,
                "Create a new whiteboard? Any unsaved changes will be lost.",
                "New whiteboard",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        ReplaceDocument(new BoardDocument());
        _currentBoardPath = null;
        ResetBoardView();
    }

    private async void SaveAsMenuItem_Click(object sender, RoutedEventArgs e) =>
        await SaveBoardAsync(saveAs: true);

    private void CloseMenuItem_Click(object sender, RoutedEventArgs e) => Close();

    private void CopyMenuItem_Click(object sender, RoutedEventArgs e) =>
        CopySelectionToClipboard();

    private void UndoButton_Click(object sender, RoutedEventArgs e) =>
        _history.Undo(_document);

    private void RedoButton_Click(object sender, RoutedEventArgs e) =>
        _history.Redo(_document);

    private async Task ImportImageAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import image",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif",
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) != true)
        {
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
            if (!Clipboard.ContainsImage())
            {
                return Task.CompletedTask;
            }

            var bitmap = Clipboard.GetImage();
            if (bitmap is null)
            {
                return Task.CompletedTask;
            }

            AddImage(
                WpfImageCodec.EncodePng(bitmap),
                "clipboard-image.png",
                "image/png");
        }
        catch (Exception exception)
        {
            ShowError("Could not paste image", exception);
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
    }

    private async Task SaveBoardAsync(bool saveAs = false)
    {
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
            await using var stream = new FileStream(
                filePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                useAsync: true);
            await BoardArchive.SaveAsync(_document, stream);
            _currentBoardPath = filePath;
        }
        catch (Exception exception)
        {
            ShowError("Could not save board", exception);
        }
    }

    private async Task OpenBoardAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open board",
            Filter = "Whiteboard document|*.wboard",
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            await using var stream = new FileStream(
                dialog.FileName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                useAsync: true);
            var loaded = await BoardArchive.LoadAsync(stream);
            ReplaceDocument(loaded);
            _currentBoardPath = dialog.FileName;
            ResetBoardView();
        }
        catch (Exception exception)
        {
            ShowError("Could not open board", exception);
        }
    }

    private void ReplaceDocument(BoardDocument replacement)
    {
        _document.Changed -= Document_Changed;
        _document = replacement;
        _document.Changed += Document_Changed;
        SceneSurface.Configure(_document, _camera);
        Document_Changed(this, EventArgs.Empty);
    }

    private void ResetBoardView()
    {
        _camera.Reset();
        _selectedObjectId = null;
        SceneSurface.SelectedObjectId = null;
        InkSurface.Strokes.Clear();
        _history.Clear();
        ResetImageGesture();
        CameraChanged();
        InkSurface.Focus();
    }

    private void CopySelectionToClipboard()
    {
        if (_selectedObjectId is not Guid selectedId ||
            _document.Objects.FirstOrDefault(item => item.Id == selectedId) is not ImageBoardObject image ||
            !_document.Assets.TryGetValue(image.AssetId, out var asset))
        {
            return;
        }

        try
        {
            Clipboard.SetImage(WpfImageCodec.Decode(asset.Data));
        }
        catch (Exception exception)
        {
            ShowError("Could not copy image", exception);
        }
    }

    private void InkSurface_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private async void InkSurface_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return;
        }

        var dropCenter = _camera.ScreenToWorld(ToPointD(e.GetPosition(InkSurface)));
        var imported = 0;
        try
        {
            foreach (var path in paths.Where(path =>
                         ImageFileExtensions.Contains(Path.GetExtension(path))))
            {
                var bytes = await File.ReadAllBytesAsync(path);
                var offset = new PointD(imported * 24, imported * 24);
                AddImage(
                    bytes,
                    Path.GetFileName(path),
                    ContentTypeFor(path),
                    dropCenter + offset);
                imported++;
            }

        }
        catch (Exception exception)
        {
            ShowError("Could not drop image", exception);
        }

        e.Handled = true;
    }

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

        if (_isFullScreen && e.Key == Key.Escape)
        {
            ExitFullScreen();
            e.Handled = true;
            return;
        }

        var modifiers = Keyboard.Modifiers;
        var controlDown = modifiers.HasFlag(ModifierKeys.Control);
        var shiftDown = modifiers.HasFlag(ModifierKeys.Shift);
        if (shiftDown && e.Key == Key.F12)
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
        else if (_stylusAction == PointerAction.Image || _mouseAction == PointerAction.Image)
        {
            CompleteImageGesture();
        }

        _stylusAction = PointerAction.None;
        _mouseAction = PointerAction.None;
        _penInContact = false;
        ClearTouchNavigation();
        InkSurface.Cursor = Cursors.Arrow;
        HidePointerDot();
    }

    private void UsePenCursor()
    {
        InkSurface.Cursor = EffectiveTool == BoardTool.Select
            ? Cursors.Arrow
            : Cursors.None;
    }

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

    private void HidePointerDot() => PointerDot.Visibility = Visibility.Collapsed;

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
