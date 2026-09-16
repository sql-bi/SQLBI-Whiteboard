using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SQLBI.Whiteboard.Core.Geometry;
using SQLBI.Whiteboard.Core.Model;
using SQLBI.Whiteboard.Core.Settings;
using SQLBI.Whiteboard.Core.Viewport;

namespace SQLBI.Whiteboard.SmokeTests;

/// <summary>
/// The grid is drawn rather than computed, so what is worth checking here is what
/// comes out of the surface: that a style paints something, that the two styles
/// differ, and that what leaves for a preview is the board without any of it.
/// How faint it looks is a question for eyes, which is what the preview path is
/// for.
/// </summary>
internal static class GridSmokeTests
{
    private const int Edge = 240;

    public static void Run(string? previewPath)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                CheckTheGridDraws();
                CheckThePreviewIgnoresIt();
                if (!string.IsNullOrWhiteSpace(previewPath))
                {
                    RenderPreview(previewPath);
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new InvalidOperationException("Grid smoke tests failed.", failure);
        }
    }

    private static void CheckTheGridDraws()
    {
        var document = Board();
        var blank = Pixels(document, GridStyle.Off, zoom: 1);
        Assert(
            blank.SequenceEqual(Pixels(document, GridStyle.Off, zoom: 1)),
            "One board drawn twice is the same picture, or nothing below could be compared.");
        Assert(
            !blank.SequenceEqual(Pixels(document, GridStyle.Lines, zoom: 1)),
            "Lines should reach the surface, or the preference would be a setting that does nothing.");
        Assert(
            !blank.SequenceEqual(Pixels(document, GridStyle.Dots, zoom: 1)),
            "Dots should reach the surface.");
        Assert(
            !Pixels(document, GridStyle.Lines, zoom: 1)
                .SequenceEqual(Pixels(document, GridStyle.Dots, zoom: 1)),
            "Lines and dots should not come out as the same picture.");

        // The step in the spacing is the whole point of the feature, so it is
        // checked where a person would see it rather than only in the arithmetic.
        Assert(
            !Pixels(document, GridStyle.Lines, zoom: 1)
                .SequenceEqual(Pixels(document, GridStyle.Lines, zoom: 0.2)),
            "Zooming out past the floor should redraw the grid coarser.");
    }

    // A preview renders on a surface of its own and never asks what the
    // preference says, so the grid cannot reach it. That is asserted where it is
    // decided - the surface a preview builds - rather than by rendering one
    // picture twice and comparing it with itself.
    private static void CheckThePreviewIgnoresIt()
    {
        Assert(
            new BoardSurface().GridStyle == GridStyle.Off,
            "A fresh surface draws no grid, which is what keeps it out of every export and preview.");

        var document = Board();
        var preview = BoardPreviewRenderer.Render(document);
        Assert(preview is not null, "A board with ink on it has a preview.");
        Assert(
            preview!.SequenceEqual(BoardPreviewRenderer.Render(document)!),
            "One board previews to the same bytes twice, whatever the grid preference happens to be.");
    }

    /// <summary>
    /// The three pictures side by side, for a person who wants to see how faint
    /// faint is: lines, dots, and lines zoomed out past the step.
    /// </summary>
    private static void RenderPreview(string path)
    {
        var document = Board();
        var panels = new[]
        {
            Bitmap(document, GridStyle.Lines, zoom: 1),
            Bitmap(document, GridStyle.Dots, zoom: 1),
            Bitmap(document, GridStyle.Lines, zoom: 0.2),
        };

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            for (var index = 0; index < panels.Length; index++)
            {
                context.DrawImage(panels[index], new Rect(index * Edge, 0, Edge, Edge));
            }
        }

        var sheet = new RenderTargetBitmap(Edge * panels.Length, Edge, 96, 96, PixelFormats.Pbgra32);
        sheet.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(sheet));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    private static BoardDocument Board()
    {
        var document = new BoardDocument();
        document.AddObject(InkStrokeObject.Create(
            [
                new InkPoint(new PointD(40, 40), 0.5f, 0),
                new InkPoint(new PointD(280, 200), 0.5f, 1),
            ],
            PenStyle.Default,
            document.NextZIndex));
        return document;
    }

    private static byte[] Pixels(BoardDocument document, GridStyle style, double zoom)
    {
        var pixels = new byte[Edge * Edge * 4];
        Bitmap(document, style, zoom).CopyPixels(pixels, Edge * 4, 0);
        return pixels;
    }

    private static RenderTargetBitmap Bitmap(BoardDocument document, GridStyle style, double zoom)
    {
        var camera = new Camera2D();
        camera.Resize(Edge, Edge);
        camera.ZoomAt(new PointD(Edge / 2d, Edge / 2d), zoom);

        var surface = new BoardSurface
        {
            Width = Edge,
            Height = Edge,
            GridStyle = style,
            DrawFrames = false,
        };
        surface.Configure(document, camera);
        surface.Measure(new Size(Edge, Edge));
        surface.Arrange(new Rect(0, 0, Edge, Edge));
        surface.UpdateLayout();

        var bitmap = new RenderTargetBitmap(Edge, Edge, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(surface);
        bitmap.Freeze();
        return bitmap;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
