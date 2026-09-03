using System.IO;
using System.Windows.Media;
using SharpVectors.Converters;
using SharpVectors.Dom;
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

        using var reader = new FileSvgReader(settings);
        using var stream = new MemoryStream(SvgMarkup.HoistImageClips(bytes), writable: false);
        var drawing = reader.Read(stream)
            ?? throw new InvalidDataException("The SVG has nothing to draw.");

        var image = new DrawingImage(drawing);
        if (image.CanFreeze)
        {
            image.Freeze();
        }

        return image;
    }
}
