using System.Windows.Threading;
using SQLBI.Whiteboard.LiveView;

namespace SQLBI.Whiteboard.SmokeTests;

internal static class LiveViewFocusSmokeTests
{
    public static void Run()
    {
        var playing = new LiveViewPlaybackState();
        var alsoPlaying = new LiveViewPlaybackState();
        var manuallyPaused = new LiveViewPlaybackState { IsFrozen = true };
        var views = new[] { playing, alsoPlaying, manuallyPaused };
        for (var cycle = 0; cycle < 100; cycle++)
        {
            foreach (var state in views)
            {
                Assert(state.SetSuspended(true) && !state.SetSuspended(true) && !state.CanCapture,
                    "Focus loss should suspend each view once, even with repeated notifications.");
            }

            Assert(!playing.IsFrozen && !alsoPlaying.IsFrozen && manuallyPaused.IsFrozen,
                "A background pause must not change the manual or saved frozen state.");
            foreach (var state in views)
            {
                Assert(state.SetSuspended(false) && !state.SetSuspended(false),
                    "Returning or disabling the preference should remove suspension once.");
            }

            Assert(playing.CanCapture && alsoPlaying.CanCapture && !manuallyPaused.CanCapture,
                "Only previously playing views may capture again on return.");
        }

        playing.SetSuspended(true);
        playing.IsFrozen = true;
        playing.SetSuspended(false);
        Assert(!playing.CanCapture, "A manual pause or target closure during suspension must stay paused.");
        playing.SetSuspended(true);
        playing.IsFrozen = false;
        Assert(!playing.CanCapture, "A resume or new target must still obey background suspension.");
        playing.SetSuspended(false);
        Assert(playing.CanCapture, "A newly connected playing target may capture on return.");

        // Exercise the real session lifecycle without asking for capture permission or a GPU.
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var capture = new LiveViewCaptureSession(Dispatcher.CurrentDispatcher);
                capture.SetSuspended(true);
                capture.SetSuspended(true);
                Assert(capture.IsSuspended && !capture.IsFrozen && !capture.HasTarget,
                    "Suspension must work before the target or presentation device exists.");
                capture.ClearTarget();
                capture.SetSuspended(false);
                Assert(!capture.IsSuspended && capture.IsFrozen && !capture.HasTarget,
                    "Resuming focus must not reconnect a cleared target.");
                capture.SetSuspended(true);
                capture.Dispose();
                capture.Dispose();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new InvalidOperationException("LiveView focus lifecycle tests failed.", failure);
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
