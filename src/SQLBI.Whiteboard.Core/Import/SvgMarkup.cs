using System.Globalization;
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
    /// Applies every rewrite the renderer needs and returns the markup to hand it.
    /// Markup with nothing to change comes back as the same bytes; markup that does
    /// not parse is left for the renderer to reject in its own words.
    /// </summary>
    public static byte[] Rewrite(byte[] bytes)
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
            var changed = HoistImageClips(document);
            changed |= DropAnchoredLetterSpacing(document);
            if (!changed)
            {
                return bytes;
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

    /// <summary>
    /// Moves an image's own <c>clip-path</c>, and its <c>transform</c> with it, onto a
    /// group around the image. SharpVectors applies the clip on the same drawing group
    /// as the scale and offset it builds for the image's width, height, and aspect ratio,
    /// so a clip written in page coordinates is scaled and shifted along with the bitmap
    /// and lands somewhere else (issue 98). On a group the clip is honored where the
    /// author put it.
    /// </summary>
    private static bool HoistImageClips(XDocument document)
    {
        var clipped = document
            .Descendants(Svg + "image")
            .Where(image => image.Attribute("clip-path") is not null)
            .ToArray();

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

        return clipped.Length > 0;
    }

    /// <summary>
    /// Removes <c>letter-spacing</c> from text that is not anchored at its start.
    /// SharpVectors draws spaced text one glyph at a time and gives each glyph the text's
    /// own alignment, so with <c>text-anchor="middle"</c> every glyph is centred on the
    /// pen and the pen advances half a glyph, and with <c>end</c> it does not advance at
    /// all: the label piles up in half its width or less. Without the spacing the whole
    /// string is measured and placed at once, which the renderer gets right; the label
    /// is set a little tighter than the author asked, and where they asked. Spacing the
    /// renderer already ignores, such as a length with a unit, is left alone.
    /// </summary>
    private static bool DropAnchoredLetterSpacing(XDocument document)
    {
        var spaced = document
            .Descendants()
            .Where(element =>
                element.Name.Namespace == Svg &&
                (element.Name.LocalName == "text" || element.Name.LocalName == "tspan") &&
                element.Attribute("letter-spacing") is { } spacing &&
                double.TryParse(spacing.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount) &&
                amount != 0 &&
                TextAnchor(element) is "middle" or "end")
            .ToArray();

        foreach (var element in spaced)
        {
            element.Attribute("letter-spacing")!.Remove();
        }

        return spaced.Length > 0;
    }

    /// <summary>
    /// The <c>text-anchor</c> in force on an element: its own, or the nearest ancestor's,
    /// whether written as an attribute or inside <c>style</c>.
    /// </summary>
    private static string TextAnchor(XElement element)
    {
        for (var current = element; current is not null; current = current.Parent)
        {
            if (current.Attribute("text-anchor")?.Value.Trim() is { Length: > 0 } attribute &&
                attribute != "inherit")
            {
                return attribute;
            }

            if (current.Attribute("style")?.Value is { } style)
            {
                foreach (var declaration in style.Split(';'))
                {
                    var colon = declaration.IndexOf(':');
                    if (colon > 0 &&
                        declaration[..colon].Trim() == "text-anchor" &&
                        declaration[(colon + 1)..].Trim() is { Length: > 0 } value &&
                        value != "inherit")
                    {
                        return value;
                    }
                }
            }
        }

        return "start";
    }
}
