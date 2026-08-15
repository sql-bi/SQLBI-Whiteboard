using System.Diagnostics;
using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Foundation.Metadata;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using WinRT;
using WinRtDirect3DDevice = Windows.Graphics.DirectX.Direct3D11.IDirect3DDevice;

namespace SQLBI.Whiteboard.LiveView;

/// <summary>
/// Captures into the Direct3D device used by the WPF presentation surface. The
/// worker keeps only the newest frame, so capture can never queue behind the UI.
/// </summary>
internal sealed class LiveViewCaptureSession : IDisposable
{
    private readonly object _gate = new();
    private WinRtDirect3DDevice? _winRtDevice;
    private GraphicsCaptureItem? _captureItem;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _captureSession;
    private Direct3D11CaptureFrame? _pendingFrame;
    private SizeInt32 _contentSize;
    private bool _isFrozen;
    private bool _captureCursor;
    private int _desiredFrameRate = 15;
    private long _lastAcceptedTimestamp;
    private bool _disposed;

    public event Action? FrameAvailable;

    public event Action<SizeInt32>? ContentSizeChanged;

    public event Action? TargetClosed;

    public event Action<Exception>? CaptureFailed;

    public bool HasTarget
    {
        get
        {
            lock (_gate)
            {
                return _captureItem is not null;
            }
        }
    }

    public bool IsFrozen
    {
        get
        {
            lock (_gate)
            {
                return _isFrozen;
            }
        }
    }

    public int DesiredFrameRate
    {
        get => Volatile.Read(ref _desiredFrameRate);
        set
        {
            int validated = value is 15 or 30 or 60 ? value : 15;
            Volatile.Write(ref _desiredFrameRate, validated);
            lock (_gate)
            {
                ApplyMinimumUpdateInterval(_captureSession);
            }
        }
    }

    public bool CaptureCursor
    {
        get => Volatile.Read(ref _captureCursor);
        set
        {
            Volatile.Write(ref _captureCursor, value);
            lock (_gate)
            {
                if (_captureSession is not null)
                {
                    _captureSession.IsCursorCaptureEnabled = value;
                }
            }
        }
    }

    public void AttachDevice(ID3D11Device1 device)
    {
        ArgumentNullException.ThrowIfNull(device);
        using IDXGIDevice dxgiDevice = device.QueryInterface<IDXGIDevice>();
        WinRtDirect3DDevice winRtDevice = CreateWinRtDevice(dxgiDevice);

        lock (_gate)
        {
            ThrowIfDisposed();
            StopCaptureCore();
            _winRtDevice?.Dispose();
            _winRtDevice = winRtDevice;
            StartCaptureCore();
        }
    }

    public void DetachDevice()
    {
        lock (_gate)
        {
            StopCaptureCore();
            _winRtDevice?.Dispose();
            _winRtDevice = null;
        }
    }

    public void SetTarget(GraphicsCaptureItem captureItem)
    {
        ArgumentNullException.ThrowIfNull(captureItem);

        lock (_gate)
        {
            ThrowIfDisposed();
            StopCaptureCore();
            UnsubscribeFromCurrentItem();
            _captureItem = captureItem;
            _captureItem.Closed += CaptureItem_Closed;
            _contentSize = SanitizeSize(captureItem.Size);
            _isFrozen = false;
            StartCaptureCore();
        }

        ContentSizeChanged?.Invoke(_contentSize);
    }

    public void Freeze()
    {
        lock (_gate)
        {
            if (_captureItem is null || _isFrozen)
            {
                return;
            }

            _isFrozen = true;
            StopCaptureCore();
        }
    }

    public void Resume()
    {
        lock (_gate)
        {
            if (_captureItem is null || !_isFrozen)
            {
                return;
            }

            _isFrozen = false;
            StartCaptureCore();
        }
    }

    public bool TryPresent(ID3D11DeviceContext1 context, ID3D11Texture2D destinationTexture)
    {
        Direct3D11CaptureFrame? frame;
        lock (_gate)
        {
            frame = _pendingFrame;
            _pendingFrame = null;
        }

        if (frame is null)
        {
            return false;
        }

        try
        {
            using ID3D11Texture2D sourceTexture = GetTexture(frame.Surface);
            Texture2DDescription source = sourceTexture.Description;
            Texture2DDescription destination = destinationTexture.Description;
            if (source.Width != destination.Width || source.Height != destination.Height)
            {
                ContentSizeChanged?.Invoke(new SizeInt32(
                    checked((int)source.Width),
                    checked((int)source.Height)));
                return false;
            }

            context.CopyResource(destinationTexture, sourceTexture);
            return true;
        }
        catch (Exception exception)
        {
            CaptureFailed?.Invoke(exception);
            return false;
        }
        finally
        {
            frame.Dispose();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            StopCaptureCore();
            UnsubscribeFromCurrentItem();
            _captureItem = null;
            _winRtDevice?.Dispose();
            _winRtDevice = null;
        }
    }

    private void StartCaptureCore()
    {
        if (_captureItem is null || _winRtDevice is null || _isFrozen || _framePool is not null)
        {
            return;
        }

        _contentSize = SanitizeSize(_captureItem.Size);
        Direct3D11CaptureFramePool framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _winRtDevice,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            2,
            _contentSize);
        GraphicsCaptureSession captureSession = framePool.CreateCaptureSession(_captureItem);
        captureSession.IsCursorCaptureEnabled = _captureCursor;
        ApplyMinimumUpdateInterval(captureSession);
        framePool.FrameArrived += FramePool_FrameArrived;
        _framePool = framePool;
        _captureSession = captureSession;
        _lastAcceptedTimestamp = 0;

        try
        {
            captureSession.StartCapture();
        }
        catch
        {
            StopCaptureCore();
            throw;
        }
    }

    private void StopCaptureCore()
    {
        Direct3D11CaptureFramePool? framePool = _framePool;
        GraphicsCaptureSession? captureSession = _captureSession;
        _framePool = null;
        _captureSession = null;

        if (framePool is not null)
        {
            framePool.FrameArrived -= FramePool_FrameArrived;
        }

        captureSession?.Dispose();
        framePool?.Dispose();
        _pendingFrame?.Dispose();
        _pendingFrame = null;
    }

    private void FramePool_FrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        Direct3D11CaptureFrame? frame = null;
        try
        {
            frame = sender.TryGetNextFrame();
            if (frame is null)
            {
                return;
            }

            SizeInt32 size = SanitizeSize(frame.ContentSize);
            lock (_gate)
            {
                if (!ReferenceEquals(sender, _framePool) || _isFrozen)
                {
                    frame.Dispose();
                    return;
                }

                if (size.Width != _contentSize.Width || size.Height != _contentSize.Height)
                {
                    _contentSize = size;
                    sender.Recreate(
                        _winRtDevice!,
                        DirectXPixelFormat.B8G8R8A8UIntNormalized,
                        2,
                        size);
                    frame.Dispose();
                    frame = null;
                }
            }

            if (frame is null)
            {
                ContentSizeChanged?.Invoke(size);
                return;
            }

            long now = Stopwatch.GetTimestamp();
            long minimumTicks = Stopwatch.Frequency / Math.Max(1, DesiredFrameRate);
            long previous = Interlocked.Read(ref _lastAcceptedTimestamp);
            if (previous != 0 && now - previous < minimumTicks)
            {
                frame.Dispose();
                return;
            }

            Interlocked.Exchange(ref _lastAcceptedTimestamp, now);
            lock (_gate)
            {
                if (!ReferenceEquals(sender, _framePool) || _isFrozen)
                {
                    frame.Dispose();
                    return;
                }

                Direct3D11CaptureFrame? replaced = _pendingFrame;
                _pendingFrame = frame;
                frame = null;
                replaced?.Dispose();
            }

            FrameAvailable?.Invoke();
        }
        catch (Exception exception)
        {
            frame?.Dispose();
            CaptureFailed?.Invoke(exception);
        }
    }

    private void CaptureItem_Closed(GraphicsCaptureItem sender, object args)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(sender, _captureItem))
            {
                return;
            }

            StopCaptureCore();
            UnsubscribeFromCurrentItem();
            _captureItem = null;
            _isFrozen = true;
        }

        TargetClosed?.Invoke();
    }

    private void ApplyMinimumUpdateInterval(GraphicsCaptureSession? session)
    {
        if (session is not null &&
            ApiInformation.IsPropertyPresent(
                "Windows.Graphics.Capture.GraphicsCaptureSession",
                nameof(GraphicsCaptureSession.MinUpdateInterval)))
        {
            session.MinUpdateInterval = TimeSpan.FromSeconds(1d / DesiredFrameRate);
        }
    }

    private void UnsubscribeFromCurrentItem()
    {
        if (_captureItem is not null)
        {
            _captureItem.Closed -= CaptureItem_Closed;
        }
    }

    private static ID3D11Texture2D GetTexture(Windows.Graphics.DirectX.Direct3D11.IDirect3DSurface surface)
    {
        nint inspectable = ((IWinRTObject)surface).NativeObject.GetRef();
        try
        {
            using IDirect3DDxgiInterfaceAccess access = ComObject.As<IDirect3DDxgiInterfaceAccess>(inspectable);
            return access.GetInterface<ID3D11Texture2D>();
        }
        finally
        {
            Marshal.Release(inspectable);
        }
    }

    private static WinRtDirect3DDevice CreateWinRtDevice(IDXGIDevice dxgiDevice)
    {
        int result = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out nint inspectable);
        Marshal.ThrowExceptionForHR(result);
        try
        {
            return MarshalInterface<WinRtDirect3DDevice>.FromAbi(inspectable);
        }
        finally
        {
            Marshal.Release(inspectable);
        }
    }

    private static SizeInt32 SanitizeSize(SizeInt32 size) => new(
        Math.Max(1, size.Width),
        Math.Max(1, size.Height));

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(
        nint dxgiDevice,
        out nint graphicsDevice);
}
