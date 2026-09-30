using System.Globalization;
using System.Text;
using SQLBI.Whiteboard.Core.Geometry;
using SQLBI.Whiteboard.Core.Model;

namespace SQLBI.Whiteboard.Core.Import;

public sealed partial class MicrosoftWhiteboardExport
{
    /// <summary>
    /// Ink coordinates are stored in 1/128 of a canvas pixel. The group transform says
    /// so on every stroke, and this is the value used when it cannot be read.
    /// </summary>
    private const double InkUnit = 1.0 / 128;

    /// <summary>
    /// Rainbow and Galaxy ink fill the stroke with a picture from Microsoft's servers.
    /// A stroke here has one color, so each becomes the color that stands for it.
    /// </summary>
    private const uint RainbowArgb = 0xFF8B5CF6;
    private const uint GalaxyArgb = 0xFF312E81;

    /// <summary>
    /// An ink group holds one or more strokes. Each is a filled outline, which gives
    /// the width, and a centerline, which gives the points.
    /// </summary>
    private static IEnumerable<MicrosoftWhiteboardItem> ReadInkGroup(Anchor anchor, List<HtmlTag> tags)
    {
        InkGroup? group = null;
        StrokeTransform? transform = null;
        string? pathData = null;
        string? fill = null;
        string? patternId = null;
        var patterns = new Dictionary<string, uint>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            if (tag.Name == "svg" && tag.HasClass("inkGroup"))
            {
                group = InkGroup.From(tag);
            }
            else if (tag.Name == "pattern")
            {
                patternId = tag.Attribute("id");
            }
            else if (tag.Name == "use" && patternId is not null)
            {
                var source = tag.Attribute("href") ?? tag.Attribute("xlink:href") ?? string.Empty;
                patterns[patternId] = source.Contains("galaxy", StringComparison.OrdinalIgnoreCase) ? GalaxyArgb : RainbowArgb;
            }
            else if (tag.Name == "g" && tag.HasClass("inkStroke"))
            {
                transform = StrokeTransform.Parse(tag.Attribute("transform"));
                pathData = null;
                fill = null;
            }
            else if (tag.Name == "path" && transform is not null)
            {
                pathData = tag.Attribute("d");
                fill = tag.Attribute("fill");
            }
            else if (tag.Name == "polyline" && tag.HasClass("inkHitTestOverlay") &&
                     group is not null && transform is not null && pathData is not null)
            {
                foreach (var stroke in ReadStroke(anchor, group, transform, pathData, fill, patterns, tag.Attribute("points")))
                {
                    yield return stroke;
                }

                transform = null;
                pathData = null;
            }
        }
    }

    private static IEnumerable<MicrosoftWhiteboardItem> ReadStroke(
        Anchor anchor,
        InkGroup group,
        StrokeTransform transform,
        string pathData,
        string? fill,
        IReadOnlyDictionary<string, uint> patterns,
        string? points)
    {
        PointD ToCanvas(PointD point) => anchor.ToCanvas(group.ToAnchor(transform.Apply(point)));
        var centerline = ParsePoints(points);
        if (centerline.Count == 0)
        {
            // Older versions of the app wrote some highlighter strokes with an outline
            // and no centerline. The outline is the shape the stroke covered.
            if (TryParseColor(fill, out var color, out var opacity) &&
                OutlinePicture(pathData, ToCanvas, color, opacity) is { } picture)
            {
                yield return picture;
            }

            yield break;
        }

        uint argb;
        double alpha = 1;
        if (fill is not null && fill.TrimStart().StartsWith("url(", StringComparison.OrdinalIgnoreCase))
        {
            var id = fill.Trim()[4..].Trim(')', ' ', '#', '"', '\'');
            argb = patterns.TryGetValue(id, out var effect) ? effect : RainbowArgb;
        }
        else if (!TryParseColor(fill, out argb, out alpha))
        {
            yield break;
        }

        var outline = PathOutline.Parse(pathData);
        var kind = group.Kind switch
        {
            "Highlighter" => PenKind.Highlighter,
            "PenStroke" => PenKind.Pen,
            _ => alpha < 1 ? PenKind.Highlighter : PenKind.Pen,
        };

        // Widths are measured in stroke units, then scaled like the points.
        IReadOnlyList<double> widths;
        double tipWidth = 0;
        if (kind == PenKind.Highlighter)
        {
            (var height, tipWidth) = HighlighterTip(outline, centerline);
            widths = Enumerable.Repeat(height, centerline.Count).ToArray();
        }
        else
        {
            widths = PenWidths(outline, centerline);
        }

        var scale = transform.Scale * anchor.Scale * group.Scale;
        yield return new MicrosoftWhiteboardStroke(
            centerline.Select(ToCanvas).ToArray(),
            widths.Select(width => width * scale).ToArray(),
            argb,
            kind,
            tipWidth * scale);
    }

    /// <summary>
    /// A picture of a filled outline, drawn in canvas pixels inside the box it covers.
    /// The outline is copied as it is, subpath by subpath, so the picture is filled
    /// as the page fills it. These outlines hold straight segments only, and one
    /// with curves is left out rather than drawn with its curves as chords.
    /// </summary>
    private static MicrosoftWhiteboardImage? OutlinePicture(
        string pathData,
        Func<PointD, PointD> toCanvas,
        uint argb,
        double opacity)
    {
        var outline = PathOutline.Parse(pathData);
        if (outline.HasCurves)
        {
            return null;
        }

        var canvas = outline.Subpaths
            .Where(points => points.Count >= 3)
            .Select(points => points.Select(toCanvas).ToArray())
            .ToArray();
        if (canvas.Length == 0)
        {
            return null;
        }

        var left = canvas.Min(points => points.Min(point => point.X));
        var top = canvas.Min(points => points.Min(point => point.Y));
        var width = Math.Max(1, canvas.Max(points => points.Max(point => point.X)) - left);
        var height = Math.Max(1, canvas.Max(points => points.Max(point => point.Y)) - top);

        static string N(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
        var data = new StringBuilder();
        foreach (var points in canvas)
        {
            data.Append('M');
            for (var index = 0; index < points.Length; index++)
            {
                if (index > 0)
                {
                    data.Append(index == 1 ? "L" : " ");
                }

                data.Append(N(points[index].X - left)).Append(',').Append(N(points[index].Y - top));
            }

            data.Append('Z');
        }

        var markup =
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{N(width)}\" height=\"{N(height)}\" " +
            $"viewBox=\"0 0 {N(width)} {N(height)}\"><path fill=\"#{argb & 0xFFFFFF:X6}\" " +
            $"fill-opacity=\"{N(opacity)}\" d=\"{data}\"/></svg>";
        return new MicrosoftWhiteboardImage(
            new RectD(left, top, width, height),
            Encoding.UTF8.GetBytes(markup),
            DroppedFileImport.SvgContentType,
            DroppedFileImport.SvgExtension);
    }

    /// <summary>
    /// A highlighter's tip is an upright rectangle, twice as tall as it is wide on
    /// most boards and nearly square on some. Its height shows twice in the outline:
    /// as the vertical edges wherever the tip stops or turns, and as what the tip adds
    /// to the centerline's height. The edges come mostly from the ends, where pressure
    /// is light, so their upper quartile can be short; the extent comes from the one
    /// point that reaches furthest, so it can be long. The height is the mean of the
    /// two, and the width is what the tip adds to the centerline's width.
    /// </summary>
    private static (double Height, double Width) HighlighterTip(PathOutline outline, IReadOnlyList<PointD> centerline)
    {
        var heights = outline.Segments
            .Where(segment => segment.Start.X == segment.End.X && segment.Start.Y != segment.End.Y)
            .Select(segment => Math.Abs(segment.End.Y - segment.Start.Y))
            .Order()
            .ToArray();
        double? edges = heights.Length > 0 ? heights[heights.Length * 3 / 4] : null;

        var points = outline.Segments.SelectMany(segment => new[] { segment.Start, segment.End }).ToArray();
        double across = 0;
        double down = 0;
        if (points.Length > 0)
        {
            across = points.Max(point => point.X) - points.Min(point => point.X) -
                     (centerline.Max(point => point.X) - centerline.Min(point => point.X));
            down = points.Max(point => point.Y) - points.Min(point => point.Y) -
                   (centerline.Max(point => point.Y) - centerline.Min(point => point.Y));
        }

        var height = (edges, down > 0) switch
        {
            ({ } edge, true) => (edge + down) / 2,
            ({ } edge, false) => edge,
            (null, true) => down,
            _ => NearestEdgeWidth(outline, centerline),
        };
        return (height, Math.Max(0, across));
    }

    /// <summary>
    /// A pen's outline is a band along the centerline with a round join at each point,
    /// plus circles and capsules for short pieces. Every arc in it is drawn around a
    /// centerline point with half the ink's width there as its radius. A point's width
    /// is therefore twice the radius of the arc whose ends lie one radius away from it,
    /// and a point without such an arc takes its width from its neighbours. Measuring
    /// the distance to the nearest edge instead gives small letters too thin, because
    /// the outline of a loop overlaps itself.
    /// </summary>
    private static double[] PenWidths(PathOutline outline, IReadOnlyList<PointD> centerline)
    {
        var result = new double[centerline.Count];
        if (outline.Arcs.Count == 0)
        {
            Array.Fill(result, NearestEdgeWidth(outline, centerline));
            return result;
        }

        var radii = outline.Arcs.Select(arc => arc.Radius).ToArray();
        var widest = radii.Max();
        if (widest <= radii.Min() * 1.05)
        {
            Array.Fill(result, Median(radii) * 2);
            return result;
        }

        var ends = new ArcEnds(outline.Arcs, widest * 2);
        var matched = new bool[centerline.Count];
        for (var index = 0; index < centerline.Count; index++)
        {
            if (ends.RadiusAround(centerline[index]) is { } radius)
            {
                result[index] = radius * 2;
                matched[index] = true;
            }
        }

        FillGaps(result, matched, Median(radii) * 2);
        return result;
    }

    private static void FillGaps(double[] widths, bool[] matched, double fallback)
    {
        var previous = -1;
        for (var index = 0; index <= widths.Length; index++)
        {
            if (index < widths.Length && !matched[index])
            {
                continue;
            }

            for (var gap = previous + 1; gap < index; gap++)
            {
                widths[gap] = (previous, index < widths.Length) switch
                {
                    (< 0, false) => fallback,
                    (< 0, true) => widths[index],
                    (_, false) => widths[previous],
                    _ => widths[previous] + ((widths[index] - widths[previous]) * (gap - previous) / (index - previous)),
                };
            }

            previous = index;
        }
    }

    /// <summary>
    /// For an outline without arcs: twice the median distance from the centerline to
    /// the nearest edge. Only a highlighter without its tip has been seen to need it.
    /// </summary>
    private static double NearestEdgeWidth(PathOutline outline, IReadOnlyList<PointD> centerline)
    {
        if (outline.Segments.Count == 0)
        {
            return 1 / InkUnit;
        }

        var distances = centerline
            .Select(point => outline.Segments.Min(segment => DistanceToSegment(point, segment)))
            .ToArray();
        return Math.Max(Median(distances) * 2, 1);
    }

    private static double DistanceToSegment(PointD point, Segment segment)
    {
        var dx = segment.End.X - segment.Start.X;
        var dy = segment.End.Y - segment.Start.Y;
        var lengthSquared = (dx * dx) + (dy * dy);
        var t = lengthSquared == 0
            ? 0
            : Math.Clamp((((point.X - segment.Start.X) * dx) + ((point.Y - segment.Start.Y) * dy)) / lengthSquared, 0, 1);
        return Distance(point, new PointD(segment.Start.X + (t * dx), segment.Start.Y + (t * dy)));
    }

    private static List<PointD> ParsePoints(string? text)
    {
        var numbers = Numbers(text);
        var points = new List<PointD>(numbers.Count / 2);
        for (var index = 0; index + 1 < numbers.Count; index += 2)
        {
            points.Add(new PointD(numbers[index], numbers[index + 1]));
        }

        return points;
    }

    /// <summary>
    /// The SVG that holds an ink group. Its view box says which stroke coordinate sits
    /// at the anchor.
    /// </summary>
    private sealed record InkGroup(string Kind, PointD ViewOrigin, double ScaleX, double ScaleY)
    {
        public double Scale => Math.Sqrt(Math.Abs(ScaleX * ScaleY));

        public static InkGroup From(HtmlTag tag)
        {
            var classes = tag.Attribute("class")?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
            var kind = classes.FirstOrDefault(name => name is "PenStroke" or "Highlighter" or "Mixed") ?? "Mixed";
            var view = Numbers(tag.Attribute("viewBox"));
            var width = Numbers(tag.Attribute("width")).FirstOrDefault();
            var height = Numbers(tag.Attribute("height")).FirstOrDefault();
            if (view.Count < 4)
            {
                return new InkGroup(kind, default, 1, 1);
            }

            return new InkGroup(
                kind,
                new PointD(view[0], view[1]),
                view[2] > 0 && width > 0 ? width / view[2] : 1,
                view[3] > 0 && height > 0 ? height / view[3] : 1);
        }

        public PointD ToAnchor(PointD point) => new(
            (point.X - ViewOrigin.X) * ScaleX,
            (point.Y - ViewOrigin.Y) * ScaleY);
    }

    private sealed record StrokeTransform(double A, double B, double C, double D, double E, double F)
    {
        public double Scale => Math.Sqrt(Math.Abs((A * D) - (B * C)));

        public static StrokeTransform Parse(string? transform)
        {
            var values = transform is not null && transform.Contains("matrix", StringComparison.Ordinal)
                ? Numbers(transform)
                : [];
            return values.Count == 6
                ? new StrokeTransform(values[0], values[1], values[2], values[3], values[4], values[5])
                : new StrokeTransform(InkUnit, 0, 0, InkUnit, 0, 0);
        }

        public PointD Apply(PointD point) => new(
            (A * point.X) + (C * point.Y) + E,
            (B * point.X) + (D * point.Y) + F);
    }

    private readonly record struct Segment(PointD Start, PointD End);

    private readonly record struct Arc(PointD Start, PointD End, double Radius);

    /// <summary>
    /// The outline of a stroke as straight segments, with every arc in it. An arc is
    /// replaced by its chord, which is close enough to measure against.
    /// </summary>
    private sealed class PathOutline
    {
        public List<Segment> Segments { get; } = [];

        public List<Arc> Arcs { get; } = [];

        /// <summary>
        /// The corners of each subpath in order, from its move to its last segment.
        /// </summary>
        public List<List<PointD>> Subpaths { get; } = [];

        /// <summary>
        /// True when an arc or a curve was replaced by its chord.
        /// </summary>
        public bool HasCurves { get; private set; }

        public static PathOutline Parse(string data)
        {
            var outline = new PathOutline();
            var current = default(PointD);
            PointD? subpathStart = null;
            var index = 0;
            var command = 'M';
            while (index < data.Length)
            {
                var character = data[index];
                if (char.IsAsciiLetter(character))
                {
                    command = character;
                    index++;
                    if (command is 'Z' or 'z' && subpathStart is { } start)
                    {
                        outline.Add(current, start);
                        current = start;
                    }

                    continue;
                }

                if (!IsNumberStart(data, index))
                {
                    index++;
                    continue;
                }

                var relative = char.IsAsciiLetterLower(command);
                switch (char.ToUpperInvariant(command))
                {
                    case 'M':
                    {
                        var point = ReadPoint(data, ref index, relative ? current : default);
                        subpathStart = point;
                        current = point;
                        outline.Subpaths.Add([point]);

                        // Pairs after the first are lines, as SVG defines it.
                        command = relative ? 'l' : 'L';
                        break;
                    }

                    case 'L':
                    {
                        var point = ReadPoint(data, ref index, relative ? current : default);
                        outline.Add(current, point);
                        current = point;
                        break;
                    }

                    case 'A':
                    {
                        var radiusX = ReadNumber(data, ref index);
                        var radiusY = ReadNumber(data, ref index);
                        ReadNumber(data, ref index);
                        ReadNumber(data, ref index);
                        ReadNumber(data, ref index);
                        var point = ReadPoint(data, ref index, relative ? current : default);
                        outline.Arcs.Add(new Arc(current, point, (Math.Abs(radiusX) + Math.Abs(radiusY)) / 2));
                        outline.HasCurves = true;
                        outline.Add(current, point);
                        current = point;
                        break;
                    }

                    default:
                        // Curves do not appear in the ink read so far. Their end point
                        // is the last pair, which keeps the outline connected.
                        var values = new List<double>();
                        while (index < data.Length && !char.IsAsciiLetter(data[index]))
                        {
                            if (IsNumberStart(data, index))
                            {
                                values.Add(ReadNumber(data, ref index));
                            }
                            else
                            {
                                index++;
                            }
                        }

                        outline.HasCurves = true;
                        if (values.Count >= 2)
                        {
                            var end = new PointD(values[^2], values[^1]);
                            end = relative ? current + end : end;
                            outline.Add(current, end);
                            current = end;
                        }

                        break;
                }
            }

            return outline;
        }

        private void Add(PointD start, PointD end)
        {
            Segments.Add(new Segment(start, end));
            if (Subpaths.Count > 0)
            {
                Subpaths[^1].Add(end);
            }
        }

        private static PointD ReadPoint(string data, ref int index, PointD origin)
        {
            var x = ReadNumber(data, ref index);
            var y = ReadNumber(data, ref index);
            return new PointD(origin.X + x, origin.Y + y);
        }

        private static double ReadNumber(string data, ref int index)
        {
            while (index < data.Length && !IsNumberStart(data, index))
            {
                if (char.IsAsciiLetter(data[index]))
                {
                    return 0;
                }

                index++;
            }

            var start = index;
            if (index < data.Length && data[index] is '-' or '+')
            {
                index++;
            }

            var seenDot = false;
            while (index < data.Length)
            {
                var character = data[index];
                if (char.IsAsciiDigit(character))
                {
                    index++;
                }
                else if (character == '.' && !seenDot)
                {
                    seenDot = true;
                    index++;
                }
                else if (character is 'e' or 'E')
                {
                    index++;
                    if (index < data.Length && data[index] is '-' or '+')
                    {
                        index++;
                    }
                }
                else
                {
                    break;
                }
            }

            return double.TryParse(data.AsSpan(start, index - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : 0;
        }
    }

    /// <summary>
    /// The ends of every arc, bucketed by cell, so finding the arcs drawn around a point
    /// looks at the cells next to it rather than at the whole outline.
    /// </summary>
    private sealed class ArcEnds
    {
        private readonly Dictionary<(int X, int Y), List<(PointD End, double Radius)>> _cells = [];
        private readonly double _cellSize;

        public ArcEnds(IEnumerable<Arc> arcs, double cellSize)
        {
            _cellSize = Math.Max(cellSize, 1);
            foreach (var arc in arcs)
            {
                Add(arc.Start, arc.Radius);
                Add(arc.End, arc.Radius);
            }
        }

        /// <summary>
        /// The radius of the arc with an end closest to one radius from the point, when
        /// one is within a tenth of its radius of that.
        /// </summary>
        public double? RadiusAround(PointD point)
        {
            var (column, row) = Cell(point);
            double? best = null;
            var bestError = double.MaxValue;
            for (var x = column - 1; x <= column + 1; x++)
            {
                for (var y = row - 1; y <= row + 1; y++)
                {
                    if (!_cells.TryGetValue((x, y), out var list))
                    {
                        continue;
                    }

                    foreach (var (end, radius) in list)
                    {
                        var error = Math.Abs(Distance(point, end) - radius) / Math.Max(radius, 1);
                        if (error < 0.1 && error < bestError)
                        {
                            bestError = error;
                            best = radius;
                        }
                    }
                }
            }

            return best;
        }

        private void Add(PointD end, double radius)
        {
            var cell = Cell(end);
            if (!_cells.TryGetValue(cell, out var list))
            {
                list = [];
                _cells[cell] = list;
            }

            list.Add((end, radius));
        }

        private (int X, int Y) Cell(PointD point) =>
            ((int)Math.Floor(point.X / _cellSize), (int)Math.Floor(point.Y / _cellSize));
    }
}
