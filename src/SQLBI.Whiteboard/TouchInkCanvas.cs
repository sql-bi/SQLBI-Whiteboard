using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Input.StylusPlugIns;
using System.Windows.Media;
using System.Windows.Threading;
using SQLBI.Whiteboard.Core.Geometry;
using SQLBI.Whiteboard.Core.Model;

namespace SQLBI.Whiteboard;

/// <summary>
/// The finger's ink surface, and the host for the laser sampler and the hover
/// tracker. It collects no pen ink: the pen's packets are read directly by the
/// window - see MainWindow.AppendPenInk - because a barrel button tears the WPF
/// contact in two every time it is pressed or released, and no stroke built on
/// that bookkeeping could be made to behave like the Shift key.
/// </summary>
public sealed class TouchInkCanvas : InkCanvas
{
    private readonly TouchOnlyDynamicRenderer _touchRenderer;

    public LaserSamplePlugIn LaserSamples { get; } = new();

    public HoverTrackerPlugIn HoverTracker { get; } = new();

    public TouchInkCanvas()
    {
        var touchTabletIds = Tablet.TabletDevices
            .Cast<TabletDevice>()
            .Where(device => device.Type == TabletDeviceType.Touch)
            .Select(device => device.Id);
        _touchRenderer = new TouchOnlyDynamicRenderer(touchTabletIds);
        DynamicRenderer = _touchRenderer;
        StylusPlugIns.Add(LaserSamples);
        StylusPlugIns.Add(HoverTracker);
    }

    public void RegisterTouchTablet(int tabletDeviceId) =>
        _touchRenderer.RegisterTouchTablet(tabletDeviceId);

    public void SetPenKind(PenKind kind) => _touchRenderer.SetPenKind(kind);

    public void SetLaserMode(bool laser) => _touchRenderer.SetLaserMode(laser);

    public void SetAllowTouchInk(bool allow) => _touchRenderer.SetAllowTouchInk(allow);

    public void AbortWetInk()
    {
        _touchRenderer.AbortWetInk();
        Strokes.Clear();
    }

    public void DrainLaserSamples(Action<Point, float> consume) =>
        LaserSamples.Drain(consume);
}

public sealed class LaserSamplePlugIn : StylusPlugIn
{
    private readonly ConcurrentQueue<(double X, double Y, float Pressure)> _samples = new();

    public volatile bool Collect;
    public volatile bool Armed;

    public void Drain(Action<Point, float> consume)
    {
        while (_samples.TryDequeue(out var sample))
        {
            consume(new Point(sample.X, sample.Y), sample.Pressure);
        }
    }

    public void Clear()
    {
        while (_samples.TryDequeue(out _))
        {
        }
    }

    protected override void OnStylusDown(RawStylusInput rawStylusInput)
    {
        if (Armed || Collect)
        {
            Enqueue(rawStylusInput);
        }
    }

    protected override void OnStylusMove(RawStylusInput rawStylusInput)
    {
        if (Collect)
        {
            Enqueue(rawStylusInput);
        }
    }

    private void Enqueue(RawStylusInput rawStylusInput)
    {
        var points = rawStylusInput.GetStylusPoints();
        for (var index = 0; index < points.Count; index++)
        {
            var point = points[index];
            _samples.Enqueue((point.X, point.Y, Math.Clamp(point.PressureFactor, 0.02f, 1f)));
        }
    }
}

public sealed class HoverTrackerPlugIn : StylusPlugIn
{
    private readonly object _gate = new();
    private Point _latest;
    private bool _queued;

    public Action<Point>? Hovered { get; set; }

    // Contact is deliberately not tracked here. The barrel button opens a stylus
    // down whose up does not arrive until after the next real touch, so a flag
    // set here stayed set across the whole hover in between and every sample was
    // dropped. The window already knows whether the pen is down, and that is the
    // only copy of the state worth keeping.
    protected override void OnStylusUp(RawStylusInput rawStylusInput) =>
        Enqueue(rawStylusInput);

    protected override void OnStylusMove(RawStylusInput rawStylusInput) =>
        Enqueue(rawStylusInput);

    private void Enqueue(RawStylusInput rawStylusInput)
    {
        var points = rawStylusInput.GetStylusPoints();
        if (points.Count == 0)
        {
            return;
        }

        var point = points[^1];
        var latest = new Point(point.X, point.Y);
        lock (_gate)
        {
            _latest = latest;
            if (_queued)
            {
                return;
            }

            _queued = true;
        }

        var dispatcher = Element?.Dispatcher;
        if (dispatcher is null)
        {
            // Nothing will ever clear the queued flag otherwise, and the tracker
            // would go quiet for the rest of the session.
            lock (_gate)
            {
                _queued = false;
            }

            return;
        }

        dispatcher.BeginInvoke(() =>
        {
            Point consumed;
            lock (_gate)
            {
                _queued = false;
                consumed = _latest;
            }

            Hovered?.Invoke(consumed);
        }, DispatcherPriority.Input);
    }
}

/// <summary>
/// Wet ink for the finger only. Pen ink is collected and drawn by the window
/// from the pen's own packet stream, because a barrel button tears the WPF
/// contact in two every time it is pressed or released - see TODO.md - and a
/// wet stroke driven by that bookkeeping cannot match the stroke that results.
/// </summary>
internal sealed class TouchOnlyDynamicRenderer : DynamicRenderer
{
    private readonly ConcurrentDictionary<int, byte> _touchTabletIds = new();
    private volatile bool _laserMode;
    private volatile bool _allowTouchInk;
    private volatile PenKind _penKind;
    private PenKind _strokeKind;
    private StylusPoint? _lastCalligraphyPoint;
    private int _lastPacketTimestamp;
    private double _smoothedCalligraphySpeed;

    public TouchOnlyDynamicRenderer(IEnumerable<int> touchTabletIds)
    {
        foreach (var tabletId in touchTabletIds)
        {
            RegisterTouchTablet(tabletId);
        }
    }

    public void RegisterTouchTablet(int tabletDeviceId) =>
        _touchTabletIds.TryAdd(tabletDeviceId, 0);

    public void SetPenKind(PenKind kind) => _penKind = kind;

    public void SetLaserMode(bool laser)
    {
        _laserMode = laser;
        if (laser)
        {
            AbortWetInk();
        }
    }

    public void SetAllowTouchInk(bool allow)
    {
        _allowTouchInk = allow;
        if (!allow)
        {
            AbortWetInk();
        }
    }

    public void AbortWetInk()
    {
        Enabled = false;
        Enabled = true;
    }

    private bool Ignore(RawStylusInput rawStylusInput) =>
        _laserMode ||
        !_allowTouchInk ||
        !_touchTabletIds.ContainsKey(rawStylusInput.TabletDeviceId);

    protected override void OnStylusDown(RawStylusInput rawStylusInput)
    {
        if (Ignore(rawStylusInput))
        {
            return;
        }

        _strokeKind = _penKind;
        ResetCalligraphyDynamics();
        ApplyCalligraphyDynamics(rawStylusInput);
        base.OnStylusDown(rawStylusInput);
    }

    protected override void OnStylusMove(RawStylusInput rawStylusInput)
    {
        if (Ignore(rawStylusInput))
        {
            return;
        }

        ApplyCalligraphyDynamics(rawStylusInput);
        base.OnStylusMove(rawStylusInput);
    }

    protected override void OnStylusUp(RawStylusInput rawStylusInput)
    {
        if (Ignore(rawStylusInput))
        {
            return;
        }

        ApplyCalligraphyDynamics(rawStylusInput);
        base.OnStylusUp(rawStylusInput);
        ResetCalligraphyDynamics();
    }

    protected override void OnDraw(
        DrawingContext drawingContext,
        StylusPointCollection stylusPoints,
        Geometry geometry,
        Brush fillBrush)
    {
        if (_laserMode)
        {
            return;
        }

        base.OnDraw(drawingContext, stylusPoints, geometry, fillBrush);
    }

    private void ApplyCalligraphyDynamics(RawStylusInput rawStylusInput)
    {
        if (_strokeKind != PenKind.Calligraphy)
        {
            return;
        }

        var points = rawStylusInput.GetStylusPoints();
        if (points.Count == 0)
        {
            return;
        }

        var elapsedMilliseconds = _lastCalligraphyPoint is null
            ? points.Count
            : unchecked(rawStylusInput.Timestamp - _lastPacketTimestamp);
        if (elapsedMilliseconds <= 0 || elapsedMilliseconds > 250)
        {
            elapsedMilliseconds = points.Count;
        }

        var elapsedPerPoint = Math.Max(0.25, elapsedMilliseconds / (double)points.Count);
        for (var index = 0; index < points.Count; index++)
        {
            var point = points[index];
            var speed = 0d;
            if (_lastCalligraphyPoint is StylusPoint previous)
            {
                var deltaX = point.X - previous.X;
                var deltaY = point.Y - previous.Y;
                speed = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY)) / elapsedPerPoint;
                _smoothedCalligraphySpeed = _smoothedCalligraphySpeed == 0
                    ? speed
                    : (_smoothedCalligraphySpeed * 0.65) + (speed * 0.35);
            }

            point.PressureFactor = CalligraphyDynamics.AdjustPressure(
                point.PressureFactor,
                _smoothedCalligraphySpeed);
            points[index] = point;
            _lastCalligraphyPoint = point;
        }

        _lastPacketTimestamp = rawStylusInput.Timestamp;
        rawStylusInput.SetStylusPoints(points);
    }

    private void ResetCalligraphyDynamics()
    {
        _lastCalligraphyPoint = null;
        _lastPacketTimestamp = 0;
        _smoothedCalligraphySpeed = 0;
    }
}
