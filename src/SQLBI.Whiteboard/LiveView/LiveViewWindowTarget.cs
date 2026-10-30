using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Graphics.Capture;
using WinRT;

namespace SQLBI.Whiteboard.LiveView;

internal sealed record LiveViewWindowTarget(nint Handle, uint ProcessId, string Title)
{
    public bool IsAvailable => IsWindow(Handle) &&
        GetWindowThreadProcessId(Handle, out var processId) != 0 && processId == ProcessId;

    public bool Activate() => IsAvailable && ActivateWindow(Handle);

    public static bool ActivateWindow(nint handle)
    {
        if (!IsWindow(handle)) return false;
        if (IsIconic(handle)) ShowWindow(handle, 9);
        // Activating the existing HWND lets Windows select its virtual desktop.
        return SetForegroundWindow(handle);
    }

    public static IReadOnlyList<LiveViewWindowTarget> Enumerate()
    {
        List<LiveViewWindowTarget> windows = [];
        EnumWindows((handle, _) =>
        {
            GetWindowThreadProcessId(handle, out uint processId);
            if (processId == Environment.ProcessId || !IsWindowVisible(handle)) return true;
            var title = new StringBuilder(1024);
            GetWindowText(handle, title, title.Capacity);
            if (title.Length > 0) windows.Add(new(handle, processId, title.ToString()));
            return true;
        }, 0);
        return windows.OrderBy(window => window.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public static LiveViewWindowTarget? TryMatch(
        GraphicsCaptureItem selected, IEnumerable<LiveViewWindowTarget>? candidates = null)
    {
        try
        {
            // A display and a window can have the same label and dimensions.
            bool displayMatch = false;
            bool monitorReadFailed = false;
            bool enumerated = EnumDisplayMonitors(0, 0, (monitor, _, _, _) =>
            {
                try
                {
                    var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
                    var iid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
                    var pointer = interop.CreateForMonitor(monitor, ref iid);
                    GraphicsCaptureItem item;
                    try { item = GraphicsCaptureItem.FromAbi(pointer); }
                    finally { Marshal.Release(pointer); }
                    try { displayMatch |= SameDescription(selected, item); }
                    finally { WinRtThreading.Release(item); }
                    return !displayMatch;
                }
                catch (Exception exception)
                {
                    Debug.WriteLine($"[LiveView] Could not identify display: {exception}");
                    monitorReadFailed = true;
                    return false;
                }
            }, 0);
            return displayMatch || monitorReadFailed || !enumerated
                ? null : FindUniqueMatch(selected, candidates ?? Enumerate());
        }
        catch (Exception exception)
        {
            // Matching is optional; failure must not interrupt the selected capture.
            Debug.WriteLine($"[LiveView] Could not identify the selected window: {exception}");
            return null;
        }
    }

    internal static LiveViewWindowTarget? FindUniqueMatch(
        GraphicsCaptureItem selected, IEnumerable<LiveViewWindowTarget> candidates)
    {
        if (string.IsNullOrWhiteSpace(selected.DisplayName)) return null;
        LiveViewWindowTarget? match = null;
        foreach (var candidate in candidates)
        {
            if (!string.Equals(candidate.Title, selected.DisplayName, StringComparison.Ordinal)) continue;
            // Query capture dimensions instead of window bounds, which include different borders.
            var item = candidate.CreateCaptureItem();
            try
            {
                if (!SameDescription(selected, item)) continue;
                if (match is not null) return null;
                match = candidate;
            }
            finally { WinRtThreading.Release(item); }
        }
        return match is { IsAvailable: true } ? match : null;
    }

    private static bool SameDescription(GraphicsCaptureItem left, GraphicsCaptureItem right) =>
        string.Equals(left.DisplayName, right.DisplayName, StringComparison.Ordinal) &&
        left.Size.Width == right.Size.Width && left.Size.Height == right.Size.Height;

    public GraphicsCaptureItem CreateCaptureItem()
    {
        if (!IsAvailable) throw new InvalidOperationException("The selected window has closed. Choose another window.");
        var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        var iid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
        nint pointer = interop.CreateForWindow(Handle, ref iid);
        try { return GraphicsCaptureItem.FromAbi(pointer); }
        finally { Marshal.Release(pointer); }
    }

    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        nint CreateForWindow(nint window, ref Guid iid);
        nint CreateForMonitor(nint monitor, ref Guid iid);
    }

    private delegate bool EnumWindowsCallback(nint window, nint parameter);
    private delegate bool EnumMonitorCallback(nint monitor, nint deviceContext, nint rectangle, nint parameter);
    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(nint deviceContext, nint clip, EnumMonitorCallback callback, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint window, StringBuilder text, int count);
}
