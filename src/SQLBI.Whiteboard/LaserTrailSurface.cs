using System.Diagnostics;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using SQLBI.Whiteboard.Core.Settings;

namespace SQLBI.Whiteboard;

internal sealed class LaserTrailSurface : FrameworkElement
{
    private const double ResumeSeconds = 0.05;
    private const double ResumeDistance = 36;
    private const double MaximumWidth = 9;
    private const double MinimumWidth = 1.2;
    private static readonly Color LaserRed = Color.FromRgb(0xE1, 0x1D, 0x48);

    private readonly List<LaserStroke> _strokes = [];
    private readonly Dictionary<int, long> _groupClocks = [];
    private Point? _head;
    private float _headPressure = 0.5f;
    private bool _strokeOpen;
    private long _liftTimestamp;
    private bool _listening;
    private int _activeGroupId = 1;

    public double HoldSeconds { get; set; } = LaserSettings.DefaultHoldSeconds;

    public double FadeSeconds { get; set; } = LaserSettings.DefaultFadeSeconds;

    public LaserHoldMode HoldMode { get; set; } = LaserHoldMode.Shared;

    public LaserTrailSurface()
    {
        IsHitTestVisible = false;
        RenderOptions.SetEdgeMode(this, EdgeMode.Unspecified);
    }

    public void BeginOrResumeStroke()
    {
    }

    public void AddSample(Point point, float pressure, bool leaveTrail)
    {
        AcceptSample(point, pressure, leaveTrail);
        EnsureTick();
        InvalidateVisual();
    }

    public void Lift()
    {
        _head = null;
        _liftTimestamp = Stopwatch.GetTimestamp();
        EnsureTick();
        InvalidateVisual();
    }

    public void HideHead()
    {
        _head = null;
        BeginFadeOnOpenStroke();
        _liftTimestamp = 0;
        EnsureTick();
        InvalidateVisual();
    }

    public void Clear()
    {
        _head = null;
        _headPressure = 0.5f;
        _strokeOpen = false;
        _liftTimestamp = 0;
        _strokes.Clear();
        _groupClocks.Clear();
        _activeGroupId = 1;
        StopTick();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        CloseStrokeIfLiftExpired();
        TrimExpired();

        foreach (var stroke in _strokes)
        {
            if (stroke.Points.Count == 0)
            {
                continue;
            }

            var life = StrokeLife(stroke);
            if (life <= 0)
            {
                continue;
            }

            var last = stroke.Points[^1];
            var pressure = stroke.Points.Count == 1
                ? last.Pressure
                : stroke.Points[^Math.Min(stroke.Points.Count, 6)].Pressure;
            var width = WidthFor(pressure);
            var ink = stroke.Ink();
            if (ink is null)
            {
                continue;
            }

            ink.DrawingAttributes = LaserAttributes(
                width + 5,
                (byte)(55 * life));
            ink.Draw(drawingContext);
            ink.DrawingAttributes = LaserAttributes(
                width,
                (byte)(230 * life * (0.55 + (0.45 * pressure))));
            ink.Draw(drawingContext);
        }

        if (_head is not Point head)
        {
            return;
        }

        var headWidth = WidthFor(_headPressure);
        var halo = new RadialGradientBrush(
            Color.FromArgb((byte)(80 + (80 * _headPressure)), LaserRed.R, LaserRed.G, LaserRed.B),
            Color.FromArgb(0, LaserRed.R, LaserRed.G, LaserRed.B));
        halo.Freeze();
        drawingContext.DrawEllipse(halo, null, head, headWidth + 6, headWidth + 6);
        var hot = new SolidColorBrush(Color.FromArgb(
            (byte)(180 + (65 * _headPressure)),
            255,
            244,
            246));
        hot.Freeze();
        drawingContext.DrawEllipse(hot, null, head, Math.Max(1.4, headWidth * 0.38), Math.Max(1.4, headWidth * 0.38));
    }

    private void AcceptSample(Point point, float pressure, bool leaveTrail)
    {
        var amount = ClampPressure(pressure);
        if (leaveTrail)
        {
            EnsureStroke(point);
            AppendSample(point, amount);
        }

        _head = point;
        _headPressure = amount;
        _liftTimestamp = 0;
    }

    private void EnsureStroke(Point point)
    {
        if (_strokeOpen)
        {
            return;
        }

        if (_strokes.Count > 0 &&
            _strokes[^1].Points.Count > 0 &&
            _liftTimestamp != 0 &&
            AgeSeconds(_liftTimestamp) <= ResumeSeconds &&
            Distance(_strokes[^1].Points[^1].Point, point) <= ResumeDistance)
        {
            _strokeOpen = true;
            return;
        }

        _strokes.Add(new LaserStroke { GroupId = NextGroupId() });
        _strokeOpen = true;
        _liftTimestamp = 0;
    }

    private void AppendSample(Point point, float pressure)
    {
        var stroke = _strokes[^1];
        if (stroke.Points.Count == 0)
        {
            stroke.Add(new LaserPoint(point, Stopwatch.GetTimestamp(), pressure));
            return;
        }

        var previous = stroke.Points[^1];
        var gap = Distance(previous.Point, point);
        if (gap < 0.35)
        {
            stroke.ReplaceLast(new LaserPoint(point, Stopwatch.GetTimestamp(), pressure));
            return;
        }

        if (gap > 160)
        {
            return;
        }

        stroke.Add(new LaserPoint(point, Stopwatch.GetTimestamp(), pressure));
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        CloseStrokeIfLiftExpired();
        TrimExpired();
        if (_strokes.Count == 0 && _head is null)
        {
            StopTick();
        }

        InvalidateVisual();
    }

    private void CloseStrokeIfLiftExpired()
    {
        if (_strokeOpen &&
            _head is null &&
            _liftTimestamp != 0 &&
            AgeSeconds(_liftTimestamp) > ResumeSeconds)
        {
            BeginFadeOnOpenStroke();
        }
    }

    private void BeginFadeOnOpenStroke()
    {
        _strokeOpen = false;
        if (_strokes.Count == 0)
        {
            return;
        }

        if (HoldMode == LaserHoldMode.Shared)
        {
            _groupClocks[_strokes[^1].GroupId] = Stopwatch.GetTimestamp();
            return;
        }

        _strokes[^1].BeginFade();
    }

    private int NextGroupId()
    {
        if (HoldMode != LaserHoldMode.Shared)
        {
            return 0;
        }

        if (_groupClocks.TryGetValue(_activeGroupId, out var clock) &&
            (clock == 0 || AgeSeconds(clock) <= HoldSeconds))
        {
            _groupClocks[_activeGroupId] = 0;
            return _activeGroupId;
        }

        if (!_groupClocks.ContainsKey(_activeGroupId))
        {
            _groupClocks[_activeGroupId] = 0;
            return _activeGroupId;
        }

        _activeGroupId++;
        _groupClocks[_activeGroupId] = 0;
        return _activeGroupId;
    }

    private double StrokeLife(LaserStroke stroke)
    {
        if (HoldMode == LaserHoldMode.Shared)
        {
            if (!_groupClocks.TryGetValue(stroke.GroupId, out var clock) || clock == 0)
            {
                return 1;
            }

            return LifeFrom(clock);
        }

        return stroke.Life(HoldSeconds, FadeSeconds);
    }

    private double LifeFrom(long startedAt)
    {
        var age = AgeSeconds(startedAt);
        if (age <= HoldSeconds)
        {
            return 1;
        }

        var fade = Math.Max(FadeSeconds, 0.01);
        return Math.Clamp(1 - ((age - HoldSeconds) / fade), 0, 1);
    }

    private void EnsureTick()
    {
        if (_listening)
        {
            return;
        }

        CompositionTarget.Rendering += OnRendering;
        _listening = true;
    }

    private void StopTick()
    {
        if (!_listening)
        {
            return;
        }

        CompositionTarget.Rendering -= OnRendering;
        _listening = false;
    }

    private void TrimExpired()
    {
        _strokes.RemoveAll(stroke => StrokeLife(stroke) <= 0);
        if (_strokes.Count == 0)
        {
            _groupClocks.Clear();
            return;
        }

        var liveGroups = _strokes.Select(stroke => stroke.GroupId).ToHashSet();
        foreach (var groupId in _groupClocks.Keys.ToArray())
        {
            if (!liveGroups.Contains(groupId))
            {
                _groupClocks.Remove(groupId);
            }
        }
    }

    private static DrawingAttributes LaserAttributes(double width, byte alpha)
    {
        var size = Math.Max(0.8, width);
        return new DrawingAttributes
        {
            Color = Color.FromArgb(alpha, LaserRed.R, LaserRed.G, LaserRed.B),
            Width = size,
            Height = size,
            FitToCurve = false,
            IgnorePressure = false,
            StylusTip = StylusTip.Ellipse,
        };
    }

    private static double WidthFor(float pressure) =>
        MinimumWidth + ((MaximumWidth - MinimumWidth) * ClampPressure(pressure));

    private static float ClampPressure(float pressure) => Math.Clamp(pressure, 0.02f, 1f);

    private static double Distance(Point first, Point second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static double AgeSeconds(long timestamp) =>
        Stopwatch.GetElapsedTime(timestamp).TotalSeconds;

    private sealed class LaserStroke
    {
        private Stroke? _ink;
        private int _builtCount = -1;
        private Point _builtLast;
        private long _fadeStartedAt;

        public List<LaserPoint> Points { get; } = [];

        public int GroupId { get; init; }

        public int Count => Points.Count;

        public double Life(double holdSeconds, double fadeSeconds)
        {
            if (_fadeStartedAt == 0)
            {
                return 1;
            }

            var age = AgeSeconds(_fadeStartedAt);
            if (age <= holdSeconds)
            {
                return 1;
            }

            var fade = Math.Max(fadeSeconds, 0.01);
            return Math.Clamp(1 - ((age - holdSeconds) / fade), 0, 1);
        }

        public void BeginFade()
        {
            if (_fadeStartedAt == 0 && Points.Count > 0)
            {
                _fadeStartedAt = Stopwatch.GetTimestamp();
            }
        }

        public void Add(LaserPoint point)
        {
            Points.Add(point);
            _builtCount = -1;
        }

        public void ReplaceLast(LaserPoint point)
        {
            Points[^1] = point;
            _builtCount = -1;
        }

        public Stroke? Ink()
        {
            if (Points.Count == 0)
            {
                return null;
            }

            var last = Points[^1].Point;
            if (_ink is not null &&
                _builtCount == Points.Count &&
                _builtLast == last)
            {
                return _ink;
            }

            var stylusPoints = new StylusPointCollection(Points.Count);
            foreach (var point in Points)
            {
                stylusPoints.Add(new StylusPoint(
                    point.Point.X,
                    point.Point.Y,
                    Math.Clamp(point.Pressure, 0f, 1f)));
            }

            _ink = new Stroke(stylusPoints);
            _builtCount = Points.Count;
            _builtLast = last;
            return _ink;
        }
    }

    private readonly record struct LaserPoint(Point Point, long Timestamp, float Pressure);
}
