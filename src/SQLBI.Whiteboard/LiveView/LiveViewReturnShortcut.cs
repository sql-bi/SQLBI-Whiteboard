using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace SQLBI.Whiteboard.LiveView;

internal sealed class LiveViewReturnShortcut : IDisposable
{
    private const int HotkeyId = 0x5742;
    private readonly HwndSource _source;
    private readonly Action _return;
    private bool _registered;

    public LiveViewReturnShortcut(nint window, Action returnToBoard)
    {
        _source = HwndSource.FromHwnd(window) ?? throw new InvalidOperationException("Whiteboard has no window handle.");
        _return = returnToBoard;
        _source.AddHook(WindowMessage);
    }

    public void Register(uint modifiers = 0x4003, uint key = 0x4A)
    {
        if (_registered) return;
        // Ctrl+Alt+J, with repeat suppressed. Windows-key combinations belong to the OS.
        if (!RegisterHotKey(_source.Handle, HotkeyId, modifiers, key))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Ctrl+Alt+J is already in use. Release that shortcut in the other application and try again.");
        _registered = true;
    }

    private nint WindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0312 && wParam == HotkeyId)
        {
            handled = true;
            _return();
        }
        return 0;
    }

    public void Dispose()
    {
        if (_registered) UnregisterHotKey(_source.Handle, HotkeyId);
        _registered = false;
        _source.RemoveHook(WindowMessage);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(nint window, int id);
}
