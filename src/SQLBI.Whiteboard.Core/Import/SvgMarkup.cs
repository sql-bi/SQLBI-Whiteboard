using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
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

    private static readonly Regex FontFamilyDeclaration = new(
        @"font-family\s*(?:=\s*(?:""(?<list>[^""]*)""|'(?<list>[^']*)')|:\s*(?<list>[^;"">}]*))",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// Every font family the markup names, from <c>font-family</c> attributes and style
    /// declarations, in the order they first appear. Generic families and quotes are
    /// dropped. It searches the text instead of parsing the document, so it also works
    /// on markup that does not parse.
    /// </summary>
    public static IReadOnlyList<string> FontFamilies(byte[] bytes)
    {
        var families = new List<string>();
        foreach (var family in FontFamilyLists(bytes).SelectMany(list => list))
        {
            if (!families.Contains(family, StringComparer.OrdinalIgnoreCase))
            {
                families.Add(family);
            }
        }

        return families;
    }

    /// <summary>
    /// Each <c>font-family</c> declaration as its own list, in its own order of
    /// preference, without generic families. A declaration renders in the intended font
    /// when any family in its list can be found. A declaration that names only generic
    /// families returns an empty list.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> FontFamilyLists(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var lists = new List<IReadOnlyList<string>>();
        foreach (Match match in FontFamilyDeclaration.Matches(Encoding.UTF8.GetString(bytes)))
        {
            var list = new List<string>();
            foreach (var entry in match.Groups["list"].Value.Split(','))
            {
                var family = entry.Trim().Trim('"', '\'').Trim();
                if (family.Length > 0 &&
                    family is not ("serif" or "sans-serif" or "monospace" or "cursive" or "fantasy" or "system-ui" or "inherit") &&
                    !list.Contains(family, StringComparer.OrdinalIgnoreCase))
                {
                    list.Add(family);
                }
            }

            lists.Add(list);
        }

        return lists;
    }

    /// <summary>
    /// Applies every rewrite the renderer needs and returns the markup to hand it.
    /// Markup with nothing to change comes back as the same bytes; markup that does
    /// not parse is left for the renderer to reject in its own words.
    /// </summary>
    public static byte[] Rewrite(byte[] bytes) => Rewrite(bytes, metrics: null);

    /// <summary>
    /// The same, and with <paramref name="metrics"/> also the rewrites that need to
    /// know about fonts: a family that names a weight, and text the renderer would set
    /// wider than kerning does.
    /// </summary>
    public static byte[] Rewrite(byte[] bytes, ISvgFontMetrics? metrics)
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
            if (metrics is not null)
            {
                // Weights first, so the runs are measured in the face that will draw them.
                changed |= SvgText.SplitWeightNames(document, metrics);
                changed |= SvgText.KernRuns(document, metrics);
            }

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
    private static string TextAnchor(XElement element) => Inherited(element, "text-anchor") ?? "start";

    /// <summary>
    /// A presentation property as it applies to an element: from its own <c>style</c>, then
    /// its own attribute, then the same on each ancestor in turn, or null when nothing sets
    /// it. A <c>style</c> declaration takes precedence over an attribute on the same element,
    /// as in CSS.
    /// </summary>
    internal static string? Inherited(XElement element, string property)
    {
        for (var current = element; current is not null; current = current.Parent)
        {
            if (current.Attribute("style")?.Value is { } style)
            {
                foreach (var declaration in style.Split(';'))
                {
                    var colon = declaration.IndexOf(':');
                    if (colon > 0 &&
                        declaration[..colon].Trim() == property &&
                        declaration[(colon + 1)..].Trim() is { Length: > 0 } value &&
                        value != "inherit")
                    {
                        return value;
                    }
                }
            }

            if (current.Attribute(property)?.Value.Trim() is { Length: > 0 } attribute &&
                attribute != "inherit")
            {
                return attribute;
            }
        }

        return null;
    }
}
