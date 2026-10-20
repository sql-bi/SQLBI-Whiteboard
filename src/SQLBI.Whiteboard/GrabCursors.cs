using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SQLBI.Whiteboard;

/// <summary>
/// The open hand shown while a mouse drag would pan, and the closed hand shown
/// while it pans. Windows has no grab cursor of its own - its Hand is the
/// pointing finger of a link - so both are drawn here at the pixel size of the
/// monitor and handed to WPF as a .cur file.
/// </summary>
internal static class GrabCursors
{
    // Drawn on the 32-unit grid a cursor has at 100%, then scaled.
    private const double Grid = 32;
    private static readonly Point HotSpot = new(16, 15);
    private static readonly Dictionary<(bool Closed, int Size), Cursor> Cache = [];

    public static Cursor Open(double pixelsPerDip) => Get(closed: false, pixelsPerDip);

    public static Cursor Closed(double pixelsPerDip) => Get(closed: true, pixelsPerDip);

    internal static int SizeFor(double pixelsPerDip) =>
        (int)Math.Round(Grid * Math.Clamp(pixelsPerDip, 1, 4));

    internal static (int X, int Y) HotSpotFor(int size) =>
        ((int)Math.Round(HotSpot.X * size / Grid), (int)Math.Round(HotSpot.Y * size / Grid));

    private static Cursor Get(bool closed, double pixelsPerDip)
    {
        var size = SizeFor(pixelsPerDip);
        if (!Cache.TryGetValue((closed, size), out var cursor))
        {
            using var stream = new MemoryStream(Encode(Render(closed, size), HotSpotFor(size)));
            cursor = new Cursor(stream);
            Cache[(closed, size)] = cursor;
        }

        return cursor;
    }

    internal static BitmapSource Render(bool closed, int size)
    {
        var (shape, creases) = closed ? ClosedHand() : OpenHand();
        var pen = new Pen(Brushes.Black, 1.25)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.PushTransform(new ScaleTransform(size / Grid, size / Grid));
            context.DrawGeometry(Brushes.White, pen, shape);
            foreach (var (from, to) in creases)
            {
                context.DrawLine(pen, from, to);
            }

            context.Pop();
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    // Four fingers spread upward, the thumb out to the left.
    private static (Geometry Shape, (Point From, Point To)[] Creases) OpenHand()
    {
        var shape = Union(
            new RectangleGeometry(new Rect(9, 14, 15, 12), 5, 5),
            Finger(9, 5, 12.75, 16),
            Finger(12.75, 3.5, 16.5, 16),
            Finger(16.5, 4.5, 20.25, 16),
            Finger(20.25, 7, 24, 16),
            Thumb(new Rect(7.6, 13, 3.75, 10), -40, new Point(9.5, 22)));
        return (shape,
        [
            (new Point(12.75, 7), new Point(12.75, 15)),
            (new Point(16.5, 6.5), new Point(16.5, 15)),
            (new Point(20.25, 9), new Point(20.25, 15)),
        ]);
    }

    // The same hand with the fingers curled down to the knuckles.
    private static (Geometry Shape, (Point From, Point To)[] Creases) ClosedHand()
    {
        var shape = Union(
            new RectangleGeometry(new Rect(9, 14, 15, 12), 5, 5),
            Finger(9, 10, 12.75, 17),
            Finger(12.75, 9.5, 16.5, 17),
            Finger(16.5, 10, 20.25, 17),
            Finger(20.25, 11.5, 24, 17),
            Thumb(new Rect(6.5, 15, 3.75, 8), -15, new Point(8.4, 23)));
        return (shape,
        [
            (new Point(12.75, 11.5), new Point(12.75, 15)),
            (new Point(16.5, 11.5), new Point(16.5, 15)),
            (new Point(20.25, 12.5), new Point(20.25, 15)),
        ]);
    }

    private static Geometry Finger(double left, double top, double right, double bottom)
    {
        var radius = (right - left) / 2;
        return new RectangleGeometry(new Rect(left, top, right - left, bottom - top), radius, radius);
    }

    private static Geometry Thumb(Rect bounds, double angle, Point pivot) =>
        new RectangleGeometry(bounds, bounds.Width / 2, bounds.Width / 2)
        {
            Transform = new RotateTransform(angle, pivot.X, pivot.Y),
        };

    private static Geometry Union(params Geometry[] parts) =>
        parts.Aggregate((left, right) =>
            Geometry.Combine(left, right, GeometryCombineMode.Union, null));

    /// <summary>
    /// A single-image .cur: the icon directory, then a 32-bit DIB whose height
    /// counts the color rows and the mask rows together, both bottom-up. The
    /// mask marks the fully transparent pixels, for anything that reads it
    /// instead of the alpha channel.
    /// </summary>
    internal static byte[] Encode(BitmapSource image, (int X, int Y) hotSpot)
    {
        var size = image.PixelWidth;
        var straight = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var stride = size * 4;
        var pixels = new byte[stride * size];
        straight.CopyPixels(pixels, stride, 0);

        var maskStride = (size + 31) / 32 * 4;
        var mask = new byte[maskStride * size];
        var color = new byte[pixels.Length];
        for (var row = 0; row < size; row++)
        {
            var flipped = size - 1 - row;
            Buffer.BlockCopy(pixels, row * stride, color, flipped * stride, stride);
            for (var column = 0; column < size; column++)
            {
                if (pixels[row * stride + column * 4 + 3] == 0)
                {
                    mask[flipped * maskStride + column / 8] |= (byte)(0x80 >> (column % 8));
                }
            }
        }

        const int HeaderSize = 40;
        var imageSize = HeaderSize + color.Length + mask.Length;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)2);
        writer.Write((ushort)1);
        writer.Write((byte)(size >= 256 ? 0 : size));
        writer.Write((byte)(size >= 256 ? 0 : size));
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((ushort)hotSpot.X);
        writer.Write((ushort)hotSpot.Y);
        writer.Write(imageSize);
        writer.Write(22);
        writer.Write(HeaderSize);
        writer.Write(size);
        writer.Write(size * 2);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(0);
        writer.Write(color.Length + mask.Length);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(color);
        writer.Write(mask);
        writer.Flush();
        return stream.ToArray();
    }
}
