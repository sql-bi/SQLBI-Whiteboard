using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace SQLBI.Whiteboard.SmokeTests;

internal static class WindowCloseSmokeTests
{
    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                ReproduceReentrantClose();
                CheckSynchronousPreparation();
                CheckPendingPreparation();
                CheckCancellation();
                CheckFailure();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("Window close smoke tests failed.", failure);
    }

    private static Window HiddenWindow()
    {
        var window = new Window { ShowInTaskbar = false, Width = 100, Height = 100 };
        // A real HWND exercises WPF's closing state without showing a window or
        // opening the application, session store, or the user's board.
        _ = new WindowInteropHelper(window).EnsureHandle();
        return window;
    }

    private static void ReproduceReentrantClose()
    {
        var window = HiddenWindow();
        InvalidOperationException? failure = null;
        CancelEventHandler handler = (_, args) =>
        {
            args.Cancel = true;
            try { window.Close(); }
            catch (InvalidOperationException exception) { failure = exception; }
        };
        window.Closing += handler;
        window.Close();
        window.Closing -= handler;
        window.Close();
        Assert(failure is not null, "Calling Close inside Closing must reproduce the original WPF exception.");
    }

    private static void CheckSynchronousPreparation()
    {
        using var host = new CloseHost(() => Task.FromResult(true));
        host.Window.Close();
        Assert(host.Request.IsPending && host.Prepared == 0 && !host.IsClosed && host.CleanedUp == 0,
            "The original Closing event must return before preparation, even for an already-completed task.");
        // A second click before the dispatcher continuation must not queue another exit.
        host.Window.Close();
        PumpUntil(() => host.IsClosed);
        Assert(host.Prepared == 1 && host.CleanedUp == 1 && host.Errors == 0,
            "Synchronous preparation must close and clean up exactly once without reentrancy.");
    }

    private static void CheckPendingPreparation()
    {
        var saved = new TaskCompletionSource<bool>();
        using var host = new CloseHost(() => saved.Task);
        host.Window.Close();
        PumpUntil(() => host.Prepared == 1);
        host.Window.Close();
        host.Window.Close();
        Assert(host.Request.IsPending && !host.IsClosed && host.Prepared == 1 && host.CleanedUp == 0,
            "Repeated close requests during a save must not start another save or dispose resources.");
        saved.SetResult(true);
        PumpUntil(() => host.IsClosed);
        Assert(host.Prepared == 1 && host.CleanedUp == 1 && host.Errors == 0,
            "An asynchronous save should lead to one final close.");
    }

    private static void CheckCancellation()
    {
        bool allow = false;
        using var host = new CloseHost(() => Task.FromResult(allow));
        host.Window.Close();
        PumpUntil(() => !host.Request.IsPending);
        Assert(!host.IsClosed && !host.Confirmed && host.CleanedUp == 0,
            "Cancel, or a save that did not complete, must leave the board and its resources open.");
        allow = true;
        host.Window.Close();
        PumpUntil(() => host.IsClosed);
        Assert(host.Prepared == 2 && host.CleanedUp == 1 && host.Errors == 0,
            "After cancellation the next close request must be allowed to try again.");
    }

    private static void CheckFailure()
    {
        foreach (bool synchronous in new[] { true, false })
        {
            using var host = new CloseHost(() => synchronous
                ? throw new InvalidOperationException("Test preparation failure")
                : Task.FromException<bool>(new InvalidOperationException("Test preparation failure")));
            host.Window.Close();
            PumpUntil(() => host.IsClosed);
            Assert(host.Errors == 1 && host.CleanedUp == 1 && !host.Request.IsPending,
                "The existing failure fallback must also retry Close outside the original Closing event.");
        }
    }

    private sealed class CloseHost : IDisposable
    {
        private readonly Func<Task<bool>> _prepare;
        private bool _insideClosing;

        public CloseHost(Func<Task<bool>> prepare)
        {
            _prepare = prepare;
            Window = HiddenWindow();
            Window.Closing += Closing;
            Window.Closed += (_, _) => IsClosed = true;
        }

        public Window Window { get; }
        public DeferredCloseRequest Request { get; } = new();
        public bool IsClosed { get; private set; }
        public bool Confirmed { get; private set; }
        public int Prepared { get; private set; }
        public int CleanedUp { get; private set; }
        public int Errors { get; private set; }

        private void Closing(object? sender, CancelEventArgs args)
        {
            _insideClosing = true;
            try { HandleClosing(args); }
            finally { _insideClosing = false; }
        }

        // This is the same two-pass contract as MainWindow, but uses no settings,
        // files, capture devices, or visible confirmation dialogs.
        private async void HandleClosing(CancelEventArgs args)
        {
            if (Confirmed)
            {
                CleanedUp++;
                return;
            }
            args.Cancel = true;
            bool proceed;
            try
            {
                proceed = await Request.PrepareAsync(() =>
                {
                    Assert(!_insideClosing, "Preparation must not run inside the initial Closing event.");
                    Prepared++;
                    return _prepare();
                });
            }
            catch
            {
                Errors++;
                proceed = true;
            }
            if (proceed)
            {
                Confirmed = true;
                Window.Close();
            }
        }

        public void Dispose()
        {
            Window.Closing -= Closing;
            if (!IsClosed) Window.Close();
        }
    }

    private static void PumpUntil(Func<bool> condition)
    {
        if (condition()) return;
        var frame = new DispatcherFrame();
        var elapsed = Stopwatch.StartNew();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(10),
        };
        timer.Tick += (_, _) =>
        {
            if (condition() || elapsed.Elapsed > TimeSpan.FromSeconds(5)) frame.Continue = false;
        };
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        Assert(condition(), "The deferred close did not finish within five seconds.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
