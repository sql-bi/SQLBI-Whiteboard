using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SQLBI.Whiteboard.Core.Export;
using SQLBI.Whiteboard.Core.Geometry;
using SQLBI.Whiteboard.Core.Model;
using SQLBI.Whiteboard.Core.Viewport;

namespace SQLBI.Whiteboard.Export;

/// <summary>
/// An area as slide objects rather than as one picture: images and LiveView
/// frames as pictures, text containers as text boxes with the colors the
/// screen shows, and all the ink as one transparent picture on top. Every
/// rectangle is in the pixel space of the picture the area would otherwise
/// have been, so the two modes place things identically.
/// </summary>
internal static class EditableSlide
{
    private const string PngContentType = "image/png";
    private const string JpegContentType = "image/jpeg";

    private const uint TextBackgroundArgb = 0xFFFCFCFC;
    private const uint TextBorderArgb = 0xFFD6D9DE;
    private const uint TextArgb = 0xFF1F2937;

    /// <summary>
    /// With <paramref name="inkAsStrokes"/> the ink goes out as vector strokes in
    /// their own z-order between the containers, for a writer that draws paths;
    /// otherwise as one transparent picture over everything.
    /// </summary>
    public static IReadOnlyList<SlideElement> Build(
        BoardDocument document,
        ExportArea area,
        int pixelWidth,
        int pixelHeight,
        Func<Guid, ImageSource?>? liveViewImageSourceProvider,
        bool inkAsStrokes = false)
    {
        var camera = BoardRasterizer.CameraFor(area.Bounds, pixelWidth, pixelHeight);
        var elements = new List<SlideElement>();
        var strokes = new List<SlideStroke>();
        foreach (var item in area.Objects)
        {
            if (item is InkStrokeObject stroke)
            {
                if (inkAsStrokes)
                {
                    strokes.Add(Stroke(stroke, camera));
                }

                continue;
            }

            if (strokes.Count > 0)
            {
                elements.Add(InkElement(strokes, pixelWidth, pixelHeight));
                strokes = [];
            }

            var element = item switch
            {
                ImageBoardObject image => ImageElement(document, image, camera),
                LiveViewBoardObject liveView => LiveViewElement(document, liveView, camera, liveViewImageSourceProvider),
                TextBoardObject text => TextElement(text, camera),
                _ => null,
            };
            if (element is not null)
            {
                elements.Add(element);
            }
        }

        if (strokes.Count > 0)
        {
            elements.Add(InkElement(strokes, pixelWidth, pixelHeight));
        }

        if (!inkAsStrokes && area.Objects.OfType<InkStrokeObject>().Any())
        {
            var ink = BoardRasterizer.Render(
                document,
                area.Bounds,
                pixelWidth,
                pixelHeight,
                liveViewImageSourceProvider,
                drawBackground: false,
                objectFilter: static item => item is InkStrokeObject);
            elements.Add(new SlideImageElement(
                new SlideRect(0, 0, pixelWidth, pixelHeight),
                WpfImageCodec.EncodePng(ink),
                PngContentType));
        }

        return elements;
    }

    private static SlideInkElement InkElement(List<SlideStroke> strokes, int pixelWidth, int pixelHeight) =>
        new(new SlideRect(0, 0, pixelWidth, pixelHeight), strokes);

    private static SlideStroke Stroke(InkStrokeObject stroke, Camera2D camera) => new(
        stroke.Points.Select(point =>
        {
            var screen = camera.WorldToScreen(point.Position);
            return new SlidePoint(screen.X, screen.Y, Math.Clamp(point.Pressure, 0f, 1f));
        }).ToArray(),
        stroke.Style.Argb,
        stroke.Style.Thickness * camera.Zoom,
        stroke.Style.Kind switch
        {
            PenKind.Highlighter => SlideStrokeKind.Highlighter,
            PenKind.Calligraphy => SlideStrokeKind.Calligraphy,
            _ => SlideStrokeKind.Pen,
        });

    private static SlideElement? ImageElement(BoardDocument document, ImageBoardObject image, Camera2D camera)
    {
        if (!document.Assets.TryGetValue(image.AssetId, out var asset))
        {
            return null;
        }

        var bounds = ToPage(image.Bounds, camera);
        if (IsPng(asset.Data))
        {
            return new SlideImageElement(bounds, asset.Data, PngContentType);
        }

        if (IsJpeg(asset.Data))
        {
            return new SlideImageElement(bounds, asset.Data, JpegContentType);
        }

        // SVG, BMP, and GIF are drawn at the size they take on the slide, as
        // the clipboard does; PowerPoint gets a PNG it cannot misread.
        try
        {
            var decoded = BoardImageCodec.Decode(asset.Data);
            var shown = new RectD(0, 0, Math.Max(1, bounds.Width), Math.Max(1, bounds.Height));
            var bitmap = BoardImageCodec.Rasterize(decoded, shown);
            return new SlideImageElement(bounds, WpfImageCodec.EncodePng(bitmap), PngContentType);
        }
        catch
        {
            return null;
        }
    }

    private static SlideElement? LiveViewElement(
        BoardDocument document,
        LiveViewBoardObject liveView,
        Camera2D camera,
        Func<Guid, ImageSource?>? liveViewImageSourceProvider)
    {
        var bounds = ToPage(liveView.Bounds, camera);
        if (liveViewImageSourceProvider?.Invoke(liveView.Id) is { } source)
        {
            var frame = source as BitmapSource ?? RenderImageSource(source, bounds);
            return new SlideImageElement(bounds, WpfImageCodec.EncodePng(frame), PngContentType);
        }

        if (liveView.SnapshotAssetId is { } assetId &&
            document.Assets.TryGetValue(assetId, out var snapshot) &&
            IsPng(snapshot.Data))
        {
            return new SlideImageElement(bounds, snapshot.Data, PngContentType);
        }

        return null;
    }

    private static SlideElement TextElement(TextBoardObject text, Camera2D camera)
    {
        var language = TextLanguageRegistry.Resolve(text.LanguageId);
        var analysis = language.Analyze(text.Text, text.Title);
        var visualScale = double.IsFinite(text.VisualScale) && text.VisualScale > 0 ? text.VisualScale : 1;
        var scale = visualScale * camera.Zoom;

        return new SlideTextElement(
            ToPage(text.Bounds, camera),
            analysis.Title,
            Runs(text.Text, analysis.Spans),
            language.FontFamilyName,
            TextContainerVisual.TitleFontSize * scale,
            TextContainerVisual.BodyFontSize * scale,
            TextContainerVisual.ContentPadding * scale,
            TextBackgroundArgb,
            TextBorderArgb,
            TextArgb);
    }

    /// <summary>
    /// The classification spans cover the tokens the language colors; the
    /// text between them is a run in the default color, so the runs together
    /// spell the whole text in order.
    /// </summary>
    private static IReadOnlyList<SlideTextRun> Runs(string text, IReadOnlyList<StyledTextSpan> spans)
    {
        var runs = new List<SlideTextRun>();
        var cursor = 0;
        foreach (var span in spans.OrderBy(span => span.Start))
        {
            var start = Math.Clamp(span.Start, cursor, text.Length);
            var end = Math.Clamp(span.Start + span.Length, start, text.Length);
            if (end <= start)
            {
                continue;
            }

            if (start > cursor)
            {
                runs.Add(new SlideTextRun(text[cursor..start], TextArgb, false, false));
            }

            runs.Add(new SlideTextRun(
                text[start..end],
                Argb(span.Style.Foreground),
                span.Style.FontWeight.ToOpenTypeWeight() >= FontWeights.SemiBold.ToOpenTypeWeight(),
                span.Style.FontStyle == FontStyles.Italic));
            cursor = end;
        }

        if (cursor < text.Length)
        {
            runs.Add(new SlideTextRun(text[cursor..], TextArgb, false, false));
        }

        return runs;
    }

    private static uint Argb(Brush brush) => brush is SolidColorBrush { Color: var color }
        ? ((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B
        : TextArgb;

    private static SlideRect ToPage(RectD bounds, Camera2D camera)
    {
        var topLeft = camera.WorldToScreen(new PointD(bounds.Left, bounds.Top));
        var bottomRight = camera.WorldToScreen(new PointD(bounds.Right, bounds.Bottom));
        return new SlideRect(
            topLeft.X,
            topLeft.Y,
            Math.Max(1, bottomRight.X - topLeft.X),
            Math.Max(1, bottomRight.Y - topLeft.Y));
    }

    private static BitmapSource RenderImageSource(ImageSource source, SlideRect bounds)
    {
        var width = (int)Math.Clamp(Math.Round(bounds.Width), 1, 8192);
        var height = (int)Math.Clamp(Math.Round(bounds.Height), 1, 8192);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawImage(source, new Rect(0, 0, width, height));
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static bool IsPng(byte[] bytes) =>
        bytes.Length > 8 &&
        bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;

    private static bool IsJpeg(byte[] bytes) =>
        bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
}
