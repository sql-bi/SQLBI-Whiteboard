using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using SQLBI.Whiteboard.Core.Import;

namespace SQLBI.Whiteboard;

internal static class ClipboardImage
{
    public static string[] GetImportableFiles()
    {
        if (!Clipboard.ContainsFileDropList())
        {
            return [];
        }

        var paths = Clipboard.GetFileDropList()
            .Cast<string>()
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .ToArray();
        return DroppedFileImport.CanImportAny(paths) ? paths : [];
    }

    /// <summary>
    /// The clipboard format names that carry SVG markup. Illustrator, Inkscape, Office,
    /// Figma, and the browsers each publish their own; the last two are what a plain
    /// "copy as SVG" command tends to use.
    /// </summary>
    public static readonly string[] SvgFormats =
    [
        "image/svg+xml",
        "image/svg-xml",
        "image/x-inkscape-svg",
        "Scalable Vector Graphics",
        "SVG",
    ];

    /// <summary>
    /// Reads SVG off the clipboard, either as one of its own formats or as markup that
    /// was copied as text — which is how an SVG built by a DAX measure arrives.
    /// </summary>
    public static byte[]? TryGetSvgBytes()
    {
        var data = Clipboard.GetDataObject();
        if (data is not null)
        {
            foreach (var format in SvgFormats)
            {
                if (!data.GetDataPresent(format, autoConvert: false))
                {
                    continue;
                }

                var bytes = ReadBytes(data.GetData(format)) ?? ReadText(data.GetData(format));
                if (DroppedFileImport.LooksLikeSvg(bytes))
                {
                    return bytes;
                }
            }
        }

        if (!Clipboard.ContainsText(TextDataFormat.UnicodeText))
        {
            return null;
        }

        var text = Clipboard.GetText(TextDataFormat.UnicodeText);
        return DroppedFileImport.LooksLikeSvg(text)
            ? new UTF8Encoding(false).GetBytes(text)
            : null;
    }

    public static byte[]? TryGetEncodedPng() =>
        TryGetPngBytes() ?? TryGetDibPng() ?? TryEncodeWpfImage();

    public static byte[]? TryGetPngBytes()
    {
        var data = Clipboard.GetDataObject();
        if (data is null)
        {
            return null;
        }

        foreach (string format in (string[])["PNG", "image/png"])
        {
            if (!data.GetDataPresent(format, autoConvert: false))
            {
                continue;
            }

            byte[]? bytes = ReadBytes(data.GetData(format));
            if (IsPng(bytes))
            {
                return bytes;
            }
        }

        return null;
    }

    public static byte[]? TryGetDibPng()
    {
        var data = Clipboard.GetDataObject();
        if (data is null)
        {
            return null;
        }

        foreach (string format in (string[])["DeviceIndependentBitmap", "Format17", DataFormats.Dib])
        {
            if (!data.GetDataPresent(format, autoConvert: false))
            {
                continue;
            }

            byte[]? dib = ReadBytes(data.GetData(format));
            byte[]? png = TryDecodeDib(dib);
            if (png is not null)
            {
                return png;
            }
        }

        return null;
    }

    public static byte[]? TryDecodeDib(byte[]? dib)
    {
        byte[]? bmp = WrapDibAsBmp(dib);
        if (bmp is null)
        {
            return null;
        }

        try
        {
            return WpfImageCodec.EncodePng(WpfImageCodec.Decode(bmp));
        }
        catch
        {
            return null;
        }
    }

    internal static byte[]? WrapDibAsBmp(byte[]? dib)
    {
        if (dib is null || dib.Length < 40)
        {
            return null;
        }

        int headerSize = BinaryPrimitives.ReadInt32LittleEndian(dib);
        if (headerSize < 40 || headerSize > dib.Length)
        {
            return null;
        }

        short bitCount = BinaryPrimitives.ReadInt16LittleEndian(dib.AsSpan(14));
        int compression = BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(16));
        int colorsUsed = BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(32));
        int colorTableBytes = 0;
        if (bitCount <= 8)
        {
            int entries = colorsUsed != 0 ? colorsUsed : 1 << bitCount;
            colorTableBytes = entries * 4;
        }
        else if (compression == 3 && headerSize == 40)
        {
            colorTableBytes = 12;
        }

        int pixelOffset = 14 + headerSize + colorTableBytes;
        if (pixelOffset > 14 + dib.Length)
        {
            return null;
        }

        var bmp = new byte[14 + dib.Length];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(2), bmp.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(10), pixelOffset);
        Buffer.BlockCopy(dib, 0, bmp, 14, dib.Length);
        return bmp;
    }

    private static byte[]? TryEncodeWpfImage()
    {
        if (!Clipboard.ContainsImage())
        {
            return null;
        }

        try
        {
            BitmapSource? bitmap = Clipboard.GetImage();
            if (bitmap is not { PixelWidth: > 1, PixelHeight: > 1 })
            {
                return null;
            }

            return WpfImageCodec.EncodePng(bitmap);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsPng(byte[]? bytes) =>
        bytes is { Length: > 8 } &&
        bytes[0] == 0x89 &&
        bytes[1] == (byte)'P' &&
        bytes[2] == (byte)'N' &&
        bytes[3] == (byte)'G';

    private static byte[]? ReadText(object? data) =>
        data is string text ? new UTF8Encoding(false).GetBytes(text) : null;

    private static byte[]? ReadBytes(object? data) => data switch
    {
        MemoryStream stream => CopyStream(stream),
        Stream stream => CopyStream(stream),
        byte[] bytes => bytes,
        _ => null,
    };

    private static byte[] CopyStream(Stream stream)
    {
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
