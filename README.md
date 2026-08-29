# SQLBI Whiteboard

A native Windows 11 whiteboard built with C# and WPF. WPF's dedicated dynamic ink renderer owns the live pen stroke, while a retained viewport renders an unbounded world-coordinate document beneath it.

How the project is developed and shipped is documented separately:
[CONTRIBUTING.md](CONTRIBUTING.md) for the working agreement,
[docs/release-management.md](docs/release-management.md) for the process, and
[docs/decisions.md](docs/decisions.md) for the choices behind it.

## Included in the application

- Low-latency, pressure-aware WPF wet ink, including rear-eraser detection on any pen that reports it
- A normal cursor for physical mouse input, and a pen-hover indicator that shows what a tap would do: the laser with its halo and speed trail, a dashed square around what the eraser would clear, and a high-contrast dot for everything else. All of them disappear on contact
- Touch panning and two-finger pinch zoom
- Optional finger drawing (default when no pen is detected): one finger uses the current tool, two fingers still pan and pinch-zoom, and Eraser and Pan appear on the toolbar
- A notice at startup when Windows reports neither a pen tablet nor a touchscreen: the application still opens, but there is nothing to draw with, and [discussion 78](https://github.com/sql-bi/SQLBI-Whiteboard/discussions/78) collects votes for mouse-only drawing. Dismissable from the notice itself or from Preferences, since the tablet list Windows reports can miss a pen that has never been in range
- Basic palm rejection: touch navigation is suspended when the pen makes contact
- Mouse-wheel zoom and middle-button or temporary Space-key panning
- Whole-stroke erasing
- PNG, JPEG, BMP, GIF, and SVG import, clipboard bitmap paste, and Explorer drag-and-drop of images, text files, and `.wimport` recipes
- SVG stays vector: it is stored as its markup and redrawn at every zoom and resize rather than rasterized on arrival. Pasting SVG markup that was copied as text — the output of a DAX SVG measure, for instance — creates a picture, and copying an SVG container puts both the markup and a bitmap on the clipboard
- Image selection, movement, resizing, and deletion
- Text containers created by pasting plain text, with display and in-place edit modes
- Plain-text, DAX, and SQL Server language modes, with live syntax highlighting and local F6 formatting
- Containers automatically carry strokes that touch only that container when moved or resized
- LiveView containers for GPU-backed capture of an application window or display, with freeze/resume and saved last-frame previews
- Double-click a container to center it and fit it to the canvas
- Undo and redo for strokes, erasing, containers, text edits, and transformations
- Versioned ZIP-based `.wboard` documents with an embedded `preview.png`. Explorer shows that picture as the file thumbnail in the released install. The VS Code extension in `vscode/sqlbi-whiteboard` opens the same picture instead of the ZIP.
- Markdown `.wimport` recipes that build image and text containers from headings
- An intentionally small floating toolbar
- A File / Edit / View / Help tab strip. Click a tab for a one-row command strip over the canvas
- Preferences for the startup monitor, full-screen start, finger drawing, the pen button, snippet format order, laser trail timing and weight, toolbar position and layout, and (except Store installs) a daily new-version check
- About, with version and channel

## Build and run

Requirements:

- Windows 11
- .NET 10 SDK
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

## Installers

`installer/wix` contains a single WiX v5 source that produces every installer variant,
selected with the `Channel` and `Scope` preprocessor variables:

| Artifact | Installs to |
| --- | --- |
| `SQLBI.Whiteboard.<version>.x64.msi` | `Program Files\SQLBI\Whiteboard` |
| `SQLBI.Whiteboard.<version>.x64-userinstaller.msi` | `%LOCALAPPDATA%\Programs\SQLBI\Whiteboard`, no elevation |
| `SQLBI.Whiteboard.<version>.x64-dev.msi` | `Program Files\SQLBI\Whiteboard Dev` |
| `SQLBI.Whiteboard.<version>.x64-dev-userinstaller.msi` | `%LOCALAPPDATA%\Programs\SQLBI\Whiteboard Dev`, no elevation |
| `SQLBI.Whiteboard.<version>.x64-portable.zip` | runs without installing |
| `SQLBI.Whiteboard.<version>.x64-dev-portable.zip` | runs without installing, as the pre-release channel |
| `SQLBI.Whiteboard.<version>.x64.msix` | unsigned Store package for the released channel. Identity version is `<version>.0`. The pipeline submits it to Partner Center after a release is promoted; the Store re-signs it. |

### Channels

The released and pre-release channels are separate products, so a tester can keep both
installed. They differ in three ways:

- The pre-release channel installs under `Whiteboard Dev`, is named **SQLBI Whiteboard (Dev)**,
  and carries its own `UpgradeCode`, so it never upgrades or replaces a released install.
- Only the released channel registers the `.wboard` and `.wimport` file types. Uninstalling a
  pre-release build therefore cannot leave those associations broken.
- The pre-release installer places a `channel.txt` beside the executable, and the pre-release
  portable ZIP carries the same file. `AppChannel` reads it at startup to append `(Dev)` to
  the window title and to keep settings in `%APPDATA%\SQLBI\Whiteboard Dev`, so the two
  copies cannot overwrite each other's settings.

Because the channel is detected at run time rather than compiled in, one build of the
application serves both, and a tested binary can be promoted without being rebuilt.

Build all of them locally with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1 -Version 1.0.0
```

Packaging is almost entirely CAB compression, so building every variant is slow. Pass
`-Variants` to restrict it while iterating on the authoring — pull request validation builds
the diagonal pair, which still exercises both sides of every conditional in the source:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1 -Variants stable/perMachine,dev/perUser
```

The WiX toolset is pinned in `.config/dotnet-tools.json`. The script restores it and adds
the UI and Util extensions, so no manual setup is required.

The application and document icons, the installer artwork, and the site's favicons and
social card are all generated from `src/SQLBI.Whiteboard/Assets/SQLBI.Whiteboard.svg` by
`.\scripts\build-assets.ps1`. The generated files are committed, so that script only needs
running when the artwork changes.

`.azure/pipelines/build-whiteboard.yaml` performs the same build in Azure Pipelines and
signs the binaries and both MSI packages with the SQLBI certificate held in Azure Key Vault.
What to run for a pre-release, a full release, the VS Code extension, or the site is the
opening section of [docs/release-management.md](docs/release-management.md).

## Landing page

`site/` is <https://whiteboard.sqlbi.com>: the download landing page plus the public
guide, shortcuts, FAQ, compare, changelog, privacy, `.wimport` contract, and
contribute/publish page. Styles live in `site/styles.css`. Pages are hand-authored
HTML, not generated from this README.

`.github/workflows/publish-site.yml` deploys it to GitHub Pages whenever `site/` changes on
`main`. `site/CNAME` carries the custom domain so it survives each deployment. Asset paths
are relative, so the page also works from the project-site URL before the domain resolves.

Download links are resolved at load time, because the installer file name carries the
version and cannot be hard-coded. The page reads `stable.json`, a release manifest
generated into the same deployment by `scripts/build-release-manifests.ps1`, so an
ordinary visit makes no API call. It falls back to the GitHub releases API when no stable
release is published, which is also the only source that can describe a pre-release to a
browser: `github.com` release-asset URLs send no CORS header, so a manifest attached to a
release cannot be read from a page. Without scripting, or if both fail, every link stays
pointing at the releases page.

`stable.json` and `dev.json` are the same manifests the winget submission and the in-app
update check read. They are generated per deployment, not committed. The schema is in
[docs/release-management.md](docs/release-management.md).

## Application

`SQLBI.Whiteboard` is the WPF application project. It uses `InkCanvas` only for live wet ink; completed
pressure strokes are converted into `SQLBI.Whiteboard.Core` world coordinates and
rendered by the retained WPF scene layer.

After building the solution, start the application with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run.ps1
```

## LiveView

Choose **View → LiveView** and select an application window or display in the Windows capture picker. A LiveView behaves like an imported-image container: it can be selected, moved, resized while preserving its aspect ratio, framed by double-clicking, deleted with its linked strokes, and manipulated through undo/redo.

Use **View > Freeze** to stop capture while retaining the last frame. The same command resumes a target that is still available. **View > Disconnect** releases the target, keeps the last frame, and hides the on-frame freeze/play controls; **View > Reconnect** is then the only way back to a live feed.

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
| Shift + pen tip | Constrain the stroke to horizontal or vertical, whichever is nearer, at a uniform width. Press or release mid-stroke to start or end the constraint from that point. The barrel button does the same when assigned to Straight line |
| Pen hover | Show the small red pointer dot and hide the arrow |
| Pen contact | Hide both the pointer dot and arrow |
| Physical mouse movement | Show the normal arrow |
| Left mouse | Temporarily select/move/resize a container; return to the previous drawing tool on release |
| Double-click container | Center and fit the image, text, or LiveView to the canvas |
| Double-click empty canvas | Center and fit all board content, or reset an empty board |
| Pen eraser | Erase complete strokes. The upper side button erases too: Windows reports it the same way as a pen turned round |
| Pen barrel | Hold the barrel button for the action assigned in Preferences: Laser (default) or Straight line. Laser returns to the previous tool on release |
| One finger | Pan. With Finger drawing on, uses the current tool instead |
| Two fingers | Pan and pinch zoom. Cancels an in-progress finger stroke when Finger drawing is on |
| Mouse wheel | Zoom at the pointer. Shift+wheel zooms more slowly |
| Middle mouse | Pan |
| Right mouse | Pan while held on the canvas; return to Pen on release. Right-click the palette to hide or show it |
| Space | Temporarily switch to Pan |
| Ctrl+Z / Ctrl+Y | Undo / redo |
| Ctrl+C | Copy the selection. Copying a LiveView copies its last frame as a bitmap |
| Ctrl+V | Paste prefers an image (including a file on the clipboard) over text. Otherwise create a text container from plain text |
| F2 | Edit the selected text container |
| Language chip | Choose Plain text, DAX, or SQL Server on a selected text container |
| F6 | Format DAX or SQL on the selected text container. In F2, formats in place |
| Ctrl+Enter | Commit the F2 edit, including an F6 format done in that session, and return to display mode |
| Escape | Cancel the active text edit, close the command strip, or leave full screen or canvas only |
| Ctrl+S / Ctrl+O | Save / open a board |
| Shift+F12 | Save As |
| Delete | Delete the selected container and its linked strokes |
| Alt+L | Laser pointer |
| File / Edit / View / Help | Tab strip. Click a tab for a one-row command strip over the canvas. Click the canvas to hide it |
| Help > Preferences | Searchable settings: startup monitor, full screen, no-pen warning, finger drawing, pen button, snippet format order, laser trail, toolbar, update checks |
| View > Bring to front / Send to back | Reorder the selected image, text, or LiveView (and its linked strokes) |
| Help > About | Version, channel, license, the product site, and a download link when a newer release is known |
| View > LiveView | Capture, freeze, disconnect, or reconnect a window or display |
| F11 | Fill the current monitor and hide title and tabs. Escape leaves it when a text container is not being edited |
| Ctrl+F11 | Hide title and tabs but keep this window’s size and place |

With the mouse, selection is automatic: click a container to move it, or drag the circular bottom-right handle to resize it while preserving its aspect ratio. Double-click a container to center it and fit it to the canvas. Releasing the mouse returns to the previously selected drawing tool.

Imported images, LiveViews, and text objects act as containers. A completed stroke is linked when it touches exactly one container, including crossing its edge; a stroke touching multiple containers remains independent. Moving or resizing a container transforms its linked strokes with it. **View → Bring to front** and **View → Send to back** reorder the selected container and those linked strokes. Deleting a container also deletes all of its linked strokes. Undo/redo treats each complete container operation as one action.

Paste plain text to create a selected text container in display mode. **Help → Preferences** has Snippet format order: paste tries those languages from top to bottom and uses the first that accepts the text. Plain text always accepts, so leaving it first keeps every paste as plain text. Recognized extensions (`.dax`, `.sql`, `.txt`) keep their language; other dropped text files use the same order. Choose **Plain text**, **DAX**, or **SQL Server** from the title-bar chip afterward. Press **F6** to format DAX or SQL on the selected container without entering edit. Press **F2** to edit the body; the same list is in the title bar while editing. In F2, **F6** formats in place; **Ctrl+Enter** commits that edit (including the format) and returns to display. **Escape** restores the previous text, language, and dimensions. Text reflows while its edit-mode resize grip changes the width, and the height grows automatically when necessary. Double-click still centers and fits the container. Syntax highlighting applies in both edit and display modes. A language-aware title identifies a defined DAX or SQL object when possible. SQL Server mode targets SQL Server 2025 T-SQL, preserves `GO` batch separators, and leaves invalid scripts unchanged. In display mode, resizing preserves the aspect ratio and scales the complete text visual without reflowing it.

Image files, and `.txt`, `.dax`, and `.sql` files, can be dropped directly from File Explorer. Their initial center is the board position at which they were dropped. DAX and SQL files open in the matching language mode. Other dropped text files use Snippet format order.

### `.wimport` recipes

A `.wimport` file is Markdown that builds containers on a board. It is import-only: there is no export, and saving always writes a `.wboard`.

- `#` is an optional title (not a container).
- `##` starts one container. The heading is the title.
- An image (`![](path)`), a `dax` / `sql` fence, a link to `.dax` / `.sql` / an image file, or leftover prose chooses the container kind.
- A thematic break (`---`) starts a new row. Items otherwise flow left to right and wrap.
- Paths are local and relative to the `.wimport` file. Missing files are skipped and listed in a dialog.

Drop a `.wimport` onto an open board to add its containers, with the pointer as the group’s top-left. **File → Open** or double-click (released installer) starts a new untitled board from the recipe.

The full contract for authors and agents is [docs/wimport.md](docs/wimport.md). A sample lives at `docs/samples/contoso-workshop.wimport`. In VS Code, associate the extension with Markdown to use the built-in preview:

```json
"files.associations": { "*.wimport": "markdown" }
```

Plain `.md` files are not imported.

A `.wboard` in the same tree opens as the embedded preview if
[SQLBI Whiteboard for VS Code](https://marketplace.visualstudio.com/items?itemName=sqlbi.sqlbi-whiteboard)
is installed. The source is `vscode/sqlbi-whiteboard`.

## Architecture

- `SQLBI.Whiteboard.Core` contains world geometry, camera math, retained board objects, commands, hit testing, and archive persistence. It has no UI-framework dependency.
- `SQLBI.Whiteboard.Dax` contains the framework-neutral DAX lexer, parser, classifier, and deterministic formatter adapted from Prompt Assistant.
- `SQLBI.Whiteboard.SqlServer` contains the framework-neutral SQL Server 2025 adapter over Microsoft's ScriptDOM parser and script generator.
- `vscode/sqlbi-whiteboard` is a VS Code custom editor that shows `preview.png` from a `.wboard` ZIP. It is not part of the desktop installer.
- `SQLBI.Whiteboard` is the WPF shell. `TouchInkCanvas` supplies system-managed wet ink for the finger, while `BoardSurface` renders completed ink, images, text, and selection on a white canvas in camera space, plus the pen's own wet stroke. A transient AvalonEdit surface is overlaid only while a text container is being edited; language services translate parser classifications into WPF text styles.
- `SQLBI.Whiteboard/LiveView` owns Windows Graphics Capture and the Direct3D-to-WPF bridge. Capture retains one GPU frame per active LiveView; CPU bitmap conversion occurs only when copying or saving a snapshot.
- `SQLBI.Whiteboard.Core.SmokeTests` is a package-free executable test harness for camera anchoring, commands, hit testing, and archive round trips.

The current document query is deliberately linear. A spatial index can be introduced behind `BoardDocument.Query` when profiling demonstrates a need, without changing input, tools, persistence, or rendering call sites.

Live ink uses a transparent WPF `TouchInkCanvas` above the retained scene, and it collects
the finger's strokes only: WPF renders those on its dedicated dynamic-rendering thread.
Pen ink is read straight from the pen's packets by `MainWindow.AppendPenInk`, which owns
the contact, the straight-line constraint, and the calligraphy dynamics, and draws the wet
stroke through `BoardSurface.PendingStroke`. A barrel button tears the WPF contact in two
every time it is pressed or released — see [TODO.md](TODO.md) — so no stroke built on that
bookkeeping could behave like the Shift key. In both paths pressure points are transformed
from screen space into the unbounded world model on completion; camera movement is
suspended while the pen is in contact.

## Wacom Cintiq Pro validation

Test these on the target device before tuning stroke algorithms:

1. Draw slowly and quickly at several pressure levels.
2. Confirm the rear eraser removes complete strokes.
3. Rest a palm while drawing and verify the board does not pan.
4. Lift the pen, then immediately pan and pinch with touch.
5. Draw near all display edges and across the Windows display-scaling boundary, if multiple monitors use different scaling.

Wacom driver settings can remap the barrel and eraser controls, so validate both Windows Ink mode and the intended application profile.
