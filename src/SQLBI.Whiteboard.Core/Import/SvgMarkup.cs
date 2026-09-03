using System.Xml;
using System.Xml.Linq;

namespace SQLBI.Whiteboard.Core.Import;

/// <summary>
/// Rewrites SVG markup around the renderer's blind spots before it is drawn. The
/// markup is otherwise stored and drawn as it arrived.
/// </summary>
public static class SvgMarkup
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    /// <summary>
    /// Moves an image's own <c>clip-path</c>, and its <c>transform</c> with it, onto a
    /// group around the image. SharpVectors applies the clip on the same drawing group
    /// as the scale and offset it builds for the image's width, height, and aspect ratio,
    /// so a clip written in page coordinates is scaled and shifted along with the bitmap
    /// and lands somewhere else (issue 98). On a group the clip is honored where the
    /// author put it. Markup with nothing to move comes back as the same bytes; markup
    /// that does not parse is left for the renderer to reject in its own words.
    /// </summary>
    public static byte[] HoistImageClips(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
            });
            var document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
            var clipped = document
                .Descendants(Svg + "image")
                .Where(image => image.Attribute("clip-path") is not null)
                .ToArray();
            if (clipped.Length == 0)
            {
                return bytes;
            }

            foreach (var image in clipped)
            {
                var group = new XElement(Svg + "g");
                foreach (var name in new[] { "clip-path", "transform" })
                {
                    if (image.Attribute(name) is { } attribute)
                    {
                        attribute.Remove();
                        group.Add(new XAttribute(name, attribute.Value));
                    }
                }

                image.ReplaceWith(group);
                group.Add(image);
            }

            // Formatting off: re-indenting would put whitespace between the runs of a
            // <text>, which SVG renders as spaces.
            using var output = new MemoryStream();
            document.Save(output, SaveOptions.DisableFormatting);
            return output.ToArray();
        }
        catch (XmlException)
        {
            return bytes;
        }
    }
}
