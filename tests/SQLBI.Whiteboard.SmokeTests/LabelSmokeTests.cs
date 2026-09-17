using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SQLBI.Whiteboard.Core.Geometry;
using SQLBI.Whiteboard.Core.Model;
using SQLBI.Whiteboard.Core.Viewport;

namespace SQLBI.Whiteboard.SmokeTests;

/// <summary>
/// Core keeps a label's layout size but never works it out: only the framework
/// that draws text can measure it. What is checked here is that side of the
/// bargain - that a measurement comes back, that it grows with the text - and
/// that a label reaches the surface once it has one.
/// </summary>
internal static class LabelSmokeTests
{
    private const int Edge = 240;

    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                CheckMeasuring();
                CheckTheLabelDraws();
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
            throw new InvalidOperationException("Label smoke tests failed.", failure);
        }
    }

    private static void CheckMeasuring()
    {
        Size line = LabelVisual.Measure("Design objects", "Segoe UI", 24, false, false, 1);
        Assert(line.Width > 1 && line.Height > 1, "A line of text measures something.");

        Size twoLines = LabelVisual.Measure("Design objects\nand labels", "Segoe UI", 24, false, false, 1);
        Assert(
            twoLines.Height > line.Height * 1.5,
            "A second line makes the layout about twice as tall.");

        Size bigger = LabelVisual.Measure("Design objects", "Segoe UI", 48, false, false, 1);
        Assert(
            bigger.Width > line.Width && bigger.Height > line.Height,
            "The same text at twice the size measures larger both ways.");

        Size bold = LabelVisual.Measure("Design objects", "Segoe UI", 24, true, false, 1);
        Assert(bold.Width >= line.Width, "Bold is no narrower than the same text plain.");

        Size empty = LabelVisual.Measure(string.Empty, "Segoe UI", 24, false, false, 1);
        Assert(
            empty.Width >= 1 && empty.Height > 1,
            "An empty label keeps a box, or there would be nowhere to put the caret.");
    }

    private static void CheckTheLabelDraws()
    {
        var document = new BoardDocument();
        var pixelsPerDip = 1d;
        Size layout = LabelVisual.Measure("Whiteboard", "Segoe UI", 32, false, false, pixelsPerDip);
        var label = FreeTextBoardObject.Create(
            Guid.NewGuid(),
            document.NextZIndex,
            new PointD(0, 0),
            "Whiteboard",
            "Segoe UI",
            32,
            0xFF1F2937,
            false,
            false,
            false,
            0,
            layout.Width,
            layout.Height);
        document.AddObject(label);

        var blank = Pixels(new BoardDocument());
        Assert(!blank.SequenceEqual(Pixels(document)), "A label reaches the surface.");

        var turned = new BoardDocument();
        turned.AddObject(label.WithAngle(45));
        Assert(
            !Pixels(document).SequenceEqual(Pixels(turned)),
            "A turned label is drawn turned rather than upright.");
    }

    private static byte[] Pixels(BoardDocument document)
    {
        var camera = new Camera2D();
        camera.Resize(Edge, Edge);

        var surface = new BoardSurface
        {
            Width = Edge,
            Height = Edge,
            DrawFrames = false,
        };
        surface.Configure(document, camera);
        surface.Measure(new Size(Edge, Edge));
        surface.Arrange(new Rect(0, 0, Edge, Edge));
        surface.UpdateLayout();

        var bitmap = new RenderTargetBitmap(Edge, Edge, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(surface);
        bitmap.Freeze();
        var pixels = new byte[Edge * Edge * 4];
        bitmap.CopyPixels(pixels, Edge * 4, 0);
        return pixels;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
