using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Xml;
using System.Xml.Linq;
using SQLBI.Whiteboard.Core.Model;

namespace SQLBI.Whiteboard;

/// <summary>Decodes normalized diagrams without executing markup or resolving resources.</summary>
internal static class MermaidSvg
{
    private static readonly XNamespace SvgNamespace = "http://www.w3.org/2000/svg";
    private static readonly HashSet<string> Elements = new(StringComparer.Ordinal)
    {
        "svg", "g", "defs", "symbol", "marker", "path", "rect", "circle", "ellipse", "line",
        "polyline", "polygon", "text", "tspan", "title", "desc", "clipPath",
        "linearGradient", "radialGradient", "stop", "filter", "feDropShadow",
    };

    public static DrawingImage Decode(string svg)
    {
        if (svg.Length > MermaidSnapshot.MaximumSvgLength)
            throw new InvalidDataException("The diagram snapshot is too large.");
        using var input = new StringReader(svg);
        using var reader = XmlReader.Create(input, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MermaidSnapshot.MaximumSvgLength,
        });
        int nodes = 0;
        while (reader.Read())
        {
            if (++nodes > 20_000 || reader.Depth > 64 || reader.NodeType == XmlNodeType.ProcessingInstruction)
                throw new InvalidDataException("Unsupported diagram snapshot structure.");
        }

        var root = XElement.Parse(svg);
        if (root.Name != SvgNamespace + "svg") throw new InvalidDataException("Invalid diagram snapshot.");
        foreach (var element in root.DescendantsAndSelf())
        {
            if (element.Name.Namespace != SvgNamespace || !Elements.Contains(element.Name.LocalName))
                throw new InvalidDataException("Unsupported diagram snapshot element.");
            foreach (var attribute in element.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration))
            {
                string name = attribute.Name.LocalName;
                if (name.StartsWith("on", StringComparison.OrdinalIgnoreCase) ||
                    name is "style" or "href" or "src" or "base" ||
                    (attribute.Name.Namespace != XNamespace.None && attribute.Name != XNamespace.Xml + "space"))
                    throw new InvalidDataException("Unsupported diagram snapshot attribute.");
                foreach (Match match in Regex.Matches(attribute.Value, @"url\s*\(([^)]*)\)", RegexOptions.IgnoreCase))
                {
                    string target = match.Groups[1].Value.Trim().Trim('\'', '"');
                    if (!Regex.IsMatch(target, @"\A#[\w.:-]+\z", RegexOptions.CultureInvariant))
                        throw new InvalidDataException("External diagram resources are not supported.");
                }
            }
        }

        double width = (double?)root.Attribute("width") ?? 0;
        double height = (double?)root.Attribute("height") ?? 0;
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0 ||
            width > 20_000 || height > 20_000)
            throw new InvalidDataException("Invalid diagram canvas size.");

        var decoded = SvgImageCodec.Decode(Encoding.UTF8.GetBytes(svg));
        // Invisible SVG geometry can extend the WPF drawing bounds past the
        // declared viewport. Keep the browser's canvas size for layout and fit.
        var viewport = new RectangleGeometry(new Rect(0, 0, width, height));
        var drawing = new DrawingGroup { ClipGeometry = viewport };
        drawing.Children.Add(new GeometryDrawing(Brushes.Transparent, null, viewport));
        // DrawingImage maps its non-zero origin to the target rectangle. Keep
        // that mapping before bounding the declared canvas.
        var positioned = new DrawingGroup
        {
            Transform = new TranslateTransform(-decoded.Drawing.Bounds.X, -decoded.Drawing.Bounds.Y),
        };
        positioned.Children.Add(decoded.Drawing);
        drawing.Children.Add(positioned);
        var image = new DrawingImage(drawing);
        image.Freeze();
        return image;
    }
}
