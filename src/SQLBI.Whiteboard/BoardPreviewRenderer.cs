using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SQLBI.Whiteboard.Core.Geometry;
using SQLBI.Whiteboard.Core.Model;
using SQLBI.Whiteboard.Core.Viewport;

namespace SQLBI.Whiteboard;

internal static class BoardPreviewRenderer
{
    public const int MaxEdge = 1024;

    public static byte[]? Render(
        BoardDocument document,
        Func<Guid, ImageSource?>? liveViewImageSourceProvider = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.ContentBounds is not { } content ||
            content.Width < 1 ||
            content.Height < 1)
        {
            return null;
        }

        var pixelSize = PixelSize(content);
        var camera = new Camera2D();
        camera.Resize(pixelSize.Width, pixelSize.Height);
        camera.Frame(content);

        var surface = new BoardSurface
        {
            Width = pixelSize.Width,
            Height = pixelSize.Height,
            LiveViewImageSourceProvider = liveViewImageSourceProvider,
            DrawFrames = false,
        };
        surface.Configure(document, camera);
        surface.Measure(new Size(pixelSize.Width, pixelSize.Height));
        surface.Arrange(new Rect(0, 0, pixelSize.Width, pixelSize.Height));
        surface.UpdateLayout();

        var bitmap = new RenderTargetBitmap(
            pixelSize.Width,
            pixelSize.Height,
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(surface);
        bitmap.Freeze();
        return WpfImageCodec.EncodePng(bitmap);
    }

    private static (int Width, int Height) PixelSize(RectD world)
    {
        var aspect = world.Width / Math.Max(world.Height, 0.000001);
        if (aspect >= 1)
        {
            var width = MaxEdge;
            var height = Math.Max(1, (int)Math.Round(MaxEdge / aspect));
            return (width, height);
        }

        var tall = MaxEdge;
        var narrow = Math.Max(1, (int)Math.Round(MaxEdge * aspect));
        return (narrow, tall);
    }
}
