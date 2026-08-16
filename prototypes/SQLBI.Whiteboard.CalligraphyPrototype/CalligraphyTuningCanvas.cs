using System.Collections.Concurrent;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Input.StylusPlugIns;

namespace SQLBI.Whiteboard.CalligraphyPrototype;

public sealed class CalligraphyTuningCanvas : InkCanvas
{
    private readonly TuningDynamicRenderer _renderer;

    public CalligraphyTuningCanvas()
    {
        var touchTabletIds = Tablet.TabletDevices
            .Cast<TabletDevice>()
            .Where(device => device.Type == TabletDeviceType.Touch)
            .Select(device => device.Id);
        _renderer = new TuningDynamicRenderer(touchTabletIds);
        DynamicRenderer = _renderer;
    }

    internal void ApplySettings(CalligraphySettings settings)
    {
        DefaultDrawingAttributes = settings.CreateDrawingAttributes();
        _renderer.ApplySettings(settings);
    }

    internal (double RawPressure, double EffectivePressure, double Speed) ReadMetrics() =>
        _renderer.ReadMetrics();
}

internal sealed class TuningDynamicRenderer : DynamicRenderer
{
    private readonly ConcurrentDictionary<int, byte> _touchTabletIds = new();
    private CalligraphySettings _settings = CalligraphySettings.Stronger;
    private StylusPoint? _lastPoint;
    private int _lastPacketTimestamp;
    private double _smoothedSpeed;
    private double _latestRawPressure;
    private double _latestEffectivePressure;
    private double _latestSpeed;

    public TuningDynamicRenderer(IEnumerable<int> touchTabletIds)
    {
        foreach (int tabletId in touchTabletIds)
        {
            _touchTabletIds.TryAdd(tabletId, 0);
        }
    }

    public void ApplySettings(CalligraphySettings settings) =>
        Volatile.Write(ref _settings, settings);

    public (double RawPressure, double EffectivePressure, double Speed) ReadMetrics() =>
        (
            Volatile.Read(ref _latestRawPressure),
            Volatile.Read(ref _latestEffectivePressure),
            Volatile.Read(ref _latestSpeed));

    protected override void OnStylusDown(RawStylusInput rawStylusInput)
    {
        if (IsTouch(rawStylusInput))
        {
            return;
        }

        ResetStrokeDynamics();
        ApplyDynamics(rawStylusInput);
        base.OnStylusDown(rawStylusInput);
    }

    protected override void OnStylusMove(RawStylusInput rawStylusInput)
    {
        if (IsTouch(rawStylusInput))
        {
            return;
        }

        ApplyDynamics(rawStylusInput);
        base.OnStylusMove(rawStylusInput);
    }

    protected override void OnStylusUp(RawStylusInput rawStylusInput)
    {
        if (IsTouch(rawStylusInput))
        {
            return;
        }

        ApplyDynamics(rawStylusInput);
        base.OnStylusUp(rawStylusInput);
        ResetStrokeDynamics();
    }

    private void ApplyDynamics(RawStylusInput rawStylusInput)
    {
        StylusPointCollection points = rawStylusInput.GetStylusPoints();
        if (points.Count == 0)
        {
            return;
        }

        CalligraphySettings settings = Volatile.Read(ref _settings);
        int elapsedMilliseconds = _lastPoint is null
            ? points.Count
            : unchecked(rawStylusInput.Timestamp - _lastPacketTimestamp);
        if (elapsedMilliseconds <= 0 || elapsedMilliseconds > 250)
        {
            elapsedMilliseconds = points.Count;
        }

        double elapsedPerPoint = Math.Max(
            0.25,
            elapsedMilliseconds / (double)points.Count);
        for (int index = 0; index < points.Count; index++)
        {
            StylusPoint point = points[index];
            float rawPressure = point.PressureFactor;
            double speed = 0;
            if (_lastPoint is StylusPoint previous)
            {
                double deltaX = point.X - previous.X;
                double deltaY = point.Y - previous.Y;
                speed = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY)) /
                    elapsedPerPoint;
                _smoothedSpeed = _smoothedSpeed == 0
                    ? speed
                    : (_smoothedSpeed * settings.SpeedSmoothing) +
                      (speed * (1 - settings.SpeedSmoothing));
            }

            float effectivePressure = settings.CalculateEffectivePressure(
                rawPressure,
                _smoothedSpeed);
            point.PressureFactor = effectivePressure;
            points[index] = point;
            _lastPoint = point;
            Volatile.Write(ref _latestRawPressure, rawPressure);
            Volatile.Write(ref _latestEffectivePressure, effectivePressure);
            Volatile.Write(ref _latestSpeed, _smoothedSpeed);
        }

        _lastPacketTimestamp = rawStylusInput.Timestamp;
        rawStylusInput.SetStylusPoints(points);
    }

    private void ResetStrokeDynamics()
    {
        _lastPoint = null;
        _lastPacketTimestamp = 0;
        _smoothedSpeed = 0;
    }

    private bool IsTouch(RawStylusInput rawStylusInput) =>
        _touchTabletIds.ContainsKey(rawStylusInput.TabletDeviceId);
}
