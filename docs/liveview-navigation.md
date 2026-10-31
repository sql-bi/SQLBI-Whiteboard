# LiveView window navigation

Implemented locally: the third on-container button activates the source window.
One system-wide shortcut, Ctrl+Alt+Shift+W by default, does the same from Whiteboard
and returns to the existing Whiteboard window from anywhere else, resuming the feed.

## Window identity and desktops

The original Windows capture picker is used for both windows and displays. After
selection, Whiteboard compares the capture title and pixel dimensions with open
windows. Candidate dimensions come from temporary capture items, so window borders
and DPI do not require approximate size comparisons. No additional capture session
is started. Windows on other virtual desktops are included in enumeration.

A single matching candidate enables navigation. Duplicate matches, changed titles,
resizing, or errors leave navigation disabled without interrupting capture. Display
capture descriptions are checked first to avoid linking a display to a window with
an identical title and size. This is a heuristic: a unique description is not proof
of identity, and a window changing while matching can make the result inconclusive.

Once matched, the HWND and process ID are retained for the capture session; later
title changes do not trigger another search. The association is cleared on disconnect
or target closure. Reopening a board still requires Reconnect, which runs the same
picker and matching process again.

Navigation restores a minimized window and calls `SetForegroundWindow`. Windows
selects the virtual desktop containing that existing window. Neither source nor
Whiteboard is moved to another desktop. This follows the window's current desktop;
if the user moves it during the visit, the return follows its new location.

## Shortcut

`RegisterHotKey` delivers the shortcut to Whiteboard whichever window is in front,
so one key serves both directions. When Whiteboard's main window is active, it goes
to the selected LiveView's source, then the last one visited, then the only LiveView
with a source window. When another application is active, it returns to Whiteboard
and resumes the LiveView last visited. It does nothing while a Whiteboard dialog or
a label or shape text edit is active.

A registered hotkey takes the keys from every other application, including shortcuts
an application handles in its own window, which `RegisterHotKey` cannot detect. The
shortcut is therefore registered only while at least one LiveView has an available
source window, or a visit is under way, and released otherwise. A visit keeps it
registered when the source closes, so the return still works. A registration conflict
is silent when a LiveView connects, reported by **Go to source window** before leaving
the board, and reported when the shortcut is changed in Preferences.

**Preferences → Live View → Source window shortcut** offers a fixed list:
Ctrl+Alt+Shift+W, Ctrl+Alt+., and Ctrl+Alt+J. A list, rather than recording any
combination, avoids having to reject Whiteboard's own shortcuts, Windows-key
combinations reserved by the operating system, and Ctrl+Alt combinations that type a
character with AltGr on the current keyboard layout. Ctrl+Alt+Shift+W is the default
because three modifiers keep it out of AltGr typing and out of most applications'
shortcut tables. Ctrl+Alt+J, for example, is used by Visual Studio.

Jumping resumes a frozen source. During the visit, that feed is exempt from
**Pause when Whiteboard loses focus**, so edits can be captured before returning
to another desktop. Other feeds retain the preference. Activating Whiteboard ends
the exemption. Frame delivery still depends on the source application and Windows
Graphics Capture; activation does not guarantee that a new frame has already arrived.

## Validation

The default smoke suite checks matching with supplied capture descriptions, including
unique matches, duplicates, size mismatches, title changes, and inaccessible candidates.
It also checks native window identity, closed-window rejection, and hotkey registration
conflicts and cleanup. These checks do not require Windows Graphics Capture.

Real capture tests require an interactive Windows desktop and are explicitly enabled:

```powershell
dotnet run --project tests/SQLBI.Whiteboard.SmokeTests/SQLBI.Whiteboard.SmokeTests.csproj -c Release -- --liveview-navigation-capture
```

They create offscreen windows without activating them, verify capture and matching,
and check that failure leaves the selected capture item usable. GitHub's hosted runner
rejected capture-item creation with E_INVALIDARG, so this integration check is not part
of the default CI suite. Neither suite changes virtual desktops.

Interactive checks still required on Windows 11:

1. Capture a window with the Windows picker, jump with the shortcut, edit its contents,
   and return with the same shortcut. Repeat with the button for the first jump.
2. Repeat with the source and Whiteboard on different virtual desktops. Confirm both
   jumps follow the appropriate desktop and the edit appears in the feed.
3. Repeat with a minimized source, a frozen LiveView, and background capture pausing on.
4. Close the source during the visit. The shortcut must still return to Whiteboard.
5. Choose a display through the system picker. Freeze and Resume must still work;
   the jump button must stay disabled. Repeat with two windows whose titles and dimensions match.
6. Change the shortcut in Preferences with a LiveView connected. The new keys must work
   at once, the old ones must reach other applications again, and the button tooltip
   must name the new keys.
7. Disconnect every LiveView, then close Whiteboard, and confirm another instance or
   application can register the shortcut each time.

References: [foreground activation and virtual desktops](https://devblogs.microsoft.com/oldnewthing/20171002-00/?p=97116),
[capture by HWND](https://learn.microsoft.com/en-us/windows/win32/api/windows.graphics.capture.interop/nf-windows-graphics-capture-interop-igraphicscaptureiteminterop-createforwindow),
[global hotkey registration](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey).
