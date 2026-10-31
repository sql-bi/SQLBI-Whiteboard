using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using SQLBI.Whiteboard.Core.Settings;

namespace SQLBI.Whiteboard.LiveView;

internal sealed class LiveViewHotkey : IDisposable
{
    private const int HotkeyId = 0x5742;
    private const uint NoRepeat = 0x4000;
    private const uint Alt = 0x0001;
    private const uint Control = 0x0002;
    private const uint Shift = 0x0004;
    private readonly HwndSource _source;
    private readonly Action _pressed;
    private (uint Modifiers, uint Key)? _registered;

    public LiveViewHotkey(nint window, Action pressed)
    {
        _source = HwndSource.FromHwnd(window) ?? throw new InvalidOperationException("Whiteboard has no window handle.");
        _pressed = pressed;
        _source.AddHook(WindowMessage);
    }

    public bool IsRegistered => _registered is not null;

    public static string Label(LiveViewShortcut shortcut) => shortcut switch
    {
        LiveViewShortcut.ControlAltPeriod => "Ctrl+Alt+.",
        LiveViewShortcut.ControlAltJ => "Ctrl+Alt+J",
        _ => "Ctrl+Alt+Shift+W",
    };

    // Windows-key combinations belong to the OS.
    private static (uint Modifiers, uint Key) Keys(LiveViewShortcut shortcut) => shortcut switch
    {
        LiveViewShortcut.ControlAltPeriod => (Control | Alt, 0xBE),
        LiveViewShortcut.ControlAltJ => (Control | Alt, 0x4A),
        _ => (Control | Alt | Shift, 0x57),
    };

    public void Register(LiveViewShortcut shortcut)
    {
        var (modifiers, key) = Keys(shortcut);
        Register(modifiers, key, Label(shortcut));
    }

    public void Register(uint modifiers, uint key, string label)
    {
        if (_registered == (modifiers, key)) return;
        Unregister();
        if (!RegisterHotKey(_source.Handle, HotkeyId, modifiers | NoRepeat, key))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"{label} is already in use. Release that shortcut in the other application, or choose another one in Preferences.");
        _registered = (modifiers, key);
    }

    public void Unregister()
    {
        if (_registered is null) return;
        UnregisterHotKey(_source.Handle, HotkeyId);
        _registered = null;
    }

    private nint WindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0312 && wParam == HotkeyId)
        {
            handled = true;
            _pressed();
        }
        return 0;
    }

    public void Dispose()
    {
        Unregister();
        _source.RemoveHook(WindowMessage);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(nint window, int id);
}
