using System.IO;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SQLBI.Whiteboard.SmokeTests;

/// <summary>
/// The pan cursors are drawn and encoded here rather than shipped as files, so
/// what is checked is that Windows accepts the result at every size a monitor
/// asks for, and that the hand sits under the hot spot. Whether it looks like a
/// hand has to be judged by eye, with the preview path.
/// </summary>
internal static class GrabCursorSmokeTests
{
    public static void Run(string? previewPath)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Check();
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
            throw new InvalidOperationException("Grab cursor smoke tests failed.", failure);
        }
    }

    private static void Check()
    {
        Assert(GrabCursors.SizeFor(1) == 32, "At 100% the cursor is the 32 pixels Windows draws its own at.");
        Assert(GrabCursors.SizeFor(1.5) == 48, "At 150% it is drawn at 48 pixels rather than stretched.");
        Assert(GrabCursors.SizeFor(0.5) == 32, "It is never drawn smaller than at 100%.");

        foreach (var scale in new[] { 1.0, 1.25, 1.5, 2.0, 3.0 })
        {
            Assert(GrabCursors.Open(scale) is Cursor, $"Windows should load the open hand at {scale:P0}.");
            Assert(GrabCursors.Closed(scale) is Cursor, $"Windows should load the closed hand at {scale:P0}.");
        }

        Assert(
            ReferenceEquals(GrabCursors.Open(1.5), GrabCursors.Open(1.5)),
            "A cursor is built once per size, not on every mouse move.");

        foreach (var closed in new[] { false, true })
        {
            var size = GrabCursors.SizeFor(1.5);
            var pixels = Pixels(GrabCursors.Render(closed, size));
            var (x, y) = GrabCursors.HotSpotFor(size);
            Assert(
                Alpha(pixels, size, x, y) == 255,
                "The hot spot should be on the hand, so the board is held where the cursor shows it.");
            Assert(
                Alpha(pixels, size, 0, 0) == 0 && Alpha(pixels, size, size - 1, size - 1) == 0,
                "The corners should be transparent, or the cursor would be drawn as a square.");
        }

        Assert(
            !Pixels(GrabCursors.Render(closed: false, 32))
                .SequenceEqual(Pixels(GrabCursors.Render(closed: true, 32))),
            "The open and the closed hand should not be the same picture.");

        var file = GrabCursors.Encode(GrabCursors.Render(closed: false, 32), GrabCursors.HotSpotFor(32));
        Assert(
            BitConverter.ToUInt16(file, 2) == 2 &&
            BitConverter.ToUInt16(file, 10) == 16 &&
            BitConverter.ToUInt16(file, 12) == 15,
            "The file should say it is a cursor and carry the hot spot.");
    }

    private static byte[] Pixels(BitmapSource bitmap)
    {
        var straight = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[straight.PixelWidth * straight.PixelHeight * 4];
        straight.CopyPixels(pixels, straight.PixelWidth * 4, 0);
        return pixels;
    }

    private static byte Alpha(byte[] pixels, int size, int x, int y) =>
        pixels[(y * size + x) * 4 + 3];

    // Both hands at three sizes on light and dark, magnified so the pixels
    // can be judged.
    private static void RenderPreview(string path)
    {
        const int Magnify = 4;
        int[] sizes = [32, 48, 64];
        var cell = 64 * Magnify + 16;
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
        using (var context = visual.RenderOpen())
        {
            for (var band = 0; band < 2; band++)
            {
                var background = band == 0 ? Brushes.WhiteSmoke : Brushes.DimGray;
                context.DrawRectangle(background, null, new System.Windows.Rect(0, band * cell * 2, cell * sizes.Length, cell * 2));
                for (var row = 0; row < 2; row++)
                {
                    for (var column = 0; column < sizes.Length; column++)
                    {
                        context.DrawImage(
                            GrabCursors.Render(closed: row == 1, sizes[column]),
                            new System.Windows.Rect(column * cell + 8, (band * 2 + row) * cell + 8, sizes[column] * Magnify, sizes[column] * Magnify));
                    }
                }
            }
        }

        var bitmap = new RenderTargetBitmap(cell * sizes.Length, cell * 4, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
