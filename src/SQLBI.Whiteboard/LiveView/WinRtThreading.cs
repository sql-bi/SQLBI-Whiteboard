using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.System;
using WinRT;

namespace SQLBI.Whiteboard.LiveView;

/// <summary>
/// WinRT Graphics Capture RCWs are apartment-bound in a packaged (Store)
/// process. Releasing them from the GC finalizer is the AccessViolation
/// that closes the app. Keep a DispatcherQueue on the UI thread so
/// <see cref="Windows.Graphics.Capture.Direct3D11CaptureFramePool.Create"/>
/// delivers frames there, and Release every RCW on that same thread.
/// </summary>
internal static class WinRtThreading
{
    // Native ref kept for the process lifetime. Wrapping it in a CsWinRT
    // RCW would only recreate the finalizer problem this exists to avoid.
    private static nint _dispatcherQueueController;

    public static void EnsureDispatcherQueue()
    {
        if (DispatcherQueue.GetForCurrentThread() is not null)
        {
            return;
        }

        DispatcherQueueOptions options = new()
        {
            dwSize = Marshal.SizeOf<DispatcherQueueOptions>(),
            threadType = 2, // DQTYPE_THREAD_CURRENT
            apartmentType = 0, // DQTAT_COM_NONE — WPF already initialized STA
        };

        int hr = CreateDispatcherQueueController(options, out nint pointer);
        Marshal.ThrowExceptionForHR(hr);
        _dispatcherQueueController = pointer;
    }

    /// <summary>
    /// Close IClosable WinRT objects, then drop the CsWinRT COM pointer on
    /// this thread so <c>IObjectReference.Finalize</c> has nothing to Release.
    /// </summary>
    public static void Release(object? value)
    {
        if (value is null)
        {
            return;
        }

        if (value is IDisposable disposable)
        {
            try
            {
                disposable.Dispose();
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"[LiveView] WinRT Close failed: {exception}");
            }
        }

        if (value is IWinRTObject winrt)
        {
            try
            {
                winrt.NativeObject.Dispose();
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"[LiveView] WinRT Release failed: {exception}");
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions
    {
        public int dwSize;
        public int threadType;
        public int apartmentType;
    }

    [DllImport("CoreMessaging.dll", ExactSpelling = true)]
    private static extern int CreateDispatcherQueueController(
        DispatcherQueueOptions options,
        out nint dispatcherQueueController);
}
