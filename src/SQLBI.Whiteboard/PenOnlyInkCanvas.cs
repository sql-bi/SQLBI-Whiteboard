using System.Collections.Concurrent;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Input.StylusPlugIns;
using SQLBI.Whiteboard.Core.Model;

namespace SQLBI.Whiteboard;

public sealed class PenOnlyInkCanvas : InkCanvas
{
    private readonly PenOnlyDynamicRenderer _penOnlyRenderer;

    public PenOnlyInkCanvas()
    {
        var touchTabletIds = Tablet.TabletDevices
            .Cast<TabletDevice>()
            .Where(device => device.Type == TabletDeviceType.Touch)
            .Select(device => device.Id);
        _penOnlyRenderer = new PenOnlyDynamicRenderer(touchTabletIds);
        DynamicRenderer = _penOnlyRenderer;
    }

    public void RegisterTouchTablet(int tabletDeviceId) =>
        _penOnlyRenderer.RegisterTouchTablet(tabletDeviceId);

    public void SetPenKind(PenKind kind) =>
        _penOnlyRenderer.SetPenKind(kind);
}

internal sealed class PenOnlyDynamicRenderer : DynamicRenderer
{
    private readonly ConcurrentDictionary<int, byte> _touchTabletIds = new();
    private volatile PenKind _penKind;
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

    protected override void OnStylusDown(RawStylusInput rawStylusInput)
    {
        if (!IsTouch(rawStylusInput))
        {
            _strokeKind = _penKind;
            ResetCalligraphyDynamics();
            ApplyCalligraphyDynamics(rawStylusInput);
            base.OnStylusDown(rawStylusInput);
        }
    }

    protected override void OnStylusMove(RawStylusInput rawStylusInput)
    {
        if (!IsTouch(rawStylusInput))
        {
            ApplyCalligraphyDynamics(rawStylusInput);
            base.OnStylusMove(rawStylusInput);
        }
    }

    protected override void OnStylusUp(RawStylusInput rawStylusInput)
    {
        if (!IsTouch(rawStylusInput))
        {
            ApplyCalligraphyDynamics(rawStylusInput);
            base.OnStylusUp(rawStylusInput);
            ResetCalligraphyDynamics();
        }
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
        _touchTabletIds.ContainsKey(rawStylusInput.TabletDeviceId);
}
