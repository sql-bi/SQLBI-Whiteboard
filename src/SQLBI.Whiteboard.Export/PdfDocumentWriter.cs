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
/// Writes one picture per page as a PDF, with a bookmark per page so that a reader's
/// outline panel lists the areas. Text is set in Segoe UI read from the Windows fonts
/// folder, because PDFsharp's Core build brings no fonts of its own.
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

        DrawPicture(graphics, page, new XRect(Margin, top, contentWidth, bottom - top), centerVertically: false);
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
        DrawPicture(graphics, page, box, centerVertically: true);
        return pdfPage;
    }

    private static void DrawPicture(XGraphics graphics, ExportPage page, XRect box, bool centerVertically)
    {
        var scale = Math.Min(box.Width / page.PixelWidth, box.Height / page.PixelHeight);
        var width = page.PixelWidth * scale;
        var height = page.PixelHeight * scale;
        var x = box.X + (box.Width - width) / 2;
        var y = centerVertically ? box.Y + (box.Height - height) / 2 : box.Y;

        using var png = new MemoryStream(page.Png, writable: false);
        using var image = XImage.FromStream(png);
        graphics.DrawImage(image, x, y, width, height);
    }

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
    /// PDFsharp's Core build resolves no fonts by itself, and the Windows resolver it
    /// offers knows Arial but not Segoe UI, so the two faces are read from the Windows
    /// fonts folder here. PDFsharp accepts one resolver per process, so a single instance
    /// is installed once and shared by every Write.
    /// </summary>
    private sealed class WindowsFontResolver : IFontResolver
    {
        private const string RegularFace = "SegoeUI";
        private const string BoldFace = "SegoeUI#b";

        private static readonly Lazy<WindowsFontResolver> Installed = new(() =>
        {
            var resolver = new WindowsFontResolver();
            GlobalFontSettings.FontResolver = resolver;
            return resolver;
        });

        // Arial stands in on a Windows without Segoe UI, such as a Server Core.
        private static readonly Lazy<byte[]> Regular = new(() => ReadFont("segoeui.ttf", "arial.ttf"));
        private static readonly Lazy<byte[]> Bold = new(() => ReadFont("segoeuib.ttf", "arialbd.ttf"));

        public static void Install() => _ = Installed.Value;

        public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) =>
            string.Equals(familyName, FontFamily, StringComparison.OrdinalIgnoreCase)
                ? new FontResolverInfo(bold ? BoldFace : RegularFace, false, italic)
                : null;

        public byte[]? GetFont(string faceName) => faceName switch
        {
            RegularFace => Regular.Value,
            BoldFace => Bold.Value,
            _ => null,
        };

        private static byte[] ReadFont(string fileName, string fallbackFileName)
        {
            var folder = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            var path = Path.Combine(folder, fileName);
            return File.ReadAllBytes(File.Exists(path) ? path : Path.Combine(folder, fallbackFileName));
        }
    }
}
