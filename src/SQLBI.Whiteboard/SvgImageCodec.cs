using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SharpVectors.Converters;
using SharpVectors.Dom;
using SharpVectors.Dom.Svg;
using SharpVectors.Renderers.Wpf;
using SQLBI.Whiteboard.Core.Import;

namespace SQLBI.Whiteboard;

internal static class SvgImageCodec
{
    public static DrawingImage Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var settings = new WpfDrawingSettings
        {
            // Nothing here needs the SharpVectors runtime types; a plain drawing draws faster.
            IncludeRuntime = false,
            TextAsGeometry = false,

            // Without these the drawing is only as large as the marks in it, so an SVG
            // that centres a label in a 300x60 canvas would arrive cropped to the glyphs
            // and stretched. Together they keep the author's viewBox as the container.
            EnsureViewboxSize = true,
            EnsureViewboxPosition = true,

            // Dropped and pasted SVG is untrusted markup. Left at its default this would
            // fetch whatever an <image href> or an external stylesheet names, which turns
            // opening a board into an outbound request.
            ExternalResourcesAccessMode = ExternalResourcesAccessModes.Ignore,
        };
        settings.Visitors.ImageVisitor = PixelSizedBitmapVisitor.Instance;

        using var reader = new FileSvgReader(settings);
        using var stream = new MemoryStream(SvgMarkup.Rewrite(bytes), writable: false);
        var drawing = reader.Read(stream)
            ?? throw new InvalidDataException("The SVG has nothing to draw.");

        var image = new DrawingImage(drawing);
        if (image.CanFreeze)
        {
            image.Freeze();
        }

        return image;
    }

    /// <summary>
    /// Decodes the bitmaps an SVG embeds so that one bitmap pixel is one user unit.
    /// SharpVectors fits an embedded bitmap into its <c>&lt;image&gt;</c> by the WPF
    /// <c>Width</c> and <c>Height</c> of the decoded picture, which are device-independent
    /// units and so scale with the DPI the file declares: a 72-DPI PNG comes out a third
    /// larger than the pixels the author placed, and a 216-DPI logo less than half the
    /// size. The browsers measure the pixels. Handing the renderer the same pixels at
    /// 96 DPI makes the two agree. Anything that is not a base64 data URI, or that WPF
    /// cannot decode, is left to the renderer's own reader.
    /// </summary>
    private sealed class PixelSizedBitmapVisitor : WpfEmbeddedImageVisitor
    {
        public static PixelSizedBitmapVisitor Instance { get; } = new();

        private const double ScreenDpi = 96;

        public override ImageSource? Visit(SvgImageElement element, WpfDrawingContext context)
        {
            var href = element.Href?.AnimVal;
            if (href is null || !href.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var comma = href.IndexOf(',');
            if (comma < 0 || !href[..comma].Contains(";base64", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            try
            {
                var bytes = Convert.FromBase64String(href[(comma + 1)..]);
                using var stream = new MemoryStream(bytes, writable: false);
                var decoder = BitmapDecoder.Create(
                    stream,
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count == 0)
                {
                    return null;
                }

                BitmapSource frame = decoder.Frames[0];
                return Math.Abs(frame.DpiX - ScreenDpi) < 0.01 && Math.Abs(frame.DpiY - ScreenDpi) < 0.01
                    ? frame
                    : AtScreenDpi(frame);
            }
            catch (Exception exception) when (exception is FormatException or IOException or NotSupportedException or ArgumentException)
            {
                return null;
            }
        }

        private static BitmapSource AtScreenDpi(BitmapSource frame)
        {
            var stride = (frame.PixelWidth * frame.Format.BitsPerPixel + 7) / 8;
            var pixels = new byte[stride * frame.PixelHeight];
            frame.CopyPixels(pixels, stride, 0);
            var bitmap = BitmapSource.Create(
                frame.PixelWidth,
                frame.PixelHeight,
                ScreenDpi,
                ScreenDpi,
                frame.Format,
                frame.Palette,
                pixels,
                stride);
            bitmap.Freeze();
            return bitmap;
        }
    }
}
