using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

    private readonly Dictionary<string, BitmapSource> _bitmapCache = new(StringComparer.Ordinal);
    private BoardDocument? _document;
    private Camera2D? _camera;

    public Guid? SelectedObjectId { get; set; }

    public void Configure(BoardDocument document, Camera2D camera)
    {
        _document = document;
        _camera = camera;
        _bitmapCache.Clear();
        InvalidateVisual();
    }

    public void InvalidateAssets()
    {
        _bitmapCache.Clear();
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

        foreach (var item in _document.Query(_camera.VisibleWorldBounds))
        {
            switch (item)
            {
                case InkStrokeObject stroke:
                    DrawStroke(drawingContext, stroke, _camera);
                    break;
                case ImageBoardObject image:
                    DrawImage(drawingContext, image, _document, _camera);
                    break;
            }
        }

        if (SelectedObjectId is Guid selectedId &&
            _document.Objects.FirstOrDefault(item => item.Id == selectedId) is { } selected)
        {
            DrawSelection(drawingContext, selected.Bounds, _camera);
        }
    }

    private static void DrawStroke(
        DrawingContext drawingContext,
        InkStrokeObject stroke,
        Camera2D camera)
    {
        var points = new StylusPointCollection(stroke.Points.Select(point =>
        {
            var screen = camera.WorldToScreen(point.Position);
            return new StylusPoint(
                screen.X,
                screen.Y,
                Math.Clamp(point.Pressure, 0f, 1f));
        }));

        var attributes = InkDrawingAttributes.Create(stroke.Style, camera.Zoom);
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

        if (TryGetBitmap(image.AssetId, document, out var bitmap))
        {
            drawingContext.DrawImage(bitmap, destination);
        }
        else
        {
            drawingContext.DrawRectangle(MissingImageBrush, MissingImagePen, destination);
        }
    }

    private bool TryGetBitmap(
        string assetId,
        BoardDocument document,
        out BitmapSource? bitmap)
    {
        if (_bitmapCache.TryGetValue(assetId, out bitmap))
        {
            return true;
        }

        if (!document.Assets.TryGetValue(assetId, out var asset))
        {
            bitmap = null;
            return false;
        }

        try
        {
            bitmap = WpfImageCodec.Decode(asset.Data);
            _bitmapCache[assetId] = bitmap;
            return true;
        }
        catch
        {
            bitmap = null;
            return false;
        }
    }

    private static void DrawSelection(
        DrawingContext drawingContext,
        RectD bounds,
        Camera2D camera)
    {
        var topLeft = camera.WorldToScreen(new PointD(bounds.Left, bounds.Top));
        var bottomRight = camera.WorldToScreen(new PointD(bounds.Right, bounds.Bottom));
        var rectangle = new Rect(
            topLeft.X,
            topLeft.Y,
            Math.Max(1, bottomRight.X - topLeft.X),
            Math.Max(1, bottomRight.Y - topLeft.Y));
        drawingContext.DrawRectangle(null, SelectionPen, rectangle);
        drawingContext.DrawEllipse(
            SelectionHandleBrush,
            SelectionPen,
            new Point(bottomRight.X, bottomRight.Y),
            7,
            7);
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
