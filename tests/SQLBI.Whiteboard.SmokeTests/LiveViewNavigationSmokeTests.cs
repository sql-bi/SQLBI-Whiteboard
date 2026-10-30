using System.ComponentModel;
using System.Windows.Interop;
using SQLBI.Whiteboard.LiveView;

namespace SQLBI.Whiteboard.SmokeTests;

internal static class LiveViewNavigationSmokeTests
{
    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var window = new HwndSource(new HwndSourceParameters("LiveView navigation test")
                {
                    Width = 200,
                    Height = 100,
                    WindowStyle = 0x10CF0000,
                    ExtendedWindowStyle = 0x08000000,
                    PositionX = -20000,
                    PositionY = -20000,
                });
                var target = new LiveViewWindowTarget(window.Handle, (uint)Environment.ProcessId, "LiveView navigation test");
                Assert(target.IsAvailable, "The selected window must retain its identity.");
                Assert(!(target with { ProcessId = 0 }).IsAvailable,
                    "A reused handle owned by another process must not be activated.");
                Assert(!new LiveViewWindowTarget(0, 0, "Closed").Activate(),
                    "A closed target must fail without activating another window.");
                WinRtThreading.EnsureDispatcherQueue();
                var item = target.CreateCaptureItem();
                try
                {
                    Assert(item.DisplayName == "LiveView navigation test",
                        "The capture item must refer to the selected HWND.");
                    Assert(LiveViewWindowTarget.TryMatch(item, [target]) == target,
                        "A unique title and capture size must enable navigation.");
                    Assert(LiveViewWindowTarget.TryMatch(item, []) is null,
                        "No matching window must leave navigation unavailable.");
                    Assert(LiveViewWindowTarget.TryMatch(item, [target with { Title = "Different title" }]) is null,
                        "Matching dimensions alone must not identify a window.");
                    using var duplicate = new HwndSource(new HwndSourceParameters("LiveView navigation test")
                    {
                        Width = 200, Height = 100, WindowStyle = 0x10CF0000,
                        ExtendedWindowStyle = 0x08000000, PositionX = -20000, PositionY = -20000,
                    });
                    var duplicateTarget = new LiveViewWindowTarget(duplicate.Handle, (uint)Environment.ProcessId, target.Title);
                    Assert(LiveViewWindowTarget.TryMatch(item, [target, duplicateTarget]) is null,
                        "Identical titles and dimensions must remain ambiguous.");
                    using var larger = new HwndSource(new HwndSourceParameters("LiveView navigation test")
                    {
                        Width = 400, Height = 300, WindowStyle = 0x10CF0000,
                        ExtendedWindowStyle = 0x08000000, PositionX = -20000, PositionY = -20000,
                    });
                    var largerTarget = new LiveViewWindowTarget(larger.Handle, (uint)Environment.ProcessId, target.Title);
                    Assert(LiveViewWindowTarget.TryMatch(item, [largerTarget]) is null,
                        "Matching titles alone must not identify a window.");
                    Assert(LiveViewWindowTarget.TryMatch(item, [largerTarget, target]) == target,
                        "Capture size must distinguish windows sharing a title.");
                    duplicate.Dispose();
                    Assert(LiveViewWindowTarget.TryMatch(item, [target, duplicateTarget]) is null,
                        "A candidate closing during matching must not interrupt capture or cause a guess.");
                    Assert(item.Size.Width > 0,
                        "Failed matching must leave the selected capture item usable.");
                }
                finally { WinRtThreading.Release(item); }

                using var otherWindow = new HwndSource(new HwndSourceParameters("LiveView shortcut test"));
                using var shortcut = new LiveViewReturnShortcut(window.Handle, () => { });
                using var otherShortcut = new LiveViewReturnShortcut(otherWindow.Handle, () => { });
                // Use Ctrl+Alt+Shift+F24 so the running application can keep Ctrl+Alt+J.
                shortcut.Register(0x4007, 0x87);
                shortcut.Register(0x4007, 0x87);
                bool conflict = false;
                try { otherShortcut.Register(0x4007, 0x87); }
                catch (Win32Exception) { conflict = true; }
                Assert(conflict, "A shortcut owned by another window must report a conflict.");
                shortcut.Dispose();
                otherShortcut.Register(0x4007, 0x87);
                otherShortcut.Dispose();
                window.Dispose();
                Assert(!target.IsAvailable, "Closing the selected window must invalidate the jump target.");
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(30))) throw new TimeoutException("LiveView navigation tests timed out.");
        if (failure is not null) throw new InvalidOperationException("LiveView navigation tests failed.", failure);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
