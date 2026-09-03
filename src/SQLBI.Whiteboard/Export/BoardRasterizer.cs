using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SQLBI.Whiteboard.Core.Geometry;
using SQLBI.Whiteboard.Core.Model;
using SQLBI.Whiteboard.Core.Viewport;

namespace SQLBI.Whiteboard.Export;

/// <summary>
/// Draws any world rectangle at any pixel size, through the same
/// <see cref="BoardSurface"/> that draws the screen. A fresh surface has no
/// selection, no hover, and no pending stroke, so nothing transient reaches an
/// export. Must run on the UI thread: <see cref="RenderTargetBitmap"/> insists.
/// </summary>
internal static class BoardRasterizer
{
    /// <summary>
    /// The same margin the board leaves when it frames content.
    /// </summary>
    public const double DefaultPaddingFraction = 0.04;

    public static BitmapSource Render(
        BoardDocument document,
        RectD world,
        int pixelWidth,
        int pixelHeight,
        Func<Guid, ImageSource?>? liveViewImageSourceProvider = null,
        double paddingFraction = DefaultPaddingFraction,
        Action<DrawingContext, Camera2D>? overlay = null,
        bool drawBackground = true,
        Func<BoardObject, bool>? objectFilter = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        pixelWidth = Math.Max(1, pixelWidth);
        pixelHeight = Math.Max(1, pixelHeight);

        var camera = CameraFor(world, pixelWidth, pixelHeight, paddingFraction);
        var surface = new BoardSurface
        {
            Width = pixelWidth,
            Height = pixelHeight,
            LiveViewImageSourceProvider = liveViewImageSourceProvider,
            DrawBackground = drawBackground,
            ObjectFilter = objectFilter,
            DrawFrames = false,
        };
        surface.Configure(document, camera);
        surface.Measure(new Size(pixelWidth, pixelHeight));
        surface.Arrange(new Rect(0, 0, pixelWidth, pixelHeight));
        surface.UpdateLayout();

        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(surface);

        if (overlay is not null)
        {
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                overlay(context, camera);
            }

            bitmap.Render(visual);
        }

        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>
    /// The camera a render of <paramref name="world"/> at this pixel size uses,
    /// so that anything placed beside the picture lands where the picture
    /// would have drawn it.
    /// </summary>
    public static Camera2D CameraFor(
        RectD world,
        int pixelWidth,
        int pixelHeight,
        double paddingFraction = DefaultPaddingFraction)
    {
        var camera = new Camera2D();
        camera.Resize(Math.Max(1, pixelWidth), Math.Max(1, pixelHeight));
        camera.Frame(world, paddingFraction);
        return camera;
    }

    /// <summary>
    /// The pixel size that fits <paramref name="world"/> inside a box while
    /// keeping the world aspect, so that a landscape area does not get a
    /// portrait bitmap with white bands. The longer edge is the box's.
    /// </summary>
    public static (int Width, int Height) FitPixelSize(RectD world, int boxWidth, int boxHeight)
    {
        var aspect = world.Width / Math.Max(world.Height, 0.000001);
        var width = boxWidth;
        var height = (int)Math.Round(boxWidth / aspect);
        if (height > boxHeight)
        {
            height = boxHeight;
            width = (int)Math.Round(boxHeight * aspect);
        }

        return (Math.Max(1, width), Math.Max(1, height));
    }
}
