using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices.Marshalling;
using SQLBI.Whiteboard.Core.Persistence;

namespace SQLBI.Whiteboard.ThumbnailHandler;

[GeneratedComClass]
internal sealed partial class WboardThumbnailProvider : IInitializeWithStream, IThumbnailProvider
{
    internal static readonly Guid Clsid = new(ShellGuids.ThumbnailHandlerId);

    private const int Ok = 0;
    private const int Unexpected = unchecked((int)0x8000FFFF);
    private const int InvalidArg = unchecked((int)0x80070057);
    private const int NotFound = unchecked((int)0x80030002);
    private const int AlreadyInitialized = unchecked((int)0x800704DF);
    private const int AlphaArgb = 2;

    private byte[]? _previewPng;

    public int Initialize(nint stream, uint mode)
    {
        if (stream == 0)
        {
            return InvalidArg;
        }

        if (_previewPng is not null)
        {
            return AlreadyInitialized;
        }

        try
        {
            using var source = new ComIStream(stream);
            using var copy = new MemoryStream();
            source.CopyTo(copy);
            copy.Position = 0;
            using var preview = new MemoryStream();
            if (!BoardArchive.TryCopyPreview(copy, preview))
            {
                return NotFound;
            }

            _previewPng = preview.ToArray();
            return Ok;
        }
        catch
        {
            return Unexpected;
        }
    }

    public int GetThumbnail(uint cx, out nint bitmap, out int alphaType)
    {
        bitmap = 0;
        alphaType = AlphaArgb;
        if (_previewPng is null)
        {
            return Unexpected;
        }

        try
        {
            using var preview = new MemoryStream(_previewPng, writable: false);
            using var source = Image.FromStream(preview, useEmbeddedColorManagement: false, validateImageData: false);
            var edge = cx == 0 ? 256 : (int)Math.Clamp(cx, 16, 1024);
            using var scaled = Scale(source, edge);
            bitmap = scaled.GetHbitmap();
            return bitmap == 0 ? Unexpected : Ok;
        }
        catch
        {
            return Unexpected;
        }
    }

    private static Bitmap Scale(Image source, int maxEdge)
    {
        var longest = Math.Max(source.Width, source.Height);
        if (longest <= maxEdge)
        {
            return new Bitmap(source);
        }

        var scale = maxEdge / (double)longest;
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var result = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(result);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(source, 0, 0, width, height);
        return result;
    }
}
