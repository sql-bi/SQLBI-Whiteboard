using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace SQLBI.Whiteboard.Import;

/// <summary>
/// The clipboard as the PowerPoint import uses it: the SVG PowerPoint puts there when
/// a slide is copied, and whatever the person had there before, put back afterwards.
/// </summary>
internal static class SlideClipboard
{
    private const int Attempts = 5;

    /// <summary>
    /// Copies the slide and reads the SVG back, or null after five tries. Another
    /// process can hold the clipboard for a moment, and the first copy after PowerPoint
    /// starts is sometimes not there yet when it is read.
    /// </summary>
    public static byte[]? CopySvg(dynamic slide)
    {
        var format = RegisterClipboardFormat("image/svg+xml");
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            try
            {
                slide.Copy();
            }
            catch (COMException)
            {
                Thread.Sleep(200);
                continue;
            }

            if (Read(format) is { Length: > 0 } bytes)
            {
                return bytes;
            }

            Thread.Sleep(200);
        }

        return null;
    }

    private static byte[]? Read(uint format)
    {
        var opened = false;
        for (var attempt = 0; attempt < 20 && !opened; attempt++)
        {
            opened = OpenClipboard(IntPtr.Zero);
            if (!opened)
            {
                Thread.Sleep(50);
            }
        }

        if (!opened)
        {
            return null;
        }

        try
        {
            var handle = GetClipboardData(format);
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            var size = (int)GlobalSize(handle);
            var pointer = GlobalLock(handle);
            if (pointer == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var bytes = new byte[size];
                Marshal.Copy(pointer, bytes, 0, size);

                // The block is rounded up and ends in zeros the markup does not have.
                var length = bytes.Length;
                while (length > 0 && bytes[length - 1] == 0)
                {
                    length--;
                }

                return bytes[..length];
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>
    /// What the clipboard held before the import used it: text, a picture, or files.
    /// Anything else cannot be kept, and <see cref="Restore"/> says so.
    /// </summary>
    public sealed class Snapshot
    {
        private readonly string? _text;
        private readonly BitmapSource? _image;
        private readonly StringCollection? _files;
        private readonly bool _heldSomethingElse;

        private Snapshot(string? text, BitmapSource? image, StringCollection? files, bool heldSomethingElse)
        {
            _text = text;
            _image = image;
            _files = files;
            _heldSomethingElse = heldSomethingElse;
        }

        public static Snapshot Take()
        {
            try
            {
                var text = Clipboard.ContainsText() ? Clipboard.GetText() : null;
                var image = Clipboard.ContainsImage() ? Clipboard.GetImage() : null;
                var files = Clipboard.ContainsFileDropList() ? Clipboard.GetFileDropList() : null;
                var heldSomething = Clipboard.GetDataObject()?.GetFormats() is { Length: > 0 };
                return new Snapshot(text, image, files, heldSomething && text is null && image is null && files is null);
            }
            catch (COMException)
            {
                return new Snapshot(null, null, null, heldSomethingElse: true);
            }
        }

        /// <summary>
        /// Puts the clipboard back. False when what was there could not be kept or
        /// could not be written back.
        /// </summary>
        public bool Restore()
        {
            try
            {
                if (_text is null && _image is null && _files is null)
                {
                    Clipboard.Clear();
                    return !_heldSomethingElse;
                }

                var data = new DataObject();
                if (_text is not null)
                {
                    data.SetText(_text);
                }

                if (_image is not null)
                {
                    data.SetImage(_image);
                }

                if (_files is not null)
                {
                    data.SetFileDropList(_files);
                }

                Clipboard.SetDataObject(data, copy: true);
                return true;
            }
            catch (COMException)
            {
                return false;
            }
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterClipboardFormat(string format);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardData(uint format);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr handle);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern UIntPtr GlobalSize(IntPtr handle);
}
