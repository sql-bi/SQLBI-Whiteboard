using System.IO;
using System.Text;
using System.Windows.Media;
using SQLBI.Whiteboard.Core.Export;
using SQLBI.Whiteboard.Core.Geometry;
using SQLBI.Whiteboard.Core.Model;
using SQLBI.Whiteboard.Core.Settings;

namespace SQLBI.Whiteboard.Export;

internal readonly record struct ExportProgress(int Done, int Total, string Message);

/// <summary>
/// Turns areas into pages and pages into a file. Rendering happens on the UI
/// thread one page at a time; writing the file does not.
/// </summary>
internal static class BoardExporter
{
    /// <summary>
    /// Twice a full-HD slide, so a projector never sees the export's pixels.
    /// </summary>
    public const int PageBoxWidth = 3840;
    public const int PageBoxHeight = 2160;

    /// <summary>
    /// A whole board on one PDF page is read by zooming, so it is rendered
    /// larger than a slide would be. Six thousand pixels on the longer edge is
    /// a 144 MB transient bitmap, the most this is willing to ask for.
    /// </summary>
    public const int PosterBoxEdge = 6000;

    public static IReadOnlyList<ExportArea> Areas(
        BoardDocument document,
        ExportSettings settings,
        Func<BoardObject, string?>? titleResolver)
    {
        if (settings.PageModel == ExportPageModel.WholeBoard)
        {
            return document.ContentBounds is { } content
                ? [new ExportArea(
                    1,
                    content,
                    document.Objects,
                    null,
                    BoardPartitioner.TextScaleFor(content, settings.LayoutOptions))]
                : [];
        }

        return BoardPartitioner.Partition(document, settings.LayoutOptions, titleResolver);
    }

    public static bool HasOverview(ExportSettings settings, IReadOnlyList<ExportArea> areas) =>
        settings.IncludeOverview &&
        settings.PageModel == ExportPageModel.OnePerArea &&
        areas.Count > 1;

    public static async Task ExportAsync(
        BoardDocument document,
        ExportSettings settings,
        IReadOnlyList<ExportArea> areas,
        string filePath,
        string boardName,
        Func<Guid, ImageSource?>? liveViewImageSourceProvider,
        Func<BoardObject, string?>? titleResolver,
        IProgress<ExportProgress> progress,
        CancellationToken cancellationToken)
    {
        var pages = new List<ExportPage>();
        var overview = HasOverview(settings, areas);
        var total = areas.Count + (overview ? 1 : 0);
        var pdf = settings.Format == ExportFormat.Pdf;
        var poster = pdf && settings.PageModel == ExportPageModel.WholeBoard;
        var unit = pdf ? "page" : "slide";

        if (overview && document.ContentBounds is { } content)
        {
            progress.Report(new ExportProgress(0, total, "Rendering the overview"));
            await Task.Yield();
            pages.Add(RenderOverview(document, content, areas, liveViewImageSourceProvider));
        }

        foreach (var area in areas)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report(new ExportProgress(
                pages.Count,
                total,
                $"Rendering {unit} {pages.Count + 1} of {total}"));
            await Task.Yield();
            pages.Add(RenderArea(
                document,
                area,
                settings,
                areas.Count == 1 ? boardName : null,
                liveViewImageSourceProvider,
                titleResolver,
                poster ? PosterBoxEdge : PageBoxWidth,
                poster ? PosterBoxEdge : PageBoxHeight));
        }

        progress.Report(new ExportProgress(total, total, "Writing the file"));
        Action<Stream> write = pdf
            ? stream => PdfDocumentWriter.Write(stream, pages, new PdfOptions(
                settings.PageSize == ExportPageSize.Letter ? PdfPageSize.Letter : PdfPageSize.A4,
                Landscape: true,
                Footer: settings.IncludeFooter,
                BoardName: boardName,
                FitPageToPicture: poster))
            : stream => PptxDeckWriter.Write(stream, pages, new DeckOptions(
                settings.SlideAspect == ExportSlideAspect.Standard ? SlideAspect.Standard : SlideAspect.Wide));
        await Task.Run(() => WriteAtomically(filePath, write), cancellationToken);
    }

    public static string PageTitle(ExportArea area, string? singlePageTitle) =>
        area.Title ?? singlePageTitle ?? $"Area {area.Number}";

    private static ExportPage RenderOverview(
        BoardDocument document,
        RectD content,
        IReadOnlyList<ExportArea> areas,
        Func<Guid, ImageSource?>? liveViewImageSourceProvider)
    {
        var (width, height) = BoardRasterizer.FitPixelSize(content, PageBoxWidth, PageBoxHeight);
        var scale = Math.Max(1, width / 1000d);
        var bitmap = BoardRasterizer.Render(
            document,
            content,
            width,
            height,
            liveViewImageSourceProvider,
            overlay: (context, camera) => ExportOverlay.DrawAreas(context, camera, areas, scale));

        var notes = new StringBuilder();
        foreach (var area in areas)
        {
            notes.Append(area.Number).Append(". ").AppendLine(PageTitle(area, null));
        }

        return new ExportPage("Overview", notes.ToString().TrimEnd(), WpfImageCodec.EncodePng(bitmap), width, height);
    }

    private static ExportPage RenderArea(
        BoardDocument document,
        ExportArea area,
        ExportSettings settings,
        string? singlePageTitle,
        Func<Guid, ImageSource?>? liveViewImageSourceProvider,
        Func<BoardObject, string?>? titleResolver,
        int boxWidth,
        int boxHeight)
    {
        var (width, height) = BoardRasterizer.FitPixelSize(area.Bounds, boxWidth, boxHeight);
        var bitmap = BoardRasterizer.Render(
            document,
            area.Bounds,
            width,
            height,
            liveViewImageSourceProvider);
        var editable = settings.Format == ExportFormat.PowerPoint &&
                       settings.SlideContent == ExportSlideContent.Editable;

        return new ExportPage(
            PageTitle(area, singlePageTitle),
            settings.IncludeNotes ? Notes(area, titleResolver) : null,
            WpfImageCodec.EncodePng(bitmap),
            width,
            height,
            editable
                ? EditableSlide.Build(document, area, width, height, liveViewImageSourceProvider)
                : null);
    }

    /// <summary>
    /// Every text container in the area, so that DAX and SQL can be copied
    /// from the notes even though the slide itself is a picture.
    /// </summary>
    private static string? Notes(ExportArea area, Func<BoardObject, string?>? titleResolver)
    {
        var notes = new StringBuilder();
        foreach (var text in area.Texts)
        {
            if (string.IsNullOrWhiteSpace(text.Text))
            {
                continue;
            }

            if (notes.Length > 0)
            {
                notes.AppendLine();
            }

            var title = titleResolver?.Invoke(text) ?? text.Title;
            if (!string.IsNullOrWhiteSpace(title))
            {
                notes.AppendLine(title.Trim());
            }

            notes.AppendLine(text.Text.TrimEnd());
        }

        return notes.Length == 0 ? null : notes.ToString().TrimEnd();
    }

    /// <summary>
    /// Written beside the target and moved into place, so a failure half way
    /// through never leaves a truncated deck where a good one was.
    /// </summary>
    private static void WriteAtomically(string filePath, Action<Stream> write)
    {
        var temporaryPath = filePath + ".tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                write(stream);
            }

            File.Move(temporaryPath, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
