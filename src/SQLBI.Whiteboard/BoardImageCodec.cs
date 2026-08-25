using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SQLBI.Whiteboard.Core.Geometry;
using SQLBI.Whiteboard.Core.Import;

namespace SQLBI.Whiteboard;

/// <summary>
/// A decoded image asset. <see cref="IsVector"/> decides how it is sized on arrival and
/// whether the clipboard can be handed the markup as well as a picture.
/// </summary>
internal readonly record struct BoardImage(
    ImageSource Source,
    double NaturalWidth,
    double NaturalHeight,
    bool IsVector);

/// <summary>
/// The one place an asset's bytes become something drawable. Assets are stored as they
/// arrived and carry a content type, but boards written before SVG support have none, so
/// the bytes themselves decide which decoder runs.
/// </summary>
internal static class BoardImageCodec
{
    public static BoardImage Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (DroppedFileImport.LooksLikeSvg(bytes))
        {
            var drawing = SvgImageCodec.Decode(bytes);
            var bounds = drawing.Drawing?.Bounds ?? Rect.Empty;
            return new BoardImage(
                drawing,
                Math.Max(1, bounds.Width),
                Math.Max(1, bounds.Height),
                IsVector: true);
        }

        var bitmap = WpfImageCodec.Decode(bytes);
        return new BoardImage(
            bitmap,
            Math.Max(1, bitmap.PixelWidth),
            Math.Max(1, bitmap.PixelHeight),
            IsVector: false);
    }

    /// <summary>
    /// The size a newly arrived image takes on the board.
    /// </summary>
    public static (double Width, double Height) ArrivalSize(BoardImage image) => image.IsVector
        ? ImportLayout.VectorImageSize(image.NaturalWidth, image.NaturalHeight)
        : ImportLayout.ImageSize(image.NaturalWidth, image.NaturalHeight);

    /// <summary>
    /// Flattens an image to pixels for the clipboard, which cannot carry a WPF drawing.
    /// A vector is rendered at whichever is larger of the size it is shown at and its
    /// natural size, so the copy is never coarser than either.
    /// </summary>
    public static BitmapSource Rasterize(BoardImage image, RectD? shownAs = null)
    {
        if (image.Source is BitmapSource bitmap)
        {
            return bitmap;
        }

        var scale = shownAs is { } bounds
            ? Math.Max(1, bounds.Width / image.NaturalWidth)
            : 1;
        var width = (int)Math.Clamp(Math.Round(image.NaturalWidth * scale), 1, 8192);
        var height = (int)Math.Clamp(Math.Round(image.NaturalHeight * scale), 1, 8192);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawImage(image.Source, new Rect(0, 0, width, height));
        }

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }
}
