using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using SQLBI.Whiteboard.Core.Geometry;
using SQLBI.Whiteboard.Core.Model;
using SQLBI.Whiteboard.Core.Viewport;

namespace SQLBI.Whiteboard;

internal sealed class BoardSurface : FrameworkElement
{
    private static readonly Brush BackgroundBrush = CreateFrozenBrush(0xFFFFFFFF);
    private static readonly Pen SelectionPen = CreateFrozenPen(0xFF2563EB, 2);
    private static readonly Brush SelectionHandleBrush = CreateFrozenBrush(0xFFFFFFFF);
    private static readonly Brush MissingImageBrush = CreateFrozenBrush(0xFFE5E7EB);
    private static readonly Pen MissingImagePen = CreateFrozenPen(0xFF9CA3AF, 1);

    private readonly Dictionary<string, ImageSource> _imageCache = new(StringComparer.Ordinal);
    private BoardDocument? _document;
    private Camera2D? _camera;

    public Guid? SelectedObjectId { get; set; }

    public Guid? HoveredObjectId { get; set; }

    public Guid? HiddenObjectId { get; set; }

    public Func<Guid, ImageSource?>? LiveViewImageSourceProvider { get; set; }

    /// <summary>
    /// The stroke the pen is drawing right now, before it is committed. Pen ink
    /// is collected here rather than by the InkCanvas, so the wet stroke is
    /// drawn here too.
    /// </summary>
    public IReadOnlyList<InkPoint>? PendingStroke { get; set; }

    public PenStyle PendingStrokeStyle { get; set; }

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
        drawingContext.DrawRectangle(BackgroundBrush, null, new Rect(RenderSize));

        if (_document is null || _camera is null)
        {
            return;
        }

        if (PendingStroke is { Count: > 1 } pending)
        {
            DrawStroke(drawingContext, pending, PendingStrokeStyle, _camera);
        }

        foreach (var item in _document.Query(_camera.VisibleWorldBounds))
        {
            if (item.Id == HiddenObjectId)
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
                case TextBoardObject text:
                    TextContainerVisual.Draw(
                        drawingContext,
                        text,
                        _camera,
                        VisualTreeHelper.GetDpi(this).PixelsPerDip,
                        LanguageChipTitleReserve(text));
                    break;
            }
        }

        if (HoveredObjectId is Guid hoveredId &&
            hoveredId != SelectedObjectId &&
            _document.Objects.FirstOrDefault(item => item.Id == hoveredId) is { } hovered)
        {
            DrawSelection(drawingContext, hovered.Bounds, _camera, includeHandle: false);
        }

        if (SelectedObjectId is Guid selectedId &&
            _document.Objects.FirstOrDefault(item => item.Id == selectedId) is { } selected)
        {
            DrawSelection(drawingContext, selected.Bounds, _camera, includeHandle: true);
        }
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
        if (_camera is null || text.Id != SelectedObjectId)
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

    private static void DrawSelection(
        DrawingContext drawingContext,
        RectD bounds,
        Camera2D camera,
        bool includeHandle)
    {
        var topLeft = camera.WorldToScreen(new PointD(bounds.Left, bounds.Top));
        var bottomRight = camera.WorldToScreen(new PointD(bounds.Right, bounds.Bottom));
        var rectangle = new Rect(
            topLeft.X,
            topLeft.Y,
            Math.Max(1, bottomRight.X - topLeft.X),
            Math.Max(1, bottomRight.Y - topLeft.Y));
        drawingContext.DrawRectangle(null, SelectionPen, rectangle);
        if (includeHandle)
        {
            drawingContext.DrawEllipse(
                SelectionHandleBrush,
                SelectionPen,
                new Point(bottomRight.X, bottomRight.Y),
                7,
                7);
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
}
