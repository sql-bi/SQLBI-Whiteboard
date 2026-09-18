using System.Globalization;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using SQLBI.Whiteboard.Core.Geometry;

namespace SQLBI.Whiteboard.Export;

/// <summary>A4 or US Letter.</summary>
public enum PdfPageSize
{
    A4 = 0,
    Letter = 1,
}

/// <summary>
/// Options for a PDF. FitPageToPicture makes every page take its picture's own aspect
/// ratio (used for the whole-board export), instead of a fixed page size.
/// </summary>
public sealed record PdfOptions(
    PdfPageSize PageSize = PdfPageSize.A4,
    bool Landscape = true,
    bool Footer = true,
    string? BoardName = null,
    bool FitPageToPicture = false);

/// <summary>
/// Writes one page per slide as a PDF, with a bookmark per page so that a reader's
/// outline panel lists the areas. A page with elements is drawn as vector content in the
/// rectangle its picture would have filled; any other page takes the picture. Text is
/// set in fonts read from the Windows fonts folder, because PDFsharp's Core build brings
/// no fonts of its own.
/// </summary>
public static class PdfDocumentWriter
{
    // Page geometry in points (72 per inch).
    private const double Margin = 0.4 * 72;
    private const double HeaderGap = 0.15 * 72;
    private const double FitMargin = 0.1 * 72;
    private const double HeaderFontSize = 11;
    private const double FooterFontSize = 9;

    // A fit-to-picture page maps two pixels to a point (144 dpi). PDF viewers refuse a
    // page side beyond 200 in, so larger pictures are scaled down to that limit.
    private const double PixelsPerPoint = 2;
    private const double MaxPageSide = 14400;

    // Element geometry. Pixel values go through the page's scale; the border is in points.
    private const double TextBoxCornerRadius = 4;
    private const double TextBoxBorderWidth = 0.75;
    private const double TitleGap = 0.4;
    private const double MinNibSize = 0.4;
    private const int PenNibSides = 16;
    private const int HighlighterAlpha = 128;
    private const double MinOutlineWidth = 0.1;

    // An underline thinner than this disappears on a screen at page size, whatever
    // the face asks for.
    private const double MinUnderlineThickness = 0.3;
    private const double Epsilon = 0.000001;

    private const string FontFamily = "Segoe UI";
    private const string Ellipsis = "…";
    private const string Application = "SQLBI Whiteboard";
    private const string DefaultTitle = "Board";

    private static readonly XColor HeaderColor = XColor.FromArgb(0x1F, 0x29, 0x37);
    private static readonly XColor FooterColor = XColor.FromArgb(0x6B, 0x72, 0x80);

    public static void Write(Stream destination, IReadOnlyList<ExportPage> pages, PdfOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(pages);
        options ??= new PdfOptions();

        WindowsFontResolver.Install();

        using var document = new PdfDocument();
        document.Info.Title = string.IsNullOrEmpty(options.BoardName) ? DefaultTitle : options.BoardName;
        document.Info.Creator = Application;

        // Producer is read-only in PDFsharp 6; a value planted in the dictionary survives
        // the save as "PDFsharp x.y.z (Original: SQLBI Whiteboard)".
        document.Info.Elements.SetString("/Producer", Application);

        var headerFont = new XFont(FontFamily, HeaderFontSize, XFontStyleEx.Bold);
        var footerFont = new XFont(FontFamily, FooterFontSize);

        for (var index = 0; index < pages.Count; index++)
        {
            var page = pages[index];
            if (page.PixelWidth <= 0 || page.PixelHeight <= 0)
            {
                throw new ArgumentException($"Page '{page.Title}' has no pixel size.", nameof(pages));
            }

            var pdfPage = options.FitPageToPicture
                ? AddFittedPage(document, page)
                : AddFixedPage(document, page, index + 1, pages.Count, options, headerFont, footerFont);
            document.Outlines.Add(page.Title, pdfPage, opened: true);
        }

        // PDFsharp refuses to save a document without pages.
        if (pages.Count == 0)
        {
            SetSize(document.AddPage(), FixedPageSize(options));
        }

        document.Save(destination, closeStream: false);
    }

    private static PdfPage AddFixedPage(
        PdfDocument document,
        ExportPage page,
        int number,
        int count,
        PdfOptions options,
        XFont headerFont,
        XFont footerFont)
    {
        var size = FixedPageSize(options);
        var pdfPage = document.AddPage();
        SetSize(pdfPage, size);
        using var graphics = XGraphics.FromPdfPage(pdfPage);

        var contentWidth = size.Width - 2 * Margin;
        var headerHeight = headerFont.GetHeight();
        graphics.DrawString(
            FitOnOneLine(graphics, headerFont, page.Title, contentWidth),
            headerFont,
            new XSolidBrush(HeaderColor),
            new XRect(Margin, Margin, contentWidth, headerHeight),
            XStringFormats.TopLeft);

        var top = Margin + headerHeight + HeaderGap;
        var bottom = size.Height - Margin;
        if (options.Footer)
        {
            var footerHeight = footerFont.GetHeight();
            var footer = new XRect(Margin, bottom - footerHeight, contentWidth, footerHeight);
            var brush = new XSolidBrush(FooterColor);
            if (!string.IsNullOrEmpty(options.BoardName))
            {
                var boardName = FitOnOneLine(graphics, footerFont, options.BoardName, contentWidth / 3);
                graphics.DrawString(boardName, footerFont, brush, footer, XStringFormats.BottomLeft);
            }

            var date = DateTime.Today.ToString("d", CultureInfo.CurrentCulture);
            graphics.DrawString(date, footerFont, brush, footer, XStringFormats.BottomCenter);
            graphics.DrawString($"{number} of {count}", footerFont, brush, footer, XStringFormats.BottomRight);
            bottom = footer.Top - HeaderGap;
        }

        DrawPage(graphics, page, new XRect(Margin, top, contentWidth, bottom - top), centerVertically: false);
        return pdfPage;
    }

    private static PdfPage AddFittedPage(PdfDocument document, ExportPage page)
    {
        var scale = Math.Min(1 / PixelsPerPoint, MaxPageSide / Math.Max(page.PixelWidth, page.PixelHeight));
        var size = new XSize(page.PixelWidth * scale, page.PixelHeight * scale);
        var pdfPage = document.AddPage();
        SetSize(pdfPage, size);
        using var graphics = XGraphics.FromPdfPage(pdfPage);

        // A picture only a few pixels across would leave nothing inside the margin.
        var margin = Math.Min(FitMargin, Math.Min(size.Width, size.Height) / 4);
        var box = new XRect(margin, margin, size.Width - 2 * margin, size.Height - 2 * margin);
        DrawPage(graphics, page, box, centerVertically: true);
        return pdfPage;
    }

    // The page's pixels are scaled uniformly into the box and centred across it; the
    // single picture and every element go through the same mapping.
    private static void DrawPage(XGraphics graphics, ExportPage page, XRect box, bool centerVertically)
    {
        var scale = Math.Min(box.Width / page.PixelWidth, box.Height / page.PixelHeight);
        var width = page.PixelWidth * scale;
        var height = page.PixelHeight * scale;
        var x = box.X + (box.Width - width) / 2;
        var y = centerVertically ? box.Y + (box.Height - height) / 2 : box.Y;

        if (page.Elements is null)
        {
            DrawImage(graphics, page.Png, new XRect(x, y, width, height));
            return;
        }

        // Elements are listed back to front, which is also the drawing order.
        var mapping = new PixelMapping(scale, x, y);
        foreach (var element in page.Elements)
        {
            switch (element)
            {
                case SlideImageElement image:
                    DrawImage(graphics, image.Data, mapping.Map(image.Bounds));
                    break;
                case SlideTextElement text:
                    DrawTextBox(graphics, text, mapping);
                    break;
                case SlideShapeElement shape:
                    DrawShape(graphics, shape, mapping);
                    break;
                case SlideLabelElement label:
                    DrawLabel(graphics, label, mapping);
                    break;
                case SlideConnectorElement connector:
                    DrawConnector(graphics, connector, mapping);
                    break;
                case SlideInkElement ink:
                    DrawInk(graphics, ink, mapping);
                    break;
                default:
                    throw new ArgumentException($"Unsupported slide element {element.GetType().Name}.", nameof(page));
            }
        }
    }

    private static void DrawImage(XGraphics graphics, byte[] data, XRect rect)
    {
        using var stream = new MemoryStream(data, writable: false);
        using var image = XImage.FromStream(stream);
        graphics.DrawImage(image, rect);
    }

    /// <summary>
    /// A text container as a text box: the title line, a gap, then the body runs laid out
    /// left to right and wrapped at the right inset. Everything is clipped to the box, as
    /// the board clips it, so text below the bottom is simply not seen.
    /// </summary>
    private static void DrawTextBox(XGraphics graphics, SlideTextElement text, PixelMapping mapping)
    {
        var rect = mapping.Map(text.Bounds);
        var corner = 2 * mapping.Map(TextBoxCornerRadius);
        graphics.DrawRoundedRectangle(
            new XPen(Color(text.BorderArgb), TextBoxBorderWidth),
            new XSolidBrush(Color(text.BackgroundArgb)),
            rect.X,
            rect.Y,
            rect.Width,
            rect.Height,
            corner,
            corner);

        var padding = mapping.Map(text.Padding);
        var left = rect.X + padding;
        var right = rect.Right - padding;
        var bottom = rect.Bottom - padding;
        var y = rect.Y + padding;
        if (right <= left || bottom <= y)
        {
            return;
        }

        var state = graphics.Save();
        graphics.IntersectClip(rect);

        var titleFont = new XFont(FontFamily, mapping.Map(text.TitleFontSize), XFontStyleEx.Bold);
        var bodySize = mapping.Map(text.BodyFontSize);
        var fonts = new Dictionary<XFontStyleEx, XFont>();
        XFont BodyFont(bool bold, bool italic)
        {
            var style = (bold ? XFontStyleEx.Bold : XFontStyleEx.Regular) | (italic ? XFontStyleEx.Italic : XFontStyleEx.Regular);
            if (!fonts.TryGetValue(style, out var font))
            {
                font = new XFont(text.FontFamily, bodySize, style);
                fonts.Add(style, font);
            }

            return font;
        }

        // The title line takes its space even when empty, as the board's does.
        if (text.Title.Length > 0)
        {
            graphics.DrawString(text.Title, titleFont, new XSolidBrush(Color(text.TextArgb, opaque: true)), new XPoint(left, y), XStringFormats.TopLeft);
        }

        y += titleFont.GetHeight() + TitleGap * bodySize;
        var lineHeight = BodyFont(bold: false, italic: false).GetHeight();

        var paragraphs = text.Paragraphs ?? Paragraphs(text.Runs)
            .Select(fragments => new SlideTextParagraph(fragments.Select(fragment => fragment.Run with { Text = fragment.Text }).ToArray()))
            .ToArray();
        foreach (var paragraph in paragraphs)
        {
            if (y >= bottom)
            {
                break;
            }

            var paragraphLeft = left + mapping.Map(paragraph.LeftMargin);
            if (paragraph.Bullet is { } bullet)
            {
                graphics.DrawString(bullet, BodyFont(false, false), new XSolidBrush(Color(text.TextArgb, opaque: true)),
                    new XPoint(paragraphLeft - mapping.Map(paragraph.HangingIndent), y), XStringFormats.TopLeft);
            }

            var x = paragraphLeft;
            foreach (var run in paragraph.Runs)
            {
                var font = BodyFont(run.Bold, run.Italic);
                var brush = new XSolidBrush(Color(run.Argb, opaque: true));
                var rest = run.Text;
                while (rest.Length > 0 && y < bottom)
                {
                    var fit = FitLength(graphics, font, rest, right - x);
                    if (fit == rest.Length)
                    {
                        graphics.DrawString(rest, font, brush, new XPoint(x, y), XStringFormats.TopLeft);
                        x += graphics.MeasureString(rest, font).Width;
                        break;
                    }

                    // Break at the last space that fits. A fragment without one moves to
                    // the next line whole when something precedes it on the line, and is
                    // cut at the last character that fits when it starts the line.
                    var space = rest.LastIndexOf(' ', Math.Max(0, fit - 1));
                    var head = space > 0 ? space : x > paragraphLeft ? 0 : Math.Max(1, fit);
                    if (head > 0)
                    {
                        graphics.DrawString(rest[..head], font, brush, new XPoint(x, y), XStringFormats.TopLeft);
                    }

                    rest = rest[(space > 0 ? space + 1 : head)..];
                    x = paragraphLeft;
                    y += lineHeight;
                }
            }

            y += lineHeight;
        }

        graphics.Restore(state);
    }

    // The runs' text cut into paragraphs at line breaks, each paragraph as the fragments
    // of the runs it spans. A paragraph without fragments is an empty line.
    private static List<List<(string Text, SlideTextRun Run)>> Paragraphs(IReadOnlyList<SlideTextRun> runs)
    {
        var paragraphs = new List<List<(string Text, SlideTextRun Run)>> { new() };
        foreach (var run in runs)
        {
            var lines = run.Text.Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                if (index > 0)
                {
                    paragraphs.Add(new());
                }

                var fragment = lines[index].TrimEnd('\r');
                if (fragment.Length > 0)
                {
                    paragraphs[^1].Add((fragment, run));
                }
            }
        }

        return paragraphs;
    }

    // The longest prefix of the text that fits in the width, in characters; widths grow
    // with the length, so a binary search finds it.
    private static int FitLength(XGraphics graphics, XFont font, string text, double width)
    {
        if (graphics.MeasureString(text, font).Width <= width)
        {
            return text.Length;
        }

        var low = 0;
        var high = text.Length - 1;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (graphics.MeasureString(text[..middle], font).Width <= width)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low;
    }

    /// <summary>
    /// A shape as one path: the outline Core describes, walked as straight lines and
    /// as cubic Béziers where it curves, filled with its tint and stroked with its
    /// outline, and turned about the centre of the box it was drawn in as the board
    /// turns it. The screen is drawn from the same description, so the page and the
    /// board cannot disagree about where an edge of a shape is.
    /// </summary>
    private static void DrawShape(XGraphics graphics, SlideShapeElement shape, PixelMapping mapping)
    {
        ShapeOutline outline = ShapeGeometry.Describe(
            shape.Kind,
            new RectD(shape.Bounds.X, shape.Bounds.Y, shape.Bounds.Width, shape.Bounds.Height));
        var path = new XGraphicsPath { FillMode = XFillMode.Winding };
        PointD cursor = outline.Start;
        foreach (ShapeSegment segment in outline.Segments)
        {
            if (segment.Kind == ShapeSegmentKind.Line)
            {
                path.AddLine(mapping.Map(cursor), mapping.Map(segment.End));
                cursor = segment.End;
                continue;
            }

            foreach ((PointD First, PointD Second, PointD End) curve in ArcCurves(cursor, segment))
            {
                path.AddBezier(
                    mapping.Map(cursor),
                    mapping.Map(curve.First),
                    mapping.Map(curve.Second),
                    mapping.Map(curve.End));
                cursor = curve.End;
            }
        }

        path.CloseFigure();
        var pen = new XPen(Color(shape.OutlineArgb), Math.Max(MinOutlineWidth, mapping.Map(shape.Thickness)))
        {
            LineJoin = XLineJoin.Round,
            LineCap = XLineCap.Round,
        };

        var state = graphics.Save();
        if (shape.AngleDegrees != 0)
        {
            XRect rect = mapping.Map(shape.Bounds);
            graphics.RotateAtTransform(
                shape.AngleDegrees,
                new XPoint(rect.X + (rect.Width / 2), rect.Y + (rect.Height / 2)));
        }

        if (shape.FillArgb is { } fill)
        {
            graphics.DrawPath(pen, new XSolidBrush(Color(fill)), path);
        }
        else
        {
            graphics.DrawPath(pen, path);
        }

        graphics.Restore(state);
    }

    /// <summary>
    /// An arc as cubic Béziers, one for each quarter turn or less, where the error
    /// of the usual approximation is a ten-thousandth of the radius - far below
    /// anything a page shows. The last one ends on the point the outline names, so
    /// rounding never leaves a gap in the path.
    /// </summary>
    private static IEnumerable<(PointD First, PointD Second, PointD End)> ArcCurves(PointD from, ShapeSegment segment)
    {
        var radiusX = Math.Max(Epsilon, segment.RadiusX);
        var radiusY = Math.Max(Epsilon, segment.RadiusY);
        PointD center = segment.Center;
        var start = Math.Atan2((from.Y - center.Y) / radiusY, (from.X - center.X) / radiusX);
        var end = Math.Atan2((segment.End.Y - center.Y) / radiusY, (segment.End.X - center.X) / radiusX);
        if (segment.Clockwise)
        {
            while (end <= start)
            {
                end += 2 * Math.PI;
            }
        }
        else
        {
            while (end >= start)
            {
                end -= 2 * Math.PI;
            }
        }

        var steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(end - start) / (Math.PI / 2)));
        var sweep = (end - start) / steps;
        var reach = 4.0 / 3 * Math.Tan(sweep / 4);
        for (var step = 0; step < steps; step++)
        {
            var fromAngle = start + (step * sweep);
            var toAngle = fromAngle + sweep;
            PointD first = Along(fromAngle, reach);
            PointD second = Along(toAngle, -reach);
            yield return (first, second, step == steps - 1 ? segment.End : At(toAngle));
        }

        PointD At(double angle) => new(
            center.X + (radiusX * Math.Cos(angle)),
            center.Y + (radiusY * Math.Sin(angle)));

        // The point on the curve, moved along the tangent there by the reach a
        // Bézier control point takes.
        PointD Along(double angle, double reachAlongTangent) => new(
            center.X + (radiusX * Math.Cos(angle)) - (reachAlongTangent * radiusX * Math.Sin(angle)),
            center.Y + (radiusY * Math.Sin(angle)) + (reachAlongTangent * radiusY * Math.Cos(angle)));
    }

    /// <summary>
    /// A connector as the line the board draws: the path stopped at the base of its
    /// own arrowhead, so a thick line does not poke through the tip, and the head as
    /// a filled triangle. A curve is the cubic flattened by Core, which is what the
    /// screen draws and what the shortening is measured along.
    /// </summary>
    private static void DrawConnector(XGraphics graphics, SlideConnectorElement connector, PixelMapping mapping)
    {
        PointD start = Point(connector.Start);
        PointD end = Point(connector.End);
        IReadOnlyList<PointD> polyline =
            connector.FirstControl is { } first && connector.SecondControl is { } second
                ? ConnectorGeometry.Flatten(start, Point(first), Point(second), end)
                : [start, end];

        var path = new XGraphicsPath();
        path.AddLines(mapping.Map(ConnectorGeometry.LinePath(connector.Kind, polyline, connector.Thickness)));
        graphics.DrawPath(
            new XPen(Color(connector.Argb), Math.Max(MinOutlineWidth, mapping.Map(connector.Thickness)))
            {
                LineJoin = XLineJoin.Round,
                LineCap = XLineCap.Round,
            },
            path);

        if (ConnectorGeometry.Arrowhead(connector.Kind, polyline, connector.Thickness) is { } head)
        {
            graphics.DrawPolygon(new XSolidBrush(Color(connector.Argb)), mapping.Map(head), XFillMode.Winding);
        }

        static PointD Point(SlidePosition position) => new(position.X, position.Y);
    }

    /// <summary>
    /// A label as text, turned about the centre of its layout rectangle the way the
    /// board turns it. The rectangle was measured by the framework that draws the
    /// screen, so the lines are spread over the height it measured rather than over
    /// this font's own, and a label takes the room on the page that it takes on the
    /// board.
    /// </summary>
    private static void DrawLabel(XGraphics graphics, SlideLabelElement label, PixelMapping mapping)
    {
        var rect = mapping.Map(label.Bounds);
        var size = Math.Max(1, mapping.Map(label.FontSize));
        var font = new XFont(
            label.FontFamily,
            size,
            (label.Bold ? XFontStyleEx.Bold : XFontStyleEx.Regular) |
            (label.Italic ? XFontStyleEx.Italic : XFontStyleEx.Regular));
        var brush = new XSolidBrush(Color(label.Argb, opaque: true));
        var lines = label.Text.Split('\n');
        var lineHeight = rect.Height / lines.Length;

        var state = graphics.Save();
        if (label.AngleDegrees != 0)
        {
            graphics.RotateAtTransform(label.AngleDegrees, new XPoint(rect.X + (rect.Width / 2), rect.Y + (rect.Height / 2)));
        }

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            var top = rect.Y + (index * lineHeight);
            graphics.DrawString(line, font, brush, new XPoint(rect.X, top), XStringFormats.TopLeft);
            if (label.Underline)
            {
                // TopLeft puts the baseline an ascender below the top of the line,
                // and the face says how far under that baseline its underline sits
                // and how thick it is.
                var units = Math.Max(1, font.Metrics.UnitsPerEm);
                var depth = top +
                            (font.GetHeight() * font.CellAscent / Math.Max(1, font.CellSpace)) +
                            (size * Math.Abs(font.Metrics.UnderlinePosition) / units);
                graphics.DrawLine(
                    new XPen(
                        Color(label.Argb, opaque: true),
                        Math.Max(MinUnderlineThickness, size * Math.Abs(font.Metrics.UnderlineThickness) / units)),
                    rect.X,
                    depth,
                    rect.X + graphics.MeasureString(line, font).Width,
                    depth);
            }
        }

        graphics.Restore(state);
    }

    /// <summary>
    /// Every stroke is one filled path: the sweep of its nib along its points, each segment
    /// as the convex hull of the nib at both ends. One fill with the winding rule keeps the
    /// overlaps between segments from darkening a translucent stroke.
    /// </summary>
    private static void DrawInk(XGraphics graphics, SlideInkElement ink, PixelMapping mapping)
    {
        foreach (var stroke in ink.Strokes)
        {
            if (stroke.Points.Count == 0)
            {
                continue;
            }

            var path = new XGraphicsPath { FillMode = XFillMode.Winding };
            var segments = 0;
            for (var index = 0; index + 1 < stroke.Points.Count; index++)
            {
                var from = stroke.Points[index];
                var to = stroke.Points[index + 1];
                if (from.X == to.X && from.Y == to.Y)
                {
                    continue;
                }

                path.AddPolygon(mapping.Map(ConvexHull([.. Nib(stroke, from), .. Nib(stroke, to)])));
                segments++;
            }

            if (segments == 0)
            {
                path.AddPolygon(mapping.Map(Nib(stroke, stroke.Points[0])));
            }

            // WPF renders an opaque highlighter at half opacity.
            var argb = stroke.Argb;
            if (stroke.Kind == SlideStrokeKind.Highlighter && argb >> 24 == 0xFF)
            {
                argb = (uint)HighlighterAlpha << 24 | argb & 0xFFFFFF;
            }

            graphics.DrawPath(new XSolidBrush(Color(argb)), path);
        }
    }

    // The nib as a polygon around a point, in page pixels. The pressure-driven size is
    // Thickness at the pressure WPF treats as normal (0.5), and never collapses to nothing.
    private static XPoint[] Nib(SlideStroke stroke, SlidePoint point)
    {
        var pressed = Math.Max(MinNibSize, stroke.Thickness * 2 * point.Pressure);
        switch (stroke.Kind)
        {
            case SlideStrokeKind.Highlighter:
                return Rectangle(point, 4 * stroke.Thickness, 2 * stroke.Thickness);
            case SlideStrokeKind.Calligraphy:
                return Rectangle(point, 0.65 * pressed, 3 * pressed);
            default:
                var radius = pressed / 2;
                var polygon = new XPoint[PenNibSides];
                for (var index = 0; index < PenNibSides; index++)
                {
                    var angle = 2 * Math.PI * index / PenNibSides;
                    polygon[index] = new XPoint(point.X + radius * Math.Cos(angle), point.Y + radius * Math.Sin(angle));
                }

                return polygon;
        }

        static XPoint[] Rectangle(SlidePoint center, double width, double height) =>
        [
            new(center.X - width / 2, center.Y - height / 2),
            new(center.X + width / 2, center.Y - height / 2),
            new(center.X + width / 2, center.Y + height / 2),
            new(center.X - width / 2, center.Y + height / 2),
        ];
    }

    // Andrew's monotone chain. The hull of the nib at both ends of a segment is the area
    // the nib sweeps between them, since a nib is convex.
    private static XPoint[] ConvexHull(XPoint[] points)
    {
        Array.Sort(points, (a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
        var hull = new List<XPoint>(points.Length + 1);
        foreach (var point in points)
        {
            AddTurningLeft(hull, point, keep: 1);
        }

        // The upper chain must not pop its way back into the lower one.
        var lowerCount = hull.Count;
        for (var index = points.Length - 2; index >= 0; index--)
        {
            AddTurningLeft(hull, points[index], keep: lowerCount);
        }

        // The chain ends where it started.
        hull.RemoveAt(hull.Count - 1);
        return [.. hull];

        static void AddTurningLeft(List<XPoint> hull, XPoint point, int keep)
        {
            while (hull.Count > keep && Cross(hull[^2], hull[^1], point) <= 0)
            {
                hull.RemoveAt(hull.Count - 1);
            }

            hull.Add(point);
        }

        static double Cross(XPoint o, XPoint a, XPoint b) =>
            (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
    }

    // Text carries no alpha on the board, so the color's own alpha is dropped for it.
    private static XColor Color(uint argb, bool opaque = false) => XColor.FromArgb(
        opaque ? 0xFF : (int)(argb >> 24),
        (int)(argb >> 16 & 0xFF),
        (int)(argb >> 8 & 0xFF),
        (int)(argb & 0xFF));

    // The header is one line: a title wider than the page is cut and ended with an
    // ellipsis rather than wrapped, so that the picture keeps its space.
    private static string FitOnOneLine(XGraphics graphics, XFont font, string text, double width)
    {
        var line = text.ReplaceLineEndings(" ");
        if (graphics.MeasureString(line, font).Width <= width)
        {
            return line;
        }

        var low = 0;
        var high = line.Length;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (graphics.MeasureString(Cut(line, middle), font).Width <= width)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return Cut(line, low);

        static string Cut(string line, int length)
        {
            if (length > 0 && char.IsHighSurrogate(line[length - 1]))
            {
                length--;
            }

            return line[..length].TrimEnd() + Ellipsis;
        }
    }

    private static XSize FixedPageSize(PdfOptions options)
    {
        var size = PageSizeConverter.ToSize(options.PageSize == PdfPageSize.Letter ? PageSize.Letter : PageSize.A4);
        return options.Landscape ? new XSize(size.Height, size.Width) : size;
    }

    // Width and height are set directly rather than through Orientation, so that the
    // media box holds the landscape size instead of a rotated portrait one.
    private static void SetSize(PdfPage page, XSize size)
    {
        page.Width = XUnit.FromPoint(size.Width);
        page.Height = XUnit.FromPoint(size.Height);
    }

    /// <summary>
    /// Where a page's pixels land on the PDF page: Scale is points per pixel, and the
    /// origin is where pixel (0,0) falls.
    /// </summary>
    private readonly record struct PixelMapping(double Scale, double OriginX, double OriginY)
    {
        public double Map(double length) => length * Scale;

        public XPoint Map(XPoint point) => new(OriginX + point.X * Scale, OriginY + point.Y * Scale);

        public XPoint Map(PointD point) => new(OriginX + point.X * Scale, OriginY + point.Y * Scale);

        public XPoint[] Map(IReadOnlyList<PointD> points)
        {
            var mapped = new XPoint[points.Count];
            for (var index = 0; index < points.Count; index++)
            {
                mapped[index] = Map(points[index]);
            }

            return mapped;
        }

        public XPoint[] Map(XPoint[] points)
        {
            var mapped = new XPoint[points.Length];
            for (var index = 0; index < points.Length; index++)
            {
                mapped[index] = Map(points[index]);
            }

            return mapped;
        }

        public XRect Map(SlideRect rect) => new(OriginX + rect.X * Scale, OriginY + rect.Y * Scale, rect.Width * Scale, rect.Height * Scale);
    }

    /// <summary>
    /// PDFsharp's Core build resolves no fonts by itself, and the Windows resolver it
    /// offers knows Arial but not Segoe UI, so the faces are read from the Windows fonts
    /// folder here: Segoe UI for the page furniture and the text boxes, Consolas for the
    /// monospace ones, the curated list a label can be written in, and Segoe UI again
    /// for any other family a board names. PDFsharp accepts one resolver per process,
    /// so a single instance is installed once and shared by every Write.
    /// </summary>
    private sealed class WindowsFontResolver : IFontResolver
    {
        private static readonly Lazy<WindowsFontResolver> Installed = new(() =>
        {
            var resolver = new WindowsFontResolver();
            GlobalFontSettings.FontResolver = resolver;
            return resolver;
        });

        // Arial, Times New Roman, and Courier New stand in on a Windows without the
        // family itself, such as a Server Core. Each family lists regular, bold,
        // italic, and bold italic; a family Windows ships no italic file for names
        // the upright face twice, because an upright label reads better than a
        // sloped substitute.
        private static readonly string[] ArialFiles = ["arial.ttf", "arialbd.ttf", "ariali.ttf", "arialbi.ttf"];
        private static readonly string[] TimesFiles = ["times.ttf", "timesbd.ttf", "timesi.ttf", "timesbi.ttf"];
        private static readonly string[] CourierFiles = ["cour.ttf", "courbd.ttf", "couri.ttf", "courbi.ttf"];

        private static readonly FontFiles SegoeUI = new(
            "SegoeUI",
            ["segoeui.ttf", "segoeuib.ttf", "segoeuii.ttf", "segoeuiz.ttf"],
            ArialFiles);

        private static readonly FontFiles Consolas = new(
            "Consolas",
            ["consola.ttf", "consolab.ttf", "consolai.ttf", "consolaz.ttf"],
            CourierFiles);

        /// <summary>
        /// The families a label offers, by the name a board saves. Cascadia Mono is
        /// the one Windows ships only as a variable font, whose bold and italic are
        /// axes rather than files, so its faces are Consolas: the same monospace
        /// widths, and four real faces to embed.
        /// </summary>
        private static readonly Dictionary<string, FontFiles> Families = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Segoe UI"] = SegoeUI,
            ["Consolas"] = Consolas,
            ["Cascadia Mono"] = Consolas,
            ["Calibri"] = new("Calibri", ["calibri.ttf", "calibrib.ttf", "calibrii.ttf", "calibriz.ttf"], ArialFiles),
            ["Arial"] = new("Arial", ArialFiles, ArialFiles),
            ["Georgia"] = new("Georgia", ["georgia.ttf", "georgiab.ttf", "georgiai.ttf", "georgiaz.ttf"], TimesFiles),
            ["Times New Roman"] = new("TimesNewRoman", TimesFiles, TimesFiles),
            ["Comic Sans MS"] = new("ComicSansMS", ["comic.ttf", "comicbd.ttf", "comici.ttf", "comicz.ttf"], ArialFiles),
            ["Segoe Print"] = new("SegoePrint", ["segoepr.ttf", "segoeprb.ttf", "segoepr.ttf", "segoeprb.ttf"], ArialFiles),
        };

        private static readonly Dictionary<string, Lazy<byte[]>> Faces = Families.Values
            .DistinctBy(family => family.Name)
            .SelectMany(family => family.Faces())
            .ToDictionary(face => face.Name, face => face.Bytes, StringComparer.Ordinal);

        public static void Install() => _ = Installed.Value;

        public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
        {
            var family = Families.GetValueOrDefault(familyName ?? string.Empty, SegoeUI);
            return new FontResolverInfo(family.FaceName(bold, italic));
        }

        public byte[]? GetFont(string faceName) =>
            Faces.TryGetValue(faceName, out var bytes) ? bytes.Value : null;

        private sealed record FontFiles(string Name, string[] FileNames, string[] FallbackFileNames)
        {
            private static readonly string[] StyleSuffixes = ["", "#b", "#i", "#bi"];

            public string FaceName(bool bold, bool italic) => Name + StyleSuffixes[(bold ? 1 : 0) + (italic ? 2 : 0)];

            public IEnumerable<(string Name, Lazy<byte[]> Bytes)> Faces()
            {
                for (var index = 0; index < StyleSuffixes.Length; index++)
                {
                    var fileName = FileNames[index];
                    var fallbackFileName = FallbackFileNames[index];
                    yield return (Name + StyleSuffixes[index], new Lazy<byte[]>(() => ReadFont(fileName, fallbackFileName)));
                }
            }

            private static byte[] ReadFont(string fileName, string fallbackFileName)
            {
                var folder = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
                var path = Path.Combine(folder, fileName);
                return File.ReadAllBytes(File.Exists(path) ? path : Path.Combine(folder, fallbackFileName));
            }
        }
    }
}
