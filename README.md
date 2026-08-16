# SQLBI Whiteboard

A native Windows 11 whiteboard built with C# and WPF. WPF's dedicated dynamic ink renderer owns the live pen stroke, while a retained viewport renders an unbounded world-coordinate document beneath it.

## Included in the application

- Low-latency, pressure-aware WPF wet ink, including Wacom rear-eraser detection
- A normal cursor for physical mouse input and a high-contrast pen-hover dot that disappears on contact
- Touch panning and two-finger pinch zoom
- Basic palm rejection: touch navigation is suspended when the pen makes contact
- Mouse-wheel zoom and middle-button or temporary Space-key panning
- Whole-stroke erasing
- PNG, JPEG, BMP, and GIF import, clipboard bitmap paste, and Explorer drag-and-drop
- Image selection, movement, resizing, and deletion
- Image containers automatically carry strokes that touch only that image when moved or resized
- LiveView containers for GPU-backed capture of an application window or display, with freeze/resume and saved last-frame previews
- Double-click an image to center it and fit it to the canvas
- Undo and redo for strokes, erasing, images, and image transformations
- Versioned ZIP-based `.wboard` documents with embedded image assets
- An intentionally small floating toolbar

## Build and run

Requirements:

- Windows 11
- .NET 8 SDK
- Visual Studio 2022 or newer with the **Windows application development** workload if using the IDE

From PowerShell in the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run.ps1
```

Core-only verification does not require WPF:

```powershell
$env:DOTNET_CLI_HOME = "$PWD\.dotnet"
$env:APPDATA = "$PWD\.appdata"
$env:NUGET_PACKAGES = "$PWD\.packages"
dotnet run --project .\tests\SQLBI.Whiteboard.Core.SmokeTests\SQLBI.Whiteboard.Core.SmokeTests.csproj
```

## Application

`SQLBI.Whiteboard` is the WPF application project. It uses `InkCanvas` only for live wet ink; completed
pressure strokes are converted into `SQLBI.Whiteboard.Core` world coordinates and
rendered by the retained WPF scene layer.

After building the solution, start the application with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run.ps1
```

## LiveView

Choose **Tools > Add LiveView...** and select an application window or display in the Windows capture picker. A LiveView behaves like an imported-image container: it can be selected, moved, resized while preserving its aspect ratio, framed by double-clicking, deleted with its linked strokes, and manipulated through undo/redo.

Use **Tools > Freeze selected LiveView** to stop capture while retaining the last frame. The same command resumes a target that is still available. **Reconnect selected LiveView...** selects a new target while preserving the container, snapshot, and linked strokes.

Saving a board captures the latest LiveView bitmap and stores it with the source label, frame-rate setting, cursor setting, frozen state, and container geometry. Loading a board displays that bitmap immediately. Windows capture permission objects cannot be serialized, so use **Reconnect** to restore the live feed after loading.

## Calligraphy Lab

An isolated calligraphy-tuning prototype is available under `prototypes/SQLBI.Whiteboard.CalligraphyPrototype`. It exposes the nib geometry, pressure curve, speed response, and smoothing parameters directly on the canvas without changing the main whiteboard. Run it with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-calligraphy-prototype.ps1
```

Use **Copy settings** after finding a useful combination so the exact values can be transferred into the main application.

## Controls

| Input | Behavior |
| --- | --- |
| Pen tip | Current tool; Pen is selected at startup |
| Pen hover | Show the small red pointer dot and hide the arrow |
| Pen contact | Hide both the pointer dot and arrow |
| Physical mouse movement | Show the normal arrow |
| Left mouse | Temporarily select/move/resize an image; return to Pen on release |
| Double-click container | Center and fit the image or LiveView to the canvas |
| Double-click empty canvas | Center and fit all board content, or reset an empty board |
| Pen eraser | Erase complete strokes |
| One finger | Pan |
| Two fingers | Pan and pinch zoom |
| Mouse wheel | Zoom at the pointer |
| Middle mouse | Pan |
| Right mouse | Pan while held; return to Pen on release |
| Space | Temporarily switch to Pan |
| Ctrl+Z / Ctrl+Y | Undo / redo |
| Ctrl+V | Paste an image |
| Ctrl+S / Ctrl+O | Save / open a board |
| Delete | Delete the selected container and its linked strokes |
| Tools > Add LiveView | Capture an application window or display as a container |
| Tools > Freeze selected LiveView | Freeze or resume the selected live feed |
| Tools > Reconnect selected LiveView | Select a new capture target for the existing container |

With the mouse, selection is automatic: click an image to move it, or drag the circular bottom-right handle to resize it while preserving its aspect ratio. Double-click an image to center it and fit it to the canvas. Releasing the mouse returns to Pen mode.

Every imported image currently acts as a container. A completed stroke is linked when it touches exactly one image, including crossing its edge; a stroke touching multiple images remains independent. Moving or resizing an image transforms its linked strokes with it. Deleting a container also deletes all of its linked strokes. Undo/redo treats each complete container operation as one action.

Image files can be dropped directly from File Explorer. Their initial center is the board position at which they were dropped.

## Architecture

- `SQLBI.Whiteboard.Core` contains world geometry, camera math, retained board objects, commands, hit testing, and archive persistence. It has no UI-framework dependency.
- `SQLBI.Whiteboard` is the WPF shell. `InkCanvas` supplies system-managed wet ink, while `BoardSurface` renders completed ink, images, and selection on a white canvas in camera space.
- `SQLBI.Whiteboard/LiveView` owns Windows Graphics Capture and the Direct3D-to-WPF bridge. Capture retains one GPU frame per active LiveView; CPU bitmap conversion occurs only when copying or saving a snapshot.
- `SQLBI.Whiteboard.Core.SmokeTests` is a package-free executable test harness for camera anchoring, commands, hit testing, and archive round trips.

The current document query is deliberately linear. A spatial index can be introduced behind `BoardDocument.Query` when profiling demonstrates a need, without changing input, tools, persistence, or rendering call sites.

Live ink uses a transparent WPF `InkCanvas` above the retained scene. WPF renders wet ink on its dedicated dynamic-rendering thread. On stroke completion, pressure points are transformed from screen space into the unbounded world model; camera movement is suspended while the pen is in contact.

## Wacom Cintiq Pro validation

Test these on the target device before tuning stroke algorithms:

1. Draw slowly and quickly at several pressure levels.
2. Confirm the rear eraser removes complete strokes.
3. Rest a palm while drawing and verify the board does not pan.
4. Lift the pen, then immediately pan and pinch with touch.
5. Draw near all display edges and across the Windows display-scaling boundary, if multiple monitors use different scaling.

Wacom driver settings can remap the barrel and eraser controls, so validate both Windows Ink mode and the intended application profile.
