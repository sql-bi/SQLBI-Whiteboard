using System.Diagnostics;
using System.Windows.Threading;

namespace SQLBI.Whiteboard.SmokeTests;

internal static class AutoFullScreenSmokeTests
{
    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Check();
                CheckDispatcherTimer();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("Auto full screen smoke tests failed.", failure);
    }

    private static void Check()
    {
        var clock = new Clock();
        bool eligible = true;
        bool fullScreen = false;
        int transitions = 0;
        using var controller = new AutoFullScreenController(() => eligible && !fullScreen, () =>
        {
            fullScreen = true;
            transitions++;
        }, clock);

        Advance(60);
        Assert(transitions == 0, "The default must never enter full screen automatically.");
        controller.Enabled = true;
        Advance(9.999);
        Assert(transitions == 0, "Enabling must start a full ten-second countdown.");
        Advance(0.001);
        Assert(transitions == 1 && fullScreen, "Ten seconds of inactivity must enter full screen.");
        Advance(60);
        Assert(transitions == 1, "An already full-screen window must not enter again.");

        // F11/Escape and window activation explicitly reset the countdown.
        fullScreen = false;
        controller.Reset();
        Advance(9);
        Assert(transitions == 1, "Manually leaving full screen must give the user a fresh countdown.");
        controller.RecordActivity();
        Advance(9);
        Assert(transitions == 1, "Input must postpone the deadline by another ten seconds.");
        Advance(1);
        Assert(transitions == 2, "Full screen must still enter after the last input has been idle for ten seconds.");

        fullScreen = false;
        controller.Reset();
        for (int i = 0; i < 100; i++)
        {
            Advance(1);
            controller.RecordActivity();
        }
        Assert(transitions == 2, "Continuous input must keep the window windowed.");

        // The host excludes inactive/minimized windows, modal dialogs, held
        // gestures and pending close requests. None may run a stale deadline.
        eligible = false;
        Advance(60);
        Assert(transitions == 2, "An ineligible window must not enter full screen.");
        eligible = true;
        controller.Reset();
        Advance(9);
        Assert(transitions == 2, "Returning from a blocked state must allow another full countdown.");
        Advance(1);
        Assert(transitions == 3, "The timer must resume when the window can safely enter full screen.");

        fullScreen = false;
        controller.Reset();
        Advance(9);
        controller.Enabled = false;
        Advance(60);
        Assert(transitions == 3, "Disabling must cancel a pending transition.");
        controller.Enabled = true;
        Advance(9);
        controller.Enabled = true;
        Advance(1);
        Assert(transitions == 4, "Re-enabling starts fresh; reapplying an unchanged setting must not restart it.");

        fullScreen = false;
        controller.Reset();
        Advance(9);
        controller.Dispose();
        controller.Dispose();
        controller.RecordActivity();
        controller.Reset();
        controller.Enabled = true;
        Advance(60);
        Assert(transitions == 4 && !controller.Enabled,
            "Shutdown must cancel pending ticks permanently and allow repeated cleanup.");

        void Advance(double seconds)
        {
            clock.Advance(TimeSpan.FromSeconds(seconds));
            controller.CheckInactivity();
        }
    }

    private static void CheckDispatcherTimer()
    {
        var frame = new DispatcherFrame();
        int transitions = 0;
        using var controller = new AutoFullScreenController(() => true, () =>
        {
            transitions++;
            frame.Continue = false;
        });
        var timeout = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(30),
        };
        timeout.Tick += (_, _) => frame.Continue = false;
        var elapsed = Stopwatch.StartNew();
        controller.Enabled = true;
        timeout.Start();
        try
        {
            Dispatcher.PushFrame(frame);
            Assert(transitions == 1 && elapsed.Elapsed >= TimeSpan.FromSeconds(10),
                "The real WPF timer must invoke the transition after ten seconds, without polling from the host.");
        }
        finally { timeout.Stop(); }
    }

    private sealed class Clock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(TimeSpan elapsed) => _ticks += elapsed.Ticks;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
