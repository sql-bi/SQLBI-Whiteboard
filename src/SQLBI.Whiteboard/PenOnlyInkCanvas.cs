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

    public void SetStraightLineMode(bool active) =>
        _penOnlyRenderer.SetStraightLineMode(active);

    public StraightLineDirection TakeCompletedStraightLineDirection() =>
        _penOnlyRenderer.TakeCompletedStraightLineDirection();

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

// Clicking the barrel button over a hovering pen is delivered as a stylus down
// that claims everything a real touch claims - not in air, tip switch pressed.
// Only the pressure gives it away, because nothing is pressing on the tip.
// Requiring the barrel to be down as well keeps a genuinely light first packet
// from being mistaken for one of these.
internal static class PhantomStylus
{
    public static bool IsBarrelDown(StylusPointCollection points)
    {
        if (points.Count == 0)
        {
            return false;
        }

        for (var index = 0; index < points.Count; index++)
        {
            var point = points[index];
            if (point.PressureFactor > 0)
            {
                return false;
            }

            if (!point.HasProperty(StylusPointProperties.BarrelButton) ||
                point.GetPropertyValue(StylusPointProperties.BarrelButton) == 0)
            {
                return false;
            }
        }

        return true;
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

internal sealed class PenOnlyDynamicRenderer : DynamicRenderer
{
    private readonly ConcurrentDictionary<int, byte> _touchTabletIds = new();
    private volatile PenKind _penKind;
    private volatile bool _laserMode;
    private volatile bool _allowTouchInk;
    private volatile bool _straightLineMode;
    private PenKind _strokeKind;
    private StylusPoint? _lastCalligraphyPoint;
    private int _lastPacketTimestamp;
    private double _smoothedCalligraphySpeed;
    private bool _straightLineStroke;
    private bool _straightLineDecisionMade;
    private StraightLineDirection _straightLineDirection;
    private StylusPoint? _straightLineAnchor;
    private int _completedStraightLineDirection;

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

    public void SetStraightLineMode(bool active) => _straightLineMode = active;

    public StraightLineDirection TakeCompletedStraightLineDirection() =>
        (StraightLineDirection)Interlocked.Exchange(
            ref _completedStraightLineDirection,
            (int)StraightLineDirection.None);

    public void AbortWetInk()
    {
        ResetStraightLineStroke();
        Interlocked.Exchange(
            ref _completedStraightLineDirection,
            (int)StraightLineDirection.None);
        Enabled = false;
        Enabled = true;
    }

    protected override void OnStylusDown(RawStylusInput rawStylusInput)
    {
        // Without this the barrel click opens a wet stroke for a pen that never
        // touched the glass, which is then torn down mid-flight when the button
        // switches to the laser a moment later.
        if (IsTouch(rawStylusInput) ||
            _laserMode ||
            PhantomStylus.IsBarrelDown(rawStylusInput.GetStylusPoints()))
        {
            return;
        }

        _strokeKind = _penKind;
        ResetCalligraphyDynamics();
        BeginStraightLineStroke(rawStylusInput);
        ApplyCalligraphyDynamics(rawStylusInput);
        ApplyStraightLine(rawStylusInput);
        base.OnStylusDown(rawStylusInput);
    }

    protected override void OnStylusMove(RawStylusInput rawStylusInput)
    {
        if (IsTouch(rawStylusInput) || _laserMode)
        {
            return;
        }

        ApplyCalligraphyDynamics(rawStylusInput);
        ApplyStraightLine(rawStylusInput);
        base.OnStylusMove(rawStylusInput);
    }

    protected override void OnStylusUp(RawStylusInput rawStylusInput)
    {
        if (IsTouch(rawStylusInput) || _laserMode)
        {
            return;
        }

        ApplyCalligraphyDynamics(rawStylusInput);
        ApplyStraightLine(rawStylusInput);
        Interlocked.Exchange(
            ref _completedStraightLineDirection,
            (int)(_straightLineStroke
                ? _straightLineDirection
                : StraightLineDirection.None));
        base.OnStylusUp(rawStylusInput);
        ResetCalligraphyDynamics();
        ResetStraightLineStroke();
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

    private void BeginStraightLineStroke(RawStylusInput rawStylusInput)
    {
        ResetStraightLineStroke();
        Interlocked.Exchange(
            ref _completedStraightLineDirection,
            (int)StraightLineDirection.None);
        _straightLineStroke = _straightLineMode && !IsTouchTablet(rawStylusInput);
        if (!_straightLineStroke)
        {
            return;
        }

        var points = rawStylusInput.GetStylusPoints();
        if (points.Count > 0)
        {
            _straightLineAnchor = points[0];
        }
    }

    private void ApplyStraightLine(RawStylusInput rawStylusInput)
    {
        if (!_straightLineStroke)
        {
            return;
        }

        var points = rawStylusInput.GetStylusPoints();
        if (points.Count == 0)
        {
            return;
        }

        _straightLineAnchor ??= points[0];
        var anchor = _straightLineAnchor.Value;
        if (!_straightLineDecisionMade)
        {
            var latest = points[^1];
            var anchorPoint = new PointD(anchor.X, anchor.Y);
            var latestPoint = new PointD(latest.X, latest.Y);
            if (!StraightLineSnap.HasActivationDistance(anchorPoint, latestPoint))
            {
                for (var index = 0; index < points.Count; index++)
                {
                    var point = points[index];
                    point.X = anchor.X;
                    point.Y = anchor.Y;
                    points[index] = point;
                }

                rawStylusInput.SetStylusPoints(points);
                return;
            }

            _straightLineDirection = StraightLineSnap.DetectDirection(
                anchorPoint,
                latestPoint);
            _straightLineDecisionMade = true;
        }

        if (_straightLineDirection == StraightLineDirection.None)
        {
            return;
        }

        var fixedPoint = new PointD(anchor.X, anchor.Y);
        for (var index = 0; index < points.Count; index++)
        {
            var point = points[index];
            var snapped = StraightLineSnap.Apply(
                new PointD(point.X, point.Y),
                fixedPoint,
                _straightLineDirection);
            point.X = snapped.X;
            point.Y = snapped.Y;
            points[index] = point;
        }

        rawStylusInput.SetStylusPoints(points);
    }

    private void ResetStraightLineStroke()
    {
        _straightLineStroke = false;
        _straightLineDecisionMade = false;
        _straightLineDirection = StraightLineDirection.None;
        _straightLineAnchor = null;
    }

    private bool IsTouch(RawStylusInput rawStylusInput) =>
        !_allowTouchInk && _touchTabletIds.ContainsKey(rawStylusInput.TabletDeviceId);

    private bool IsTouchTablet(RawStylusInput rawStylusInput) =>
        _touchTabletIds.ContainsKey(rawStylusInput.TabletDeviceId);
}
