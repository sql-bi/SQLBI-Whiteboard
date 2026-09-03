using System.Globalization;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

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

    private const string FontFamily = "Segoe UI";
    private const string MonospaceFontFamily = "Consolas";
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

        foreach (var paragraph in Paragraphs(text.Runs))
        {
            if (y >= bottom)
            {
                break;
            }

            var x = left;
            foreach (var (fragment, run) in paragraph)
            {
                var font = BodyFont(run.Bold, run.Italic);
                var brush = new XSolidBrush(Color(run.Argb, opaque: true));
                var rest = fragment;
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
                    var head = space > 0 ? space : x > left ? 0 : Math.Max(1, fit);
                    if (head > 0)
                    {
                        graphics.DrawString(rest[..head], font, brush, new XPoint(x, y), XStringFormats.TopLeft);
                    }

                    rest = rest[(space > 0 ? space + 1 : head)..];
                    x = left;
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
    /// monospace ones, and Segoe UI again for any other family a board names. PDFsharp
    /// accepts one resolver per process, so a single instance is installed once and
    /// shared by every Write.
    /// </summary>
    private sealed class WindowsFontResolver : IFontResolver
    {
        private static readonly Lazy<WindowsFontResolver> Installed = new(() =>
        {
            var resolver = new WindowsFontResolver();
            GlobalFontSettings.FontResolver = resolver;
            return resolver;
        });

        // Arial and Courier New stand in on a Windows without Segoe UI or Consolas, such
        // as a Server Core. Each family lists regular, bold, italic, and bold italic.
        private static readonly FontFiles SegoeUI = new(
            "SegoeUI",
            ["segoeui.ttf", "segoeuib.ttf", "segoeuii.ttf", "segoeuiz.ttf"],
            ["arial.ttf", "arialbd.ttf", "ariali.ttf", "arialbi.ttf"]);

        private static readonly FontFiles Consolas = new(
            "Consolas",
            ["consola.ttf", "consolab.ttf", "consolai.ttf", "consolaz.ttf"],
            ["cour.ttf", "courbd.ttf", "couri.ttf", "courbi.ttf"]);

        private static readonly Dictionary<string, Lazy<byte[]>> Faces = new[] { SegoeUI, Consolas }
            .SelectMany(family => family.Faces())
            .ToDictionary(face => face.Name, face => face.Bytes, StringComparer.Ordinal);

        public static void Install() => _ = Installed.Value;

        public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
        {
            var family = string.Equals(familyName, MonospaceFontFamily, StringComparison.OrdinalIgnoreCase) ? Consolas : SegoeUI;
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
