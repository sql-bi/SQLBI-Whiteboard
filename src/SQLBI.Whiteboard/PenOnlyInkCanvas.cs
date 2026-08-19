using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Input.StylusPlugIns;
using System.Windows.Media;
using System.Windows.Threading;
using SQLBI.Whiteboard.Core.Model;

namespace SQLBI.Whiteboard;

public sealed class PenOnlyInkCanvas : InkCanvas
{
    private readonly PenOnlyDynamicRenderer _penOnlyRenderer;

    public LaserSamplePlugIn LaserSamples { get; } = new();

    public HoverTrackerPlugIn HoverTracker { get; } = new();

    public PenOnlyInkCanvas()
    {
        var touchTabletIds = Tablet.TabletDevices
            .Cast<TabletDevice>()
            .Where(device => device.Type == TabletDeviceType.Touch)
            .Select(device => device.Id);
        _penOnlyRenderer = new PenOnlyDynamicRenderer(touchTabletIds);
        DynamicRenderer = _penOnlyRenderer;
        StylusPlugIns.Add(LaserSamples);
        StylusPlugIns.Add(HoverTracker);
    }

    public void RegisterTouchTablet(int tabletDeviceId) =>
        _penOnlyRenderer.RegisterTouchTablet(tabletDeviceId);

    public void SetPenKind(PenKind kind) =>
        _penOnlyRenderer.SetPenKind(kind);

    public void SetLaserMode(bool laser) =>
        _penOnlyRenderer.SetLaserMode(laser);

    public void SetAllowTouchInk(bool allow) =>
        _penOnlyRenderer.SetAllowTouchInk(allow);

    public void AbortWetInk()
    {
        _penOnlyRenderer.AbortWetInk();
        Strokes.Clear();
    }

    public void DrainLaserSamples(Action<Point, float> consume)
    {
        LaserSamples.Drain(consume);
    }
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
    private bool _contact;

    public Action<Point>? Hovered { get; set; }

    protected override void OnStylusDown(RawStylusInput rawStylusInput) => _contact = true;

    protected override void OnStylusUp(RawStylusInput rawStylusInput)
    {
        _contact = false;
        Enqueue(rawStylusInput);
    }

    protected override void OnStylusMove(RawStylusInput rawStylusInput)
    {
        if (!_contact)
        {
            Enqueue(rawStylusInput);
        }
    }

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
        dispatcher?.BeginInvoke(() =>
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

internal sealed class PenOnlyDynamicRenderer : DynamicRenderer
{
    private readonly ConcurrentDictionary<int, byte> _touchTabletIds = new();
    private volatile PenKind _penKind;
    private volatile bool _laserMode;
    private volatile bool _allowTouchInk;
    private PenKind _strokeKind;
    private StylusPoint? _lastCalligraphyPoint;
    private int _lastPacketTimestamp;
    private double _smoothedCalligraphySpeed;

    public PenOnlyDynamicRenderer(IEnumerable<int> touchTabletIds)
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

    protected override void OnStylusDown(RawStylusInput rawStylusInput)
    {
        if (IsTouch(rawStylusInput) || _laserMode)
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
        if (IsTouch(rawStylusInput) || _laserMode)
        {
            return;
        }

        ApplyCalligraphyDynamics(rawStylusInput);
        base.OnStylusMove(rawStylusInput);
    }

    protected override void OnStylusUp(RawStylusInput rawStylusInput)
    {
        if (IsTouch(rawStylusInput) || _laserMode)
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

    private bool IsTouch(RawStylusInput rawStylusInput) =>
        !_allowTouchInk && _touchTabletIds.ContainsKey(rawStylusInput.TabletDeviceId);
}
