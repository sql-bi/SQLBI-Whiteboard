using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace SQLBI.Whiteboard;

internal static class MonitorStartupPlacement
{
    private const uint EddGetDeviceInterfaceName = 0x00000001;
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    public static void PlaceMaximizedOnWacom(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var target = EnumerateMonitors().FirstOrDefault(monitor =>
            IsWacomMonitorName(monitor.MonitorName));
        if (target is null)
        {
            Debug.WriteLine(
                "[Startup] No Wacom/Cintiq monitor name was found; maximizing on the Windows-selected monitor.");
            window.WindowState = WindowState.Maximized;
            return;
        }

        Debug.WriteLine(
            $"[Startup] Using {target.MonitorName} on {target.DeviceName}.");
        var windowHandle = new WindowInteropHelper(window).Handle;
        var area = target.WorkingArea;
        SetWindowPos(
            windowHandle,
            0,
            area.Left,
            area.Top,
            Math.Max(1, area.Right - area.Left),
            Math.Max(1, area.Bottom - area.Top),
            SwpNoZOrder | SwpNoActivate);
        window.WindowState = WindowState.Maximized;
    }

    public static void FillCurrentMonitor(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var windowHandle = new WindowInteropHelper(window).Handle;
        var monitorHandle = MonitorFromWindow(windowHandle, MonitorDefaultToNearest);
        if (monitorHandle == 0)
        {
            window.WindowState = WindowState.Maximized;
            return;
        }

        var monitorInfo = new NativeMonitorInfo
        {
            Size = (uint)Marshal.SizeOf<NativeMonitorInfo>(),
        };
        if (!GetMonitorInfo(monitorHandle, ref monitorInfo))
        {
            window.WindowState = WindowState.Maximized;
            return;
        }

        var area = monitorInfo.MonitorArea;
        SetWindowPos(
            windowHandle,
            0,
            area.Left,
            area.Top,
            Math.Max(1, area.Right - area.Left),
            Math.Max(1, area.Bottom - area.Top),
            SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
    }

    private static IReadOnlyList<MonitorDescriptor> EnumerateMonitors()
    {
        var monitors = new List<MonitorDescriptor>();
        EnumDisplayMonitors(
            0,
            0,
            (monitorHandle, _, _, _) =>
            {
                var monitorInfo = new NativeMonitorInfo
                {
                    Size = (uint)Marshal.SizeOf<NativeMonitorInfo>(),
                };
                if (GetMonitorInfo(monitorHandle, ref monitorInfo))
                {
                    monitors.Add(new MonitorDescriptor(
                        monitorInfo.DeviceName,
                        ReadMonitorName(monitorInfo.DeviceName),
                        monitorInfo.WorkingArea));
                }

                return true;
            },
            0);
        return monitors;
    }

    private static bool IsWacomMonitorName(string? monitorName) =>
        !string.IsNullOrWhiteSpace(monitorName) &&
        (monitorName.Contains("Wacom", StringComparison.OrdinalIgnoreCase) ||
         monitorName.Contains("Cintiq", StringComparison.OrdinalIgnoreCase));

    private static string? ReadMonitorName(string displayDeviceName)
    {
        try
        {
            var displayDevice = new NativeDisplayDevice
            {
                Size = Marshal.SizeOf<NativeDisplayDevice>(),
            };
            if (!EnumDisplayDevices(
                    displayDeviceName,
                    0,
                    ref displayDevice,
                    EddGetDeviceInterfaceName))
            {
                return null;
            }

            var instanceId = ToMonitorInstanceId(displayDevice.DeviceId);
            if (instanceId is null)
            {
                return null;
            }

            using var instanceKey = Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Enum\{instanceId}");
            using var parametersKey = instanceKey?.OpenSubKey("Device Parameters");
            if (parametersKey?.GetValue("EDID") is byte[] edid &&
                ReadEdidMonitorName(edid) is { } edidName)
            {
                return edidName;
            }

            return NormalizeRegistryMonitorName(
                instanceKey?.GetValue("FriendlyName") as string);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                $"[Startup] Could not read the monitor name for {displayDeviceName}: {exception.Message}");
            return null;
        }
    }

    private static string? ToMonitorInstanceId(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return null;
        }

        if (deviceId.StartsWith("MONITOR\\", StringComparison.OrdinalIgnoreCase))
        {
            return "DISPLAY\\" + deviceId["MONITOR\\".Length..];
        }

        var interfaceParts = deviceId.Split('#');
        return interfaceParts.Length >= 3
            ? $@"DISPLAY\{interfaceParts[1]}\{interfaceParts[2]}"
            : null;
    }

    private static string? ReadEdidMonitorName(byte[] edid)
    {
        const int descriptorStart = 54;
        const int descriptorLength = 18;
        const byte monitorNameDescriptor = 0xFC;

        for (var offset = descriptorStart;
             offset + descriptorLength <= edid.Length && offset < 126;
             offset += descriptorLength)
        {
            if (edid[offset] != 0 ||
                edid[offset + 1] != 0 ||
                edid[offset + 2] != 0 ||
                edid[offset + 3] != monitorNameDescriptor)
            {
                continue;
            }

            var name = Encoding.ASCII
                .GetString(edid, offset + 5, 13)
                .Trim('\0', '\r', '\n', ' ');
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }

        return null;
    }

    private static string? NormalizeRegistryMonitorName(string? registryName)
    {
        if (string.IsNullOrWhiteSpace(registryName))
        {
            return null;
        }

        var finalSegment = registryName
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault()?
            .Trim()
            .Trim('(', ')');
        return string.IsNullOrWhiteSpace(finalSegment) ? null : finalSegment;
    }

    private sealed record MonitorDescriptor(
        string DeviceName,
        string? MonitorName,
        NativeRectangle WorkingArea);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeDisplayDevice
    {
        public int Size;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        public int StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeMonitorInfo
    {
        public uint Size;
        public NativeRectangle MonitorArea;
        public NativeRectangle WorkingArea;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    private delegate bool MonitorEnumerationProcedure(
        nint monitorHandle,
        nint deviceContext,
        nint monitorRectangle,
        nint data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(
        nint deviceContext,
        nint clipRectangle,
        MonitorEnumerationProcedure callback,
        nint data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(
        nint monitorHandle,
        ref NativeMonitorInfo monitorInfo);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(
        string deviceName,
        uint deviceNumber,
        ref NativeDisplayDevice displayDevice,
        uint flags);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(
        nint windowHandle,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint windowHandle,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}
