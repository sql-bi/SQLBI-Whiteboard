using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using SQLBI.Whiteboard.Core.Geometry;
using SQLBI.Whiteboard.Core.Model;
using SQLBI.Whiteboard.Core.Settings;
using SQLBI.Whiteboard.Core.Viewport;

namespace SQLBI.Whiteboard;

internal sealed class BoardSurface : FrameworkElement
{
    private static readonly Brush BackgroundBrush = CreateFrozenBrush(0xFFFFFFFF);
    private static readonly Pen SelectionPen = CreateFrozenPen(0xFF2563EB, 2);
    private static readonly Pen SelectionMemberPen = CreateFrozenPen(0xFF2563EB, 1);
    private static readonly Pen AreaPen = CreateDashedPen(0xFF2563EB, 1.5);
    private static readonly Brush AreaFillBrush = CreateFrozenBrush(0x142563EB);
    private static readonly Brush SelectionHandleBrush = CreateFrozenBrush(0xFFFFFFFF);
    private static readonly Brush MissingImageBrush = CreateFrozenBrush(0xFFE5E7EB);
    private static readonly Pen MissingImagePen = CreateFrozenPen(0xFF9CA3AF, 1);
    private static readonly Pen GridPen = CreateFrozenPen(0x14000000, 1);
    private static readonly Brush GridDotBrush = CreateFrozenBrush(0x14000000);

    // A dot reaches 1.5 pixels out from its intersection rather than measuring
    // 1.5 across: at the grid's own faint gray, a dot narrower than this covers
    // so little of a pixel that antialiasing thins it away to nothing.
    private const double GridDotRadius = 1.5;

    private static readonly Brush FrameBrush = CreateFrozenBrush(0xFF64748B);
    private static readonly Brush FrameTitleBrush = CreateFrozenBrush(0xFFFFFFFF);
    private static readonly Typeface FrameTypeface = new(
        new FontFamily("Segoe UI"),
        FontStyles.Normal,
        FontWeights.SemiBold,
        FontStretches.Normal);

    private readonly Dictionary<string, ImageSource> _imageCache = new(StringComparer.Ordinal);
    private BoardDocument? _document;
    private Camera2D? _camera;

    /// <summary>
    /// Every selected object. A single selection is a set of one, and looks
    /// exactly as it always did: one rectangle with the corner handle. Only a
    /// set of several also outlines its members, because only then is there a
    /// question about which of them the rectangle covers.
    /// </summary>
    public IReadOnlySet<Guid> SelectedObjectIds { get; set; } = new HashSet<Guid>();

    /// <summary>
    /// The rubber band or lasso being drawn right now, in world coordinates.
    /// </summary>
    public IReadOnlyList<PointD>? PendingArea { get; set; }

    public Guid? HoveredObjectId { get; set; }

    public Guid? HiddenObjectId { get; set; }

    public Func<Guid, ImageSource?>? LiveViewImageSourceProvider { get; set; }

    /// <summary>
    /// Off for an export overlay, which is composed over the slide's own
    /// objects and so must be transparent where nothing is drawn.
    /// </summary>
    public bool DrawBackground { get; set; } = true;

    /// <summary>
    /// When set, only the objects it accepts are drawn. An export uses it to
    /// render the ink alone, once the containers have gone out as objects.
    /// </summary>
    public Func<BoardObject, bool>? ObjectFilter { get; set; }

    /// <summary>
    /// Frames are guides for the author, drawn over everything on screen and
    /// left out of every export and preview.
    /// </summary>
    public bool DrawFrames { get; set; } = true;

    /// <summary>
    /// The background grid, under everything and on screen only. Off by default
    /// for the same reason as <see cref="DrawFrames"/> is turned off by an
    /// export: it is a guide for the person drawing, not part of the board.
    /// </summary>
    public GridStyle GridStyle { get; set; } = GridStyle.Off;

    /// <summary>
    /// A word or two beside the resize handle while a gesture needs one, such
    /// as the column count of a text container being reflowed.
    /// </summary>
    public string? HandleLabel { get; set; }

    /// <summary>
    /// The stroke the pen is drawing right now, before it is committed. Pen ink
    /// is collected here rather than by the InkCanvas, so the wet stroke is
    /// drawn here too.
    /// </summary>
    public IReadOnlyList<InkPoint>? PendingStroke { get; set; }

    public PenStyle PendingStrokeStyle { get; set; }

    /// <summary>
    /// The shape being dragged out right now. It is drawn over the board rather
    /// than added to it, so nothing is recorded until the pointer lifts.
    /// </summary>
    public ShapeBoardObject? PendingShape { get; set; }

    /// <summary>
    /// The connector being dragged out right now, or the selected one being
    /// re-routed by an endpoint. Drawn over the board rather than added to it.
    /// </summary>
    public ConnectorBoardObject? PendingConnector { get; set; }

    /// <summary>
    /// The eight binding points of the object under the pointer, while a
    /// connector endpoint is near enough to one of them to take it.
    /// </summary>
    public IReadOnlyList<PointD>? BindingDots { get; set; }

    public void Configure(BoardDocument document, Camera2D camera)
    {
        _document = document;
        _camera = camera;
        _imageCache.Clear();
        InvalidateVisual();
    }

    public void InvalidateAssets()
    {
        _imageCache.Clear();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (DrawBackground)
        {
            drawingContext.DrawRectangle(BackgroundBrush, null, new Rect(RenderSize));
        }

        if (_document is null || _camera is null)
        {
            return;
        }

        if (DrawBackground && GridStyle != GridStyle.Off)
        {
            DrawGrid(drawingContext, _camera);
        }

        foreach (var item in _document.Query(_camera.VisibleWorldBounds))
        {
            if (item.Id == HiddenObjectId || ObjectFilter?.Invoke(item) == false)
            {
                continue;
            }

            switch (item)
            {
                case InkStrokeObject stroke:
                    DrawStroke(drawingContext, stroke, _camera);
                    break;
                case ImageBoardObject image:
                    DrawImage(drawingContext, image, _document, _camera);
                    break;
                case LiveViewBoardObject liveView:
                    DrawLiveView(drawingContext, liveView, _document, _camera);
                    break;
                case FreeTextBoardObject label:
                    DrawLabel(drawingContext, label, _camera);
                    break;
                case TextBoardObject text:
                    TextContainerVisual.Draw(
                        drawingContext,
                        text,
                        _camera,
                        VisualTreeHelper.GetDpi(this).PixelsPerDip,
                        LanguageChipTitleReserve(text));
                    break;
                case ShapeBoardObject shape:
                    DrawShape(drawingContext, shape, _camera);
                    break;
                case ConnectorBoardObject connector:
                    DrawConnector(drawingContext, connector, _camera);
                    break;
            }
        }

        if (DrawFrames)
        {
            foreach (var frame in _document.Frames)
            {
                if (frame.Id != HiddenObjectId && frame.Bounds.Intersects(_camera.VisibleWorldBounds))
                {
                    DrawFrame(drawingContext, frame, _camera);
                }
            }
        }

        // Over the containers, not under them: the stroke is committed with the
        // topmost z-index, so drawing it first made the wet ink disappear behind
        // whatever it crossed and reappear only once the pen lifted.
        if (PendingStroke is { Count: > 1 } pending)
        {
            DrawStroke(drawingContext, pending, PendingStrokeStyle, _camera);
        }

        if (PendingShape is { } pendingShape)
        {
            DrawShape(drawingContext, pendingShape, _camera);
        }

        if (PendingConnector is { } pendingConnector)
        {
            DrawConnector(drawingContext, pendingConnector, _camera);
        }

        if (HoveredObjectId is Guid hoveredId &&
            !SelectedObjectIds.Contains(hoveredId) &&
            _document.Objects.FirstOrDefault(item => item.Id == hoveredId) is { } hovered)
        {
            DrawSelection(drawingContext, hovered.Bounds, _camera, includeHandle: false);
        }

        DrawSelectionSet(drawingContext, _camera);
        if (PendingArea is { Count: > 1 } area)
        {
            DrawPendingArea(drawingContext, area, _camera);
        }

        // Over the selection, because they are what the hand is aiming at: the
        // eight places this endpoint would bind to if it were let go here.
        if (BindingDots is { Count: > 0 } dots)
        {
            foreach (PointD dot in dots)
            {
                drawingContext.DrawEllipse(SelectionHandleBrush, SelectionPen, ToScreenPoint(dot, _camera), 4, 4);
            }
        }
    }

    private void DrawSelectionSet(DrawingContext drawingContext, Camera2D camera)
    {
        if (_document is null || SelectedObjectIds.Count == 0)
        {
            return;
        }

        BoardObject[] selected = _document.Objects
            .Where(item => SelectedObjectIds.Contains(item.Id))
            .ToArray();
        if (selected.Length == 0)
        {
            return;
        }

        if (selected.Length > 1)
        {
            foreach (var item in selected)
            {
                if (TurnedOutline(item) is { } outline)
                {
                    DrawTurnedOutline(drawingContext, outline, camera, SelectionMemberPen);
                    continue;
                }

                drawingContext.DrawRectangle(
                    null,
                    SelectionMemberPen,
                    ToScreenRectangle(item.Bounds, camera));
            }
        }

        var left = selected.Min(item => item.Bounds.Left);
        var top = selected.Min(item => item.Bounds.Top);
        var right = selected.Max(item => item.Bounds.Right);
        var bottom = selected.Max(item => item.Bounds.Bottom);
        var bounds = new RectD(left, top, right - left, bottom - top);

        // A label or a shape on its own is outlined where it is, turned: the box
        // around a turned object says nothing about which of its corners is
        // which. The handle stays on the box, which is where the gesture looks
        // for it.
        if (selected is [{ } lone] && TurnedOutline(lone) is { } loneOutline)
        {
            DrawTurnedOutline(drawingContext, loneOutline, camera, SelectionPen);
            DrawSelection(drawingContext, bounds, camera, includeHandle: true, includeOutline: false);
            return;
        }

        // A connector has no corner to take hold of: what a lone one offers is
        // its two ends, which is what re-routes it and what re-binds it.
        if (selected is [ConnectorBoardObject connector])
        {
            DrawSelection(drawingContext, bounds, camera, includeHandle: false);
            drawingContext.DrawEllipse(
                SelectionHandleBrush, SelectionPen, ToScreenPoint(connector.Start, camera), 7, 7);
            drawingContext.DrawEllipse(
                SelectionHandleBrush, SelectionPen, ToScreenPoint(connector.End, camera), 7, 7);
            return;
        }

        DrawSelection(drawingContext, bounds, camera, includeHandle: true);
    }

    private void DrawLabel(DrawingContext drawingContext, FreeTextBoardObject label, Camera2D camera)
    {
        PointD center = camera.WorldToScreen(label.Bounds.Center);
        FormattedText text = LabelVisual.Format(
            label,
            label.FontSize * camera.Zoom,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var origin = new Point(
            center.X - (label.LayoutWidth * camera.Zoom / 2),
            center.Y - (label.LayoutHeight * camera.Zoom / 2));

        if (label.AngleDegrees == 0)
        {
            drawingContext.DrawText(text, origin);
            return;
        }

        var rotation = new RotateTransform(label.AngleDegrees, center.X, center.Y);
        rotation.Freeze();
        drawingContext.PushTransform(rotation);
        drawingContext.DrawText(text, origin);
        drawingContext.Pop();
    }

    /// <summary>
    /// The four corners a selection outline follows instead of the box, for the
    /// objects that are drawn turned. An upright one has nothing to say here and
    /// takes the rectangle.
    /// </summary>
    private static IReadOnlyList<PointD>? TurnedOutline(BoardObject item) => item switch
    {
        FreeTextBoardObject label => label.Corners(),
        ShapeBoardObject { AngleDegrees: not 0 } shape => shape.Corners(),
        _ => null,
    };

    private static void DrawTurnedOutline(
        DrawingContext drawingContext,
        IReadOnlyList<PointD> corners,
        Camera2D camera,
        Pen pen)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            PointD first = camera.WorldToScreen(corners[0]);
            context.BeginFigure(new Point(first.X, first.Y), false, true);
            context.PolyLineTo(
                [.. corners.Skip(1).Select(corner =>
                {
                    PointD screen = camera.WorldToScreen(corner);
                    return new Point(screen.X, screen.Y);
                })],
                true,
                false);
        }

        geometry.Freeze();
        drawingContext.DrawGeometry(null, pen, geometry);
    }

    private static void DrawPendingArea(
        DrawingContext drawingContext,
        IReadOnlyList<PointD> area,
        Camera2D camera)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            PointD first = camera.WorldToScreen(area[0]);
            context.BeginFigure(new Point(first.X, first.Y), true, true);
            context.PolyLineTo(
                [.. area.Skip(1).Select(point =>
                {
                    PointD screen = camera.WorldToScreen(point);
                    return new Point(screen.X, screen.Y);
                })],
                true,
                false);
        }

        geometry.Freeze();
        drawingContext.DrawGeometry(AreaFillBrush, AreaPen, geometry);
    }

    /// <summary>
    /// The grid is faint enough that half a pixel of it disappears, so the lines
    /// are snapped: a whole-pixel position plus half the pen's width puts the
    /// stroke inside one device pixel instead of spreading it over two paler ones.
    /// </summary>
    private void DrawGrid(DrawingContext drawingContext, Camera2D camera)
    {
        var visible = camera.VisibleWorldBounds;
        var spacing = GridGeometry.SpacingFor(camera.Zoom);
        var columns = GridGeometry.VerticalLines(visible, spacing)
            .Select(x => Math.Round(camera.WorldToScreen(new PointD(x, visible.Top)).X))
            .ToArray();
        var rows = GridGeometry.HorizontalLines(visible, spacing)
            .Select(y => Math.Round(camera.WorldToScreen(new PointD(visible.Left, y)).Y))
            .ToArray();
        var width = RenderSize.Width;
        var height = RenderSize.Height;

        if (GridStyle == GridStyle.Lines)
        {
            foreach (var x in columns)
            {
                drawingContext.DrawLine(GridPen, new Point(x + 0.5, 0), new Point(x + 0.5, height));
            }

            foreach (var y in rows)
            {
                drawingContext.DrawLine(GridPen, new Point(0, y + 0.5), new Point(width, y + 0.5));
            }

            return;
        }

        // One geometry rather than a drawing record for every dot: the surface is
        // redrawn on each frame of a pan, and a dense grid runs to tens of
        // thousands of intersections.
        var radius = new Size(GridDotRadius, GridDotRadius);
        var dots = new StreamGeometry();
        using (var context = dots.Open())
        {
            foreach (var y in rows)
            {
                foreach (var x in columns)
                {
                    context.BeginFigure(new Point(x - GridDotRadius, y), isFilled: true, isClosed: true);
                    context.ArcTo(
                        new Point(x + GridDotRadius, y),
                        radius,
                        0,
                        isLargeArc: false,
                        SweepDirection.Clockwise,
                        isStroked: false,
                        isSmoothJoin: false);
                    context.ArcTo(
                        new Point(x - GridDotRadius, y),
                        radius,
                        0,
                        isLargeArc: false,
                        SweepDirection.Clockwise,
                        isStroked: false,
                        isSmoothJoin: false);
                }
            }
        }

        dots.Freeze();
        drawingContext.DrawGeometry(GridDotBrush, null, dots);
    }

    private static void DrawStroke(
        DrawingContext drawingContext,
        InkStrokeObject stroke,
        Camera2D camera) =>
        DrawStroke(drawingContext, stroke.Points, stroke.Style, camera);

    private static void DrawStroke(
        DrawingContext drawingContext,
        IReadOnlyList<InkPoint> strokePoints,
        PenStyle style,
        Camera2D camera)
    {
        var points = new StylusPointCollection(strokePoints.Select(point =>
        {
            var screen = camera.WorldToScreen(point.Position);
            return new StylusPoint(
                screen.X,
                screen.Y,
                Math.Clamp(point.Pressure, 0f, 1f));
        }));

        var attributes = InkDrawingAttributes.Create(style, camera.Zoom);
        var wpfStroke = new Stroke(points, attributes);
        wpfStroke.Draw(drawingContext);
    }

    private void DrawImage(
        DrawingContext drawingContext,
        ImageBoardObject image,
        BoardDocument document,
        Camera2D camera)
    {
        var topLeft = camera.WorldToScreen(new PointD(image.Bounds.Left, image.Bounds.Top));
        var bottomRight = camera.WorldToScreen(new PointD(image.Bounds.Right, image.Bounds.Bottom));
        var destination = new Rect(
            topLeft.X,
            topLeft.Y,
            Math.Max(1, bottomRight.X - topLeft.X),
            Math.Max(1, bottomRight.Y - topLeft.Y));

        if (TryGetImage(image.AssetId, document, out var source))
        {
            drawingContext.DrawImage(source, destination);
        }
        else
        {
            drawingContext.DrawRectangle(MissingImageBrush, MissingImagePen, destination);
        }
    }

    private void DrawLiveView(
        DrawingContext drawingContext,
        LiveViewBoardObject liveView,
        BoardDocument document,
        Camera2D camera)
    {
        Rect destination = ToScreenRectangle(liveView.Bounds, camera);
        ImageSource? source = LiveViewImageSourceProvider?.Invoke(liveView.Id);
        if (source is null &&
            liveView.SnapshotAssetId is { } assetId &&
            TryGetImage(assetId, document, out ImageSource? snapshot))
        {
            source = snapshot;
        }

        if (source is not null)
        {
            drawingContext.DrawImage(source, destination);
        }
        else
        {
            drawingContext.DrawRectangle(MissingImageBrush, MissingImagePen, destination);
        }
    }

    private void DrawFrame(DrawingContext drawingContext, FrameBoardObject frame, Camera2D camera)
    {
        var rectangle = ToScreenRectangle(frame.Bounds, camera);
        var pen = new Pen(FrameBrush, 1.5)
        {
            DashStyle = new DashStyle([6, 4], 0),
        };
        pen.Freeze();
        drawingContext.DrawRoundedRectangle(null, pen, rectangle, 3, 3);

        var tab = ToScreenRectangle(frame.TabRect(camera.Zoom), camera);
        drawingContext.DrawRectangle(FrameBrush, null, tab);
        var title = new FormattedText(
            string.IsNullOrWhiteSpace(frame.Title) ? "Slide" : frame.Title,
            System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            FrameTypeface,
            12,
            FrameTitleBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = Math.Max(1, tab.Width - 12),
            MaxTextHeight = Math.Max(1, tab.Height),
            Trimming = TextTrimming.CharacterEllipsis,
        };
        drawingContext.DrawText(
            title,
            new Point(tab.Left + 6, tab.Top + Math.Max(0, (tab.Height - title.Height) / 2)));
    }

    private static Rect ToScreenRectangle(RectD bounds, Camera2D camera)
    {
        PointD topLeft = camera.WorldToScreen(new PointD(bounds.Left, bounds.Top));
        PointD bottomRight = camera.WorldToScreen(new PointD(bounds.Right, bounds.Bottom));
        return new Rect(
            topLeft.X,
            topLeft.Y,
            Math.Max(1, bottomRight.X - topLeft.X),
            Math.Max(1, bottomRight.Y - topLeft.Y));
    }

    private double LanguageChipTitleReserve(TextBoardObject text)
    {
        if (_camera is null ||
            SelectedObjectIds.Count != 1 ||
            !SelectedObjectIds.Contains(text.Id))
        {
            return 0;
        }

        Rect destination = ToScreenRectangle(text.Bounds, _camera);
        return destination.Width >= TextContainerVisual.LanguageChipWidth +
               (2 * TextContainerVisual.LanguageChipMargin) &&
               destination.Height >= TextContainerVisual.LanguageChipHeight
            ? TextContainerVisual.LanguageChipWidth + TextContainerVisual.LanguageChipMargin
            : 0;
    }

    private bool TryGetImage(
        string assetId,
        BoardDocument document,
        out ImageSource? image)
    {
        if (_imageCache.TryGetValue(assetId, out image))
        {
            return true;
        }

        if (!document.Assets.TryGetValue(assetId, out var asset))
        {
            image = null;
            return false;
        }

        try
        {
            image = BoardImageCodec.Decode(asset.Data).Source;
            _imageCache[assetId] = image;
            return true;
        }
        catch
        {
            image = null;
            return false;
        }
    }

    private void DrawSelection(
        DrawingContext drawingContext,
        RectD bounds,
        Camera2D camera,
        bool includeHandle,
        bool includeOutline = true)
    {
        var topLeft = camera.WorldToScreen(new PointD(bounds.Left, bounds.Top));
        var bottomRight = camera.WorldToScreen(new PointD(bounds.Right, bounds.Bottom));
        var rectangle = new Rect(
            topLeft.X,
            topLeft.Y,
            Math.Max(1, bottomRight.X - topLeft.X),
            Math.Max(1, bottomRight.Y - topLeft.Y));
        if (includeOutline)
        {
            drawingContext.DrawRectangle(null, SelectionPen, rectangle);
        }

        if (includeHandle)
        {
            drawingContext.DrawEllipse(
                SelectionHandleBrush,
                SelectionPen,
                new Point(bottomRight.X, bottomRight.Y),
                7,
                7);
        }

        if (includeHandle && HandleLabel is { Length: > 0 } label)
        {
            var text = new FormattedText(
                label,
                System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                FrameTypeface,
                12,
                FrameTitleBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            var badge = new Rect(bottomRight.X + 12, bottomRight.Y - (text.Height / 2) - 3, text.Width + 12, text.Height + 6);
            drawingContext.DrawRoundedRectangle(SelectionPen.Brush, null, badge, 4, 4);
            drawingContext.DrawText(text, new Point(badge.Left + 6, badge.Top + 3));
        }
    }

    private static Color ToColor(uint argb) => Color.FromArgb(
        (byte)(argb >> 24),
        (byte)(argb >> 16),
        (byte)(argb >> 8),
        (byte)argb);

    private static SolidColorBrush CreateFrozenBrush(uint argb)
    {
        var brush = new SolidColorBrush(ToColor(argb));
        brush.Freeze();
        return brush;
    }

    private static Pen CreateFrozenPen(uint argb, double thickness)
    {
        var pen = new Pen(CreateFrozenBrush(argb), thickness);
        pen.Freeze();
        return pen;
    }

    private static Pen CreateDashedPen(uint argb, double thickness)
    {
        var pen = new Pen(CreateFrozenBrush(argb), thickness)
        {
            DashStyle = new DashStyle([4, 3], 0),
        };
        pen.Freeze();
        return pen;
    }

    private static Point ToScreenPoint(PointD world, Camera2D camera)
    {
        PointD screen = camera.WorldToScreen(world);
        return new Point(screen.X, screen.Y);
    }

    /// <summary>
    /// A shape from the outline Core describes, filled and then stroked in the
    /// camera's own space. The arcs are handed to WPF as arcs rather than as the
    /// polygon the hit test walks, so a circle stays a circle at any zoom, and
    /// the outline thickens with the zoom exactly as ink does. A turned shape is
    /// described in the box it was drawn in and turned about its centre, as a
    /// label is, so an arc stays an arc rather than being walked point by point.
    /// </summary>
    private static void DrawShape(
        DrawingContext drawingContext,
        ShapeBoardObject shape,
        Camera2D camera)
    {
        ShapeOutline outline = ShapeGeometry.Describe(shape.Kind, shape.LayoutBounds);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(ToScreenPoint(outline.Start, camera), isFilled: true, isClosed: true);
            foreach (ShapeSegment segment in outline.Segments)
            {
                if (segment.Kind == ShapeSegmentKind.Line)
                {
                    context.LineTo(
                        ToScreenPoint(segment.End, camera),
                        isStroked: true,
                        isSmoothJoin: false);
                }
                else
                {
                    context.ArcTo(
                        ToScreenPoint(segment.End, camera),
                        new Size(segment.RadiusX * camera.Zoom, segment.RadiusY * camera.Zoom),
                        0,
                        isLargeArc: false,
                        segment.Clockwise ? SweepDirection.Clockwise : SweepDirection.Counterclockwise,
                        isStroked: true,
                        isSmoothJoin: false);
                }
            }
        }

        geometry.Freeze();
        var pen = new Pen(
            CreateFrozenBrush(shape.OutlineArgb),
            Math.Max(0.1, shape.Thickness * camera.Zoom))
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        pen.Freeze();
        SolidColorBrush? fill = shape.FillArgb is { } argb ? CreateFrozenBrush(argb) : null;
        if (shape.AngleDegrees == 0)
        {
            drawingContext.DrawGeometry(fill, pen, geometry);
            return;
        }

        PointD center = camera.WorldToScreen(shape.Bounds.Center);
        var rotation = new RotateTransform(shape.AngleDegrees, center.X, center.Y);
        rotation.Freeze();
        drawingContext.PushTransform(rotation);
        drawingContext.DrawGeometry(fill, pen, geometry);
        drawingContext.Pop();
    }

    /// <summary>
    /// The line Core describes, and the filled head at the end of it. The line
    /// stops at the base of the head, so a thick connector does not push a
    /// rounded cap out through its own tip.
    /// </summary>
    private static void DrawConnector(
        DrawingContext drawingContext,
        ConnectorBoardObject connector,
        Camera2D camera)
    {
        IReadOnlyList<PointD> polyline = connector.Polyline();
        SolidColorBrush brush = CreateFrozenBrush(connector.Argb);
        var pen = new Pen(brush, Math.Max(0.1, connector.Thickness * camera.Zoom))
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        pen.Freeze();
        drawingContext.DrawGeometry(
            null,
            pen,
            PolylineGeometry(
                ConnectorGeometry.LinePath(connector.Kind, polyline, connector.Thickness),
                camera,
                isClosed: false,
                isFilled: false));

        if (ConnectorGeometry.Arrowhead(connector.Kind, polyline, connector.Thickness) is { } head)
        {
            drawingContext.DrawGeometry(
                brush,
                null,
                PolylineGeometry(head, camera, isClosed: true, isFilled: true));
        }
    }

    private static StreamGeometry PolylineGeometry(
        IReadOnlyList<PointD> points,
        Camera2D camera,
        bool isClosed,
        bool isFilled)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(ToScreenPoint(points[0], camera), isFilled, isClosed);
            context.PolyLineTo(
                [.. points.Skip(1).Select(point => ToScreenPoint(point, camera))],
                isStroked: true,
                isSmoothJoin: false);
        }

        geometry.Freeze();
        return geometry;
    }
}
