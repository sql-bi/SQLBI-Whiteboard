using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using SQLBI.Whiteboard.Core.Import;

namespace SQLBI.Whiteboard;

internal static class ClipboardImageReader
{
    public static bool TryRead(IDataObject? dataObject, out byte[] pngBytes)
    {
        pngBytes = [];
        if (dataObject is null)
        {
            return false;
        }

        IReadOnlyList<ClipboardImageFormat> formats =
            ClipboardImageImport.PreferredFormats(dataObject.GetFormats(autoConvert: false));
        foreach (var format in formats)
        {
            try
            {
                if (TryRead(dataObject, format, out pngBytes))
                {
                    return true;
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Debug.WriteLine(
                    $"[Clipboard] Could not decode image format '{format.Name}': {exception.Message}");
            }
        }

        return false;
    }

    private static bool TryRead(
        IDataObject dataObject,
        ClipboardImageFormat format,
        out byte[] pngBytes)
    {
        pngBytes = [];
        var data = dataObject.GetData(format.Name, autoConvert: false);
        return format.Kind switch
        {
            ClipboardImageDataKind.Bitmap =>
                TryEncodeBitmap(data as BitmapSource, out pngBytes),
            ClipboardImageDataKind.Encoded =>
                TryNormalizeImage(ReadBytes(data), out pngBytes),
            ClipboardImageDataKind.Dib =>
                TryNormalizeDib(ReadBytes(data), out pngBytes),
            ClipboardImageDataKind.FileDrop =>
                TryReadImageFile(data as string[], out pngBytes),
            _ => false,
        };
    }

    private static bool TryEncodeBitmap(BitmapSource? bitmap, out byte[] pngBytes)
    {
        if (bitmap is null)
        {
            pngBytes = [];
            return false;
        }

        pngBytes = WpfImageCodec.EncodePng(bitmap);
        return true;
    }

    private static bool TryNormalizeImage(byte[]? bytes, out byte[] pngBytes)
    {
        if (bytes is null || bytes.Length == 0)
        {
            pngBytes = [];
            return false;
        }

        return TryEncodeBitmap(WpfImageCodec.Decode(bytes), out pngBytes);
    }

    private static bool TryNormalizeDib(byte[]? dib, out byte[] pngBytes)
    {
        if (dib is null || dib.Length == 0)
        {
            pngBytes = [];
            return false;
        }

        try
        {
            return TryNormalizeImage(dib, out pngBytes);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Debug.WriteLine($"[Clipboard] DIB needs a bitmap file header: {exception.Message}");
            return TryNormalizeImage(ClipboardImageImport.WrapDibAsBmp(dib), out pngBytes);
        }
    }

    private static bool TryReadImageFile(string[]? paths, out byte[] pngBytes)
    {
        pngBytes = [];
        if (paths is null)
        {
            return false;
        }

        foreach (var path in paths)
        {
            if (DroppedFileImport.Classify(path) != DroppedFileKind.Image)
            {
                continue;
            }

            if (TryNormalizeImage(File.ReadAllBytes(path), out pngBytes))
            {
                return true;
            }
        }

        return false;
    }

    private static byte[]? ReadBytes(object? data)
    {
        if (data is byte[] bytes)
        {
            return bytes;
        }

        if (data is MemoryStream memory)
        {
            return memory.ToArray();
        }

        if (data is not Stream stream)
        {
            return null;
        }

        var originalPosition = stream.CanSeek ? stream.Position : 0;
        try
        {
            if (stream.CanSeek)
            {
                stream.Position = 0;
            }

            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            return copy.ToArray();
        }
        finally
        {
            if (stream.CanSeek)
            {
                stream.Position = originalPosition;
            }
        }
    }
}
