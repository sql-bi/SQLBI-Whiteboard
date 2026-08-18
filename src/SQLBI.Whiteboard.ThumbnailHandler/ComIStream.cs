using System.Runtime.InteropServices;

namespace SQLBI.Whiteboard.ThumbnailHandler;

/// <summary>
/// Sequential reader over an Explorer-supplied IStream. Isolated thumbnail
/// hosts often hand out streams that do not implement Stat or Seek, so the
/// handler copies this into a MemoryStream before ZipArchive sees it.
/// </summary>
internal sealed unsafe class ComIStream : Stream
{
    private nint _stream;
    private bool _disposed;

    public ComIStream(nint stream)
    {
        if (stream == 0)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        Marshal.AddRef(stream);
        _stream = stream;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset + count > buffer.Length)
        {
            throw new ArgumentException("The read range is outside the buffer.", nameof(count));
        }

        if (count == 0)
        {
            return 0;
        }

        EnsureNotDisposed();
        uint read;
        int hr;
        fixed (byte* pointer = buffer)
        {
            hr = StreamRead(_stream, pointer + offset, (uint)count, &read);
        }

        // S_FALSE means a short read / end of stream. Still return the bytes we got.
        if (hr < 0)
        {
            Marshal.ThrowExceptionForHR(hr);
        }

        return (int)read;
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (_stream != 0)
            {
                Marshal.Release(_stream);
                _stream = 0;
            }

            _disposed = true;
        }

        base.Dispose(disposing);
    }

    private void EnsureNotDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed || _stream == 0, this);

    private static int StreamRead(nint stream, byte* buffer, uint count, uint* read)
    {
        var vtable = *(nint**)stream;
        var function = (delegate* unmanaged[MemberFunction]<nint, byte*, uint, uint*, int>)vtable[3];
        return function(stream, buffer, count, read);
    }
}
