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
    private const double MaximumWidth = LaserSettings.MaximumTrailWidth;

    // The hover dot is deliberately independent of the trail weight below: that
    // setting is about how hard you have to press, and the pointer is drawn
    // without pressing at all.
    private const double HoverHeadWidth = 5.1;

    // The hover comet is every sample from this window, so its length is the
    // distance the pen covered in that time: a fast hover draws a long tail and
    // a slow one barely more than the dot. The cap stops a flick from streaking
    // across the board.
    private const double HoverTrailSeconds = 0.11;
    private const double HoverTrailLimit = 170;
    private const float HoverPressure = 0.5f;
    private static readonly Color LaserRed = Color.FromRgb(
        LaserSettings.TrailRed,
        LaserSettings.TrailGreen,
        LaserSettings.TrailBlue);

    private readonly List<LaserStroke> _strokes = [];
    private readonly Dictionary<int, long> _groupClocks = [];
    private readonly List<LaserPoint> _hover = [];
    private Point? _hoverHead;
    private Point? _head;
    private float _headPressure = 0.5f;
    private bool _strokeOpen;
    private long _liftTimestamp;
    private bool _listening;
    private int _activeGroupId = 1;

    public double HoldSeconds { get; set; } = LaserSettings.DefaultHoldSeconds;

    public double FadeSeconds { get; set; } = LaserSettings.DefaultFadeSeconds;

    public LaserHoldMode HoldMode { get; set; } = LaserHoldMode.Shared;

    public LaserTrailWeight TrailWeight { get; set; } = LaserTrailWeight.Light;

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

    // Hover is deliberately kept clear of the contact state. A lifted pen leaves
    // the stroke open for a moment so a resumed line joins the last one, and that
    // window is measured from _head being gone; a hovering pen must not fill it.
    public void Hover(Point point)
    {
        var now = Stopwatch.GetTimestamp();
        if (_hover.Count > 0 && Distance(_hover[^1].Point, point) < 0.5)
        {
            _hover[^1] = new LaserPoint(point, now, HoverPressure);
        }
        else
        {
            _hover.Add(new LaserPoint(point, now, HoverPressure));
        }

        _hoverHead = point;
        TrimHover();
        EnsureTick();
        InvalidateVisual();
    }

    public void EndHover()
    {
        if (_hoverHead is null && _hover.Count == 0)
        {
            return;
        }

        _hoverHead = null;
        _hover.Clear();
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
        _hover.Clear();
        _hoverHead = null;
        _activeGroupId = 1;
        StopTick();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        CloseStrokeIfLiftExpired();
        TrimExpired();
        TrimHover();

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
                (byte)(230 * life * OpacityFor(pressure)));
            ink.Draw(drawingContext);
        }

        if (_hoverHead is Point hoverHead)
        {
            DrawHoverTail(drawingContext);
            DrawHead(drawingContext, hoverHead, HoverHeadWidth, HoverPressure);
        }

        if (_head is Point head)
        {
            DrawHead(drawingContext, head, WidthFor(_headPressure), _headPressure);
        }
    }

    // A solid red centre inside a soft shade. The shade is what separates the
    // pointer from drawn ink; the centre is what stays legible on a white board
    // once a projector and a video encoder have had their turn at it.
    private static void DrawHead(
        DrawingContext drawingContext,
        Point head,
        double headWidth,
        float pressure)
    {
        var halo = new RadialGradientBrush
        {
            GradientStops =
            [
                new GradientStop(
                    Color.FromArgb((byte)(120 + (70 * pressure)), LaserRed.R, LaserRed.G, LaserRed.B),
                    0.3),
                new GradientStop(Color.FromArgb(0, LaserRed.R, LaserRed.G, LaserRed.B), 1),
            ],
        };
        halo.Freeze();
        drawingContext.DrawEllipse(halo, null, head, headWidth + 6, headWidth + 6);

        var core = new SolidColorBrush(LaserRed);
        core.Freeze();
        var coreRadius = Math.Max(2.4, headWidth * 0.55);
        drawingContext.DrawEllipse(core, null, head, coreRadius, coreRadius);

        var hot = new SolidColorBrush(Color.FromArgb(
            (byte)(150 + (70 * pressure)),
            255,
            244,
            246));
        hot.Freeze();
        var hotRadius = Math.Max(0.9, headWidth * 0.2);
        drawingContext.DrawEllipse(hot, null, head, hotRadius, hotRadius);
    }

    private void DrawHoverTail(DrawingContext drawingContext)
    {
        if (_hover.Count < 2)
        {
            return;
        }

        var tail = _hover[0].Point;
        var head = _hover[^1].Point;
        if (Distance(tail, head) < 1.5)
        {
            return;
        }

        var half = HoverHeadWidth * 0.5;
        var glow = HoverGeometry(half + 2.5);
        var core = HoverGeometry(half);
        if (glow is null || core is null)
        {
            return;
        }

        drawingContext.DrawGeometry(HoverBrush(tail, head, 70), null, glow);
        drawingContext.DrawGeometry(HoverBrush(tail, head, 205), null, core);
    }

    // A wedge that comes to a point at the tail and reaches full width under the
    // head, so the comet reads as motion rather than as a drawn line.
    private Geometry? HoverGeometry(double halfWidth)
    {
        var count = _hover.Count;
        var left = new Point[count];
        var right = new Point[count];
        for (var index = 0; index < count; index++)
        {
            var direction = HoverDirection(index);
            var taper = halfWidth * index / (count - 1);
            var offsetX = -direction.Y * taper;
            var offsetY = direction.X * taper;
            var point = _hover[index].Point;
            left[index] = new Point(point.X + offsetX, point.Y + offsetY);
            right[index] = new Point(point.X - offsetX, point.Y - offsetY);
        }

        Array.Reverse(right);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(left[0], isFilled: true, isClosed: true);
            context.PolyLineTo(left[1..], isStroked: false, isSmoothJoin: true);
            context.PolyLineTo(right, isStroked: false, isSmoothJoin: true);
        }

        geometry.Freeze();
        return geometry;
    }

    private Vector HoverDirection(int index)
    {
        var previous = _hover[Math.Max(index - 1, 0)].Point;
        var next = _hover[Math.Min(index + 1, _hover.Count - 1)].Point;
        var direction = next - previous;
        if (direction.Length < 0.001)
        {
            direction = _hover[^1].Point - _hover[0].Point;
            if (direction.Length < 0.001)
            {
                return new Vector(1, 0);
            }
        }

        direction.Normalize();
        return direction;
    }

    private static Brush HoverBrush(Point tail, Point head, byte alpha)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = tail,
            EndPoint = head,
            MappingMode = BrushMappingMode.Absolute,
        };
        brush.GradientStops.Add(
            new GradientStop(Color.FromArgb(0, LaserRed.R, LaserRed.G, LaserRed.B), 0));
        brush.GradientStops.Add(
            new GradientStop(Color.FromArgb(alpha, LaserRed.R, LaserRed.G, LaserRed.B), 1));
        brush.Freeze();
        return brush;
    }

    private void TrimHover()
    {
        _hover.RemoveAll(sample => AgeSeconds(sample.Timestamp) > HoverTrailSeconds);

        var length = 0.0;
        for (var index = _hover.Count - 1; index > 0; index--)
        {
            length += Distance(_hover[index].Point, _hover[index - 1].Point);
            if (length > HoverTrailLimit)
            {
                _hover.RemoveRange(0, index);
                return;
            }
        }
    }

    private void AcceptSample(Point point, float pressure, bool leaveTrail)
    {
        _hover.Clear();
        _hoverHead = null;
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
        TrimHover();

        // A stationary hover has nothing left to animate once its tail has aged
        // out; the head stays on screen and the next move restarts the clock.
        if (_strokes.Count == 0 && _head is null && _hover.Count <= 1)
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

    private double WidthFor(float pressure)
    {
        var minimum = LaserSettings.MinimumTrailWidthFor(TrailWeight);
        return minimum + ((MaximumWidth - minimum) * ClampPressure(pressure));
    }

    private double OpacityFor(float pressure)
    {
        var floor = LaserSettings.MinimumTrailOpacityFor(TrailWeight);
        return floor + ((1 - floor) * ClampPressure(pressure));
    }

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
