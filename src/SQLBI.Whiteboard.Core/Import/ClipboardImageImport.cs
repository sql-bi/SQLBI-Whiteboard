using System.Buffers.Binary;

namespace SQLBI.Whiteboard.Core.Import;

public enum ClipboardImageDataKind
{
    Encoded,
    Bitmap,
    Dib,
    FileDrop,
}

public readonly record struct ClipboardImageFormat(
    string Name,
    ClipboardImageDataKind Kind);

public static class ClipboardImageImport
{
    private static readonly ClipboardImageFormat[] Priority =
    [
        new("PNG", ClipboardImageDataKind.Encoded),
        new("image/png", ClipboardImageDataKind.Encoded),
        new("Bitmap", ClipboardImageDataKind.Bitmap),
        new("image/bmp", ClipboardImageDataKind.Encoded),
        new("DeviceIndependentBitmap", ClipboardImageDataKind.Dib),
        new("Format17", ClipboardImageDataKind.Dib),
        new("TaggedImageFileFormat", ClipboardImageDataKind.Encoded),
        new("image/tiff", ClipboardImageDataKind.Encoded),
        new("JFIF", ClipboardImageDataKind.Encoded),
        new("image/jpeg", ClipboardImageDataKind.Encoded),
        new("image/jpg", ClipboardImageDataKind.Encoded),
        new("image/gif", ClipboardImageDataKind.Encoded),
        new("FileDrop", ClipboardImageDataKind.FileDrop),
    ];

    public static IReadOnlyList<ClipboardImageFormat> PreferredFormats(
        IEnumerable<string> availableFormats)
    {
        ArgumentNullException.ThrowIfNull(availableFormats);
        var available = availableFormats
            .Where(format => !string.IsNullOrWhiteSpace(format))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var preferred = new List<ClipboardImageFormat>();

        foreach (var candidate in Priority)
        {
            var actualName = available.FirstOrDefault(format =>
                string.Equals(format, candidate.Name, StringComparison.OrdinalIgnoreCase));
            if (actualName is not null)
            {
                preferred.Add(candidate with { Name = actualName });
            }
        }

        return preferred;
    }

    public static byte[] WrapDibAsBmp(ReadOnlySpan<byte> dib)
    {
        const int bitmapFileHeaderSize = 14;
        if (dib.Length < 12)
        {
            throw new InvalidDataException("The clipboard DIB header is incomplete.");
        }

        var dibHeaderSize = BinaryPrimitives.ReadUInt32LittleEndian(dib);
        var pixelOffset = PixelOffset(dib, dibHeaderSize);
        var fileSize = checked(bitmapFileHeaderSize + dib.Length);
        var bitmap = new byte[fileSize];

        bitmap[0] = (byte)'B';
        bitmap[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(bitmap.AsSpan(2), (uint)fileSize);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bitmap.AsSpan(10),
            checked((uint)(bitmapFileHeaderSize + pixelOffset)));
        dib.CopyTo(bitmap.AsSpan(bitmapFileHeaderSize));
        return bitmap;
    }

    private static int PixelOffset(ReadOnlySpan<byte> dib, uint headerSize)
    {
        if (headerSize == 12)
        {
            var bitCount = BinaryPrimitives.ReadUInt16LittleEndian(dib.Slice(10, 2));
            var colorCount = bitCount <= 8 ? 1 << bitCount : 0;
            return ValidatePixelOffset(dib, checked(12 + (colorCount * 3)));
        }

        if (headerSize < 40 || headerSize > dib.Length)
        {
            throw new InvalidDataException("The clipboard DIB header is not supported.");
        }

        var bitCountInfo = BinaryPrimitives.ReadUInt16LittleEndian(dib.Slice(14, 2));
        var compression = BinaryPrimitives.ReadUInt32LittleEndian(dib.Slice(16, 4));
        var colorsUsed = BinaryPrimitives.ReadUInt32LittleEndian(dib.Slice(32, 4));
        var colorCountInfo = colorsUsed > 0
            ? checked((int)colorsUsed)
            : bitCountInfo <= 8
                ? 1 << bitCountInfo
                : 0;
        var masksAfterHeader = headerSize == 40
            ? compression switch
            {
                3 => 12,
                6 => 16,
                _ => 0,
            }
            : 0;
        var offset = checked((int)headerSize + masksAfterHeader + (colorCountInfo * 4));

        if (headerSize >= 124)
        {
            var profileOffset = BinaryPrimitives.ReadUInt32LittleEndian(dib.Slice(112, 4));
            var profileSize = BinaryPrimitives.ReadUInt32LittleEndian(dib.Slice(116, 4));
            if (profileOffset > 0 && profileSize > 0)
            {
                offset = Math.Max(offset, checked((int)(profileOffset + profileSize)));
            }
        }

        return ValidatePixelOffset(dib, offset);
    }

    private static int ValidatePixelOffset(ReadOnlySpan<byte> dib, int offset)
    {
        if (offset < 0 || offset > dib.Length)
        {
            throw new InvalidDataException("The clipboard DIB pixel offset is invalid.");
        }

        return offset;
    }
}
