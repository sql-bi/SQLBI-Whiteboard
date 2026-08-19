using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Vortice.DXGI;
using Vortice.Wpf;
using Windows.Graphics;
using Windows.Graphics.Capture;

namespace SQLBI.Whiteboard.LiveView;

/// <summary>
/// Bridges one capture session to a D3DImage that the retained board renderer
/// can draw at any world-coordinate rectangle.
/// </summary>
internal sealed class LiveViewPresenter : IDisposable
{
    private readonly LiveViewCaptureSession _capture = new();
    private readonly Dispatcher _dispatcher;
    private SizeInt32 _contentSize = new(960, 540);
    private int _invalidateQueued;
    private bool _disposed;
    private bool _failureReported;

    public LiveViewPresenter(Guid objectId, Dispatcher dispatcher)
    {
        ObjectId = objectId;
        _dispatcher = dispatcher;
        Surface = new OnDemandDrawingSurface
        {
            Width = _contentSize.Width,
            Height = _contentSize.Height,
            DepthStencilFormat = Format.Unknown,
            IsHitTestVisible = false,
            Focusable = false,
        };

        Surface.LoadContent += Surface_LoadContent;
        Surface.Draw += Surface_Draw;
        Surface.UnloadContent += Surface_UnloadContent;
        _capture.FrameAvailable += Capture_FrameAvailable;
        _capture.ContentSizeChanged += Capture_ContentSizeChanged;
        _capture.TargetClosed += Capture_TargetClosed;
        _capture.CaptureFailed += Capture_CaptureFailed;
    }

    public event Action<Guid>? FramePresented;

    public event Action<Guid>? TargetClosed;

    public event Action<Guid, Exception>? CaptureFailed;

    public Guid ObjectId { get; }

    public OnDemandDrawingSurface Surface { get; }

    public ImageSource? ImageSource =>
        !IsFrozen && HasPresentedFrame
            ? Surface.Source
            : null;

    public bool HasPresentedFrame { get; private set; }

    public bool HasTarget => _capture.HasTarget;

    public bool IsFrozen => _capture.IsFrozen;

    public int DesiredFrameRate
    {
        get => _capture.DesiredFrameRate;
        set => _capture.DesiredFrameRate = value;
    }

    public bool CaptureCursor
    {
        get => _capture.CaptureCursor;
        set => _capture.CaptureCursor = value;
    }

    public void SetTarget(GraphicsCaptureItem item)
    {
        _failureReported = false;
        _capture.SetTarget(item);
    }

    public void Freeze() => _capture.Freeze();

    public void Resume() => _capture.Resume();

    public void ClearTarget() => _capture.ClearTarget();

    public byte[]? CaptureSnapshotPng()
    {
        _dispatcher.VerifyAccess();
        if (!HasPresentedFrame || Surface.Source is not ImageSource source)
        {
            return null;
        }

        int width = Math.Max(1, _contentSize.Width);
        int height = Math.Max(1, _contentSize.Height);
        DrawingVisual visual = new();
        using (DrawingContext drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.Black, null, new Rect(0, 0, width, height));
            drawing.DrawImage(source, new Rect(0, 0, width, height));
        }

        RenderTargetBitmap bitmap = new(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return WpfImageCodec.EncodePng(bitmap);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _capture.FrameAvailable -= Capture_FrameAvailable;
        _capture.ContentSizeChanged -= Capture_ContentSizeChanged;
        _capture.TargetClosed -= Capture_TargetClosed;
        _capture.CaptureFailed -= Capture_CaptureFailed;
        _capture.Dispose();
        Surface.LoadContent -= Surface_LoadContent;
        Surface.Draw -= Surface_Draw;
        Surface.UnloadContent -= Surface_UnloadContent;
    }

    private void Surface_LoadContent(object? sender, DrawingSurfaceEventArgs e)
    {
        try
        {
            _capture.AttachDevice(e.Device);
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
        }
    }

    private void Surface_Draw(object? sender, DrawEventArgs e)
    {
        try
        {
            if (e.Surface.ColorTexture is null ||
                !_capture.TryPresent(e.Context, e.Surface.ColorTexture))
            {
                return;
            }

            HasPresentedFrame = true;
            FramePresented?.Invoke(ObjectId);
        }
        finally
        {
            Surface.CompleteRefresh();
        }
    }

    private void Surface_UnloadContent(object? sender, DrawingSurfaceEventArgs e)
    {
        _capture.DetachDevice();
        HasPresentedFrame = false;
    }

    private void Capture_FrameAvailable()
    {
        if (_disposed || Interlocked.Exchange(ref _invalidateQueued, 1) != 0)
        {
            return;
        }

        _ = _dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            Interlocked.Exchange(ref _invalidateQueued, 0);
            if (!_disposed)
            {
                Surface.Invalidate();
            }
        });
    }

    private void Capture_ContentSizeChanged(SizeInt32 size)
    {
        _ = _dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
        {
            if (_disposed)
            {
                return;
            }

            _contentSize = new SizeInt32(Math.Max(1, size.Width), Math.Max(1, size.Height));
            Surface.Width = _contentSize.Width;
            Surface.Height = _contentSize.Height;
            Surface.Invalidate();
        });
    }

    private void Capture_TargetClosed()
    {
        _ = _dispatcher.BeginInvoke(() => TargetClosed?.Invoke(ObjectId));
    }

    private void Capture_CaptureFailed(Exception exception)
    {
        _ = _dispatcher.BeginInvoke(() => ReportFailure(exception));
    }

    private void ReportFailure(Exception exception)
    {
        if (_failureReported)
        {
            return;
        }

        _failureReported = true;
        CaptureFailed?.Invoke(ObjectId, exception);
    }
}
