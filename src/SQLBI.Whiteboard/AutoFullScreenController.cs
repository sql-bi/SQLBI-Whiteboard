using System.Windows.Threading;

namespace SQLBI.Whiteboard;

internal sealed class AutoFullScreenController : IDisposable
{
    internal static readonly TimeSpan Delay = TimeSpan.FromSeconds(10);
    private readonly Func<bool> _canEnter;
    private readonly Action _enter;
    private readonly TimeProvider _clock;
    private readonly DispatcherTimer _timer;
    private long _lastActivity;
    private bool _enabled;
    private bool _disposed;

    public AutoFullScreenController(Func<bool> canEnter, Action enter, TimeProvider? clock = null)
    {
        _canEnter = canEnter;
        _enter = enter;
        _clock = clock ?? TimeProvider.System;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = Delay };
        _timer.Tick += Timer_Tick;
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_disposed || _enabled == value) return;
            _enabled = value;
            Reset();
        }
    }

    // Pen packets only record a monotonic timestamp. They do not restart a
    // dispatcher timer for every point, or count rendering as user activity.
    public void RecordActivity()
    {
        if (_enabled) _lastActivity = _clock.GetTimestamp();
    }

    public void Reset()
    {
        _timer.Stop();
        if (!_enabled) return;
        RecordActivity();
        _timer.Interval = Delay;
        _timer.Start();
    }

    internal void CheckInactivity()
    {
        if (!_enabled) return;
        if (!_canEnter())
        {
            Reset();
            return;
        }

        TimeSpan remaining = Delay - _clock.GetElapsedTime(_lastActivity);
        if (remaining > TimeSpan.Zero)
        {
            _timer.Interval = remaining;
            return;
        }

        Reset();
        _enter();
    }

    private void Timer_Tick(object? sender, EventArgs e) => CheckInactivity();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _enabled = false;
        _timer.Stop();
        _timer.Tick -= Timer_Tick;
    }
}
