using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using SQLBI.Whiteboard.Core.Commands;
using SQLBI.Whiteboard.Core.Geometry;
using SQLBI.Whiteboard.Core.Model;
using SQLBI.Whiteboard.Core.Persistence;
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
    private readonly Dictionary<PenKind, double> _thicknessByPenKind = new()
    {
        [PenKind.Pen] = 4,
        [PenKind.Highlighter] = 4,
        [PenKind.Calligraphy] = 4,
    };

    private BoardDocument _document = new();
    private string? _currentBoardPath;
    private BoardTool _activeTool = BoardTool.Pen;
    private BoardTool _lastDrawingTool = BoardTool.Pen;
    private BoardTool _toolBeforeSpace = BoardTool.Pen;
    private PenStyle _penStyle = new(0xFFDC2626, 4);
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
    private WindowState _windowStateBeforeFullScreen;
    private WindowStyle _windowStyleBeforeFullScreen;
    private ResizeMode _resizeModeBeforeFullScreen;
    private Rect _windowBoundsBeforeFullScreen;
    private bool _isUpdatingThicknessSlider;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += MainWindow_SourceInitialized;
        _document.Changed += Document_Changed;
        _history.Changed += History_Changed;
        SceneSurface.Configure(_document, _camera);
        InkSurface.Cursor = Cursors.Arrow;
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

        UsePenCursor();
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

        if (_penInContact)
        {
            HidePointerDot();
        }
        else
        {
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
        UsePenCursor();
        ShowPointerDot(e.GetPosition(RootGrid));
        Debug.WriteLine("[WpfInk] stylus-up reached WPF");
    }

    private void InkSurface_StylusEnter(object sender, StylusEventArgs e)
    {
        if (IsTouchStylus(e))
        {
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
        ToolPaletteContents.Visibility = _isToolPaletteHidden
            ? Visibility.Hidden
            : Visibility.Visible;
        ToolPalette.Background = _isToolPaletteHidden
            ? Brushes.Transparent
            : new SolidColorBrush(Color.FromRgb(249, 249, 249));
        ToolPalette.BorderBrush = _isToolPaletteHidden
            ? Brushes.Transparent
            : new SolidColorBrush(Color.FromArgb(0x24, 0, 0, 0));
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

        InkSurface.Cursor = Cursors.Arrow;
        HidePointerDot();

        var screen = ToPointD(e.GetPosition(InkSurface));
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
        SceneSurface.InvalidateVisual();
    }

    private void ClearSelection()
    {
        if (_selectedObjectId is null && SceneSurface.SelectedObjectId is null)
        {
            return;
        }

        _selectedObjectId = null;
        SceneSurface.SelectedObjectId = null;
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

    private void PenToolButton_Click(object sender, RoutedEventArgs e) =>
        SetActiveTool(BoardTool.Pen);

    private void HighlighterToolButton_Click(object sender, RoutedEventArgs e) =>
        SetActiveTool(BoardTool.Highlighter);

    private void CalligraphyToolButton_Click(object sender, RoutedEventArgs e) =>
        SetActiveTool(BoardTool.Calligraphy);

    private void EraserToolButton_Click(object sender, RoutedEventArgs e) =>
        SetActiveTool(BoardTool.Eraser);

    private void SelectToolButton_Click(object sender, RoutedEventArgs e) =>
        SetActiveTool(BoardTool.Select);

    private void PanToolButton_Click(object sender, RoutedEventArgs e) =>
        SetActiveTool(BoardTool.Pan);

    private BoardTool EffectiveTool =>
        _spaceTemporaryPan ? BoardTool.Pan : _activeTool;

    private void SetActiveTool(BoardTool tool)
    {
        _activeTool = tool;
        if (tool is BoardTool.Pen or BoardTool.Highlighter or BoardTool.Calligraphy)
        {
            _lastDrawingTool = tool;
            var penKind = tool switch
            {
                BoardTool.Highlighter => PenKind.Highlighter,
                BoardTool.Calligraphy => PenKind.Calligraphy,
                _ => PenKind.Pen,
            };
            _penStyle = _penStyle with
            {
                Kind = penKind,
                Thickness = _thicknessByPenKind[penKind],
            };
            UpdateThicknessSlider(_penStyle.Thickness);
        }

        PenToolButton.IsChecked = tool == BoardTool.Pen;
        HighlighterToolButton.IsChecked = tool == BoardTool.Highlighter;
        CalligraphyToolButton.IsChecked = tool == BoardTool.Calligraphy;
        SelectToolButton.IsChecked = tool == BoardTool.Select;
        PenToolMenuItem.IsChecked = tool == BoardTool.Pen;
        HighlighterToolMenuItem.IsChecked = tool == BoardTool.Highlighter;
        CalligraphyToolMenuItem.IsChecked = tool == BoardTool.Calligraphy;
        InkSurface.EditingMode =
            tool is BoardTool.Pen or BoardTool.Highlighter or BoardTool.Calligraphy
            ? InkCanvasEditingMode.Ink
            : InkCanvasEditingMode.None;
        InkSurface.EditingModeInverted = InkCanvasEditingMode.None;
        ApplyDrawingAttributes();
        InkSurface.Focus();
    }

    private void ColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton button &&
            button.Tag is string argbText &&
            uint.TryParse(
                argbText,
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out var argb))
        {
            _penStyle = _penStyle with { Argb = argb };
            RedColorButton.IsChecked = button == RedColorButton;
            GreenColorButton.IsChecked = button == GreenColorButton;
            BlueColorButton.IsChecked = button == BlueColorButton;
            BlackColorButton.IsChecked = button == BlackColorButton;
            YellowColorButton.IsChecked = button == YellowColorButton;
            OrangeColorButton.IsChecked = button == OrangeColorButton;
            PurpleColorButton.IsChecked = button == PurpleColorButton;
            BrownColorButton.IsChecked = button == BrownColorButton;
            ApplyDrawingAttributes();
            InkSurface.Focus();
        }
    }

    private void ThicknessSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingThicknessSlider)
        {
            return;
        }

        _thicknessByPenKind[_penStyle.Kind] = e.NewValue;
        _penStyle = _penStyle with { Thickness = e.NewValue };
        ApplyDrawingAttributes();
    }

    private void UpdateThicknessSlider(double thickness)
    {
        if (ThicknessSlider is null || ThicknessSlider.Value == thickness)
        {
            return;
        }

        _isUpdatingThicknessSlider = true;
        try
        {
            ThicknessSlider.Value = thickness;
        }
        finally
        {
            _isUpdatingThicknessSlider = false;
        }
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
        InkSurface.Cursor = Cursors.None;
    }

    private void ShowPointerDot(Point position)
    {
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
