# SQLBI Whiteboard

A native Windows 11 whiteboard built with C# and WPF. WPF's dedicated dynamic ink renderer owns the live pen stroke, while a retained viewport renders an unbounded world-coordinate document beneath it.

How the project is developed and shipped is documented separately:
[CONTRIBUTING.md](CONTRIBUTING.md) for the working agreement,
[docs/release-management.md](docs/release-management.md) for the process, and
[docs/decisions.md](docs/decisions.md) for the choices behind it.

## Included in the application

- Low-latency, pressure-aware WPF wet ink, including rear-eraser detection on any pen that reports it
- A normal cursor for physical mouse input, and a pen-hover indicator that shows what a tap would do: the laser with its halo and speed trail, a dashed square around what the eraser would clear, and a high-contrast dot for everything else. All of them disappear on contact
- Optional mouse drawing (default when Windows reports neither a pen tablet nor a touchscreen): the left button uses the current tool, Ctrl and the left button move and resize a container, and Eraser and Pan appear on the toolbar. A mouse reports no pressure, so ink is drawn at an even width and Calligraphy is the one tool that still varies, because its width comes from speed. Nothing about the pen changes when it is on. With it off, picking a tool from the toolbar with the mouse offers to turn it on, once a session
- Touch panning and two-finger pinch zoom
- Optional finger drawing (default when no pen is detected): one finger uses the current tool, two fingers still pan and pinch-zoom, and Eraser and Pan appear on the toolbar
- A notice at startup when Windows reports neither a pen tablet nor a touchscreen, saying which pointing device the session is drawing with and what a pen would add. Dismissable from the notice itself or from Preferences, since the tablet list Windows reports can miss a pen that has never been in range
- Basic palm rejection: touch navigation is suspended when the pen makes contact
- Mouse-wheel zoom and middle-button or temporary Space-key panning
- Whole-stroke erasing
- PNG, JPEG, BMP, GIF, and SVG import, clipboard bitmap paste, and Explorer drag-and-drop of images, text files, `.wimport` recipes, and PowerPoint decks
- PowerPoint decks imported as one picture per slide through PowerPoint from Microsoft 365: SVG, or PNG for a slide whose fonts are not installed, placed in one row per section, with an optional frame around each slide. **File → Open** creates a new board from the deck; **File → Import** and drag-and-drop add it below the board's content
- SVG is stored as its markup and redrawn as vectors at every zoom and resize. Pasting SVG markup that was copied as text — the output of a DAX SVG measure, for instance — creates a picture, and copying an SVG container puts both the markup and a bitmap on the clipboard. Text set in a Microsoft 365 font such as Aptos is drawn in that font when Office has it on the machine
- Image selection, movement, resizing, and deletion
- Text containers created by pasting plain text, with display and in-place edit modes
- Seventeen text-container types: fifteen languages with live syntax highlighting, plus Prompt and Markdown, and local F6 formatting for DAX, SQL Server, and KQL
- An **Insert** tab with eight shapes drawn by dragging, with outline color, fill, line thickness, and text of their own in nine fonts; three connectors that bind wherever an end is dropped on a shape or a container, follow it, and can be drawn from the four arrows on a selected shape; and text labels typed on the board. A shape or a label turns to any angle by its handle, and the pin at the end of the row makes it a palette you can move
- Selection by area: drag with Select for a rectangle, or hold Select for a freehand outline. Ctrl+A takes everything and Ctrl+Shift+A the ink alone, and the **…** on the selection offers Delete, Copy, Duplicate, and the four depth commands. Ink is taken too, and everything the area takes moves, resizes, recolors, reorders, and deletes as one
- An optional background grid, Lines or Dots, whose spacing is fixed in board pixels and coarsens as the zoom goes out. It is drawn on screen only and never appears in an export or a preview
- Containers automatically carry strokes that touch only that container when moved or resized
- LiveView containers for GPU-backed capture of an application window or display, with freeze/resume and saved last-frame previews
- Double-click a container to center it and fit it to the canvas
- Undo and redo for strokes, erasing, containers, text edits, and transformations
- Versioned ZIP-based `.wboard` documents with an embedded `preview.png`. Explorer shows that picture as the file thumbnail in the released install. The VS Code extension in `vscode/sqlbi-whiteboard` opens the same picture instead of the ZIP.
- Markdown `.wimport` recipes that build image and text containers from headings
- **File → Export** to PowerPoint or PDF: the board is cut into areas where it is empty, one slide or page per area, with an overview first. A deck carries the text containers in the speaker notes, or, as Editable slides, puts images and text containers on the slide as objects with the ink over them; a PDF has a bookmark per page, can put the whole board on one page to zoom into, and as Vector pages carries the ink as paths and the text as selectable text. The dialog shows the areas numbered on the board and updates as the settings change. **View → Frame** draws a slide on the board by hand: whatever sits inside a frame is that slide, and the rest of the board is cut automatically. [docs/export.md](docs/export.md) explains how areas are chosen
- An intentionally small floating toolbar
- A File / Edit / View / Insert / Help tab strip. Click a tab for a one-row command strip over the canvas
- Preferences for the mode — Teaching, Design, or Custom, which says whether the design tools are there at all — the startup monitor, full-screen start, full screen when idle, finger drawing, mouse drawing, the pen button, snippet format order, what an area selects and whether it extends to touching objects, the background grid, laser trail timing and weight, toolbar position and layout, Insert and Lasso on the toolbar, the Insert palette, what happens after inserting an object (Return to Select by default), keeping the Eraser on the toolbar for a pen that has no reverse end, and (except Store installs) a daily new-version check
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

**Preferences → Live View → Pause when Whiteboard loses focus** stops capture while another application is in the foreground, keeping the last frame visible. Returning to Whiteboard resumes only the LiveViews that were playing; manually paused or disconnected views stay that way. Whiteboard's own dialogs do not interrupt capture. The checkbox is off by default and takes effect immediately.

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
| Shift + pen tip | Hold Shift before starting a stroke to constrain it to horizontal or vertical at a uniform width. Releasing ends the constraint; a mid-stroke press is ignored |
| Pen hover | Show the small red pointer dot and hide the arrow |
| Pen contact | Hide both the pointer dot and arrow |
| Physical mouse movement | Show the normal arrow |
| Left mouse | With Mouse drawing off, temporarily select/move/resize a container and return to the previous drawing tool on release. With it on, the current tool: ink, erase, select, pan, or the laser |
| Ctrl + left mouse | Select/move/resize a container and return to the previous drawing tool — what the left button does on its own when Mouse drawing is off |
| Double-click container | Center and fit the image, text, or LiveView to the canvas. With Mouse drawing on and an ink or eraser tool selected, hold Ctrl: two plain clicks are two strokes |
| Double-click empty canvas | Center and fit all board content, or reset an empty board |
| Pen eraser | Erase complete strokes. The upper side button erases too, because Windows reports it the same way as a pen turned round. A pen with neither reaches the Eraser through **Help → Preferences → Toolbar → Always show the Eraser** |
| Pen barrel | Hold for Laser, or hold before starting a stroke for Straight line, as assigned in Preferences. Releasing ends the action; a mid-stroke press cannot start Straight line |
| One finger | Pan. With Finger drawing on, uses the current tool instead |
| Two fingers | Pan and pinch zoom. Cancels an in-progress finger stroke when Finger drawing is on |
| Mouse wheel | Zoom at the pointer. Shift+wheel zooms more slowly |
| Middle mouse | Pan |
| Right mouse | Pan while held on the canvas; return to Pen on release. Right-click the palette to hide or show it |
| Space | Temporarily switch to Pan |
| Ctrl+Z / Ctrl+Y | Undo / redo |
| Ctrl+C | Copy the selection. Copying a LiveView copies its last frame as a bitmap |
| Ctrl+V | Paste prefers an image (including a file on the clipboard) over text. Otherwise create a text container; Markdown and rich clipboard HTML can retain tables, lists, and headings |
| F2 | Edit the selected text container or label, put words inside the selected shape, or rename the selected frame. Typing a printable character with one shape selected starts its text too |
| View > Frame | Add a frame the size of the screen: a slide drawn on the board, selected by its edge or its tab, which Export takes as it is |
| View > Show frames | Hide the frames and their titles, or show them again. Hidden frames are still slides for Export, and the choice is saved with the board. Letter H while the View strip is open |
| Language chip | Choose a language, Prompt, or Markdown on a selected text container |
| F6 | Format DAX, SQL, or KQL on the selected text container; DAX wraps to the container's columns. In F2, formats in place. On a language that is only highlighted, opens that language's issue on GitHub |
| Drag right edge, or Shift + drag handle | Change a text container's width in columns and reflow it; the handle shows the count. A plain drag of the corner scales it |
| Ctrl+Enter | Commit the F2 edit, including an F6 format done in that session, and return to display mode |
| Escape | Cancel the active text edit, clear the selection, put down an Insert tool, close the command strip, or leave full screen or canvas only |
| Ctrl+S / Ctrl+O | Save / open a board |
| File > Import | Add a PowerPoint deck, an image, or a `.wimport` recipe to the board. Letter I while the File strip is open |
| Shift+F12 | Save As |
| Ctrl+E | Export the board to PowerPoint or PDF |
| Delete | Delete the selected container and its linked strokes |
| Ctrl+A / Ctrl+Shift+A | Select everything an area could take, or the ink strokes only |
| Ctrl+D | Duplicate the selection 24 pixels down and to the right, and select the copy |
| … on the property bar | Delete, Copy, Duplicate, and the four depth commands for whatever is selected |
| Alt+L | Laser pointer |
| File / Edit / View / Insert / Help | Tab strip. Click a tab for a one-row command strip over the canvas. Click the canvas to hide it |
| Insert tab | Alt+I: the eight shapes, the three connectors, and Text. The tool returns to Select after one object unless **After inserting an object** says to keep it |
| Insert > Palette | Pin the Insert row into a floating palette that drags anywhere in the window and comes back where it was left. Letter P while the Insert strip is open, or **Preferences → Toolbar → Insert palette** |
| Hold Select, or tap it again | Switch what a drag on empty canvas takes, between a rectangle and a freehand lasso. The hold is 600 ms; the button's glyph and its tooltip say which one is armed |
| Rotation handle | The circle above a selected shape or label turns it to any angle; Shift snaps to 15°, and the property bar's two buttons step by 45° |
| Connector arrows | Drag one of the four arrows on a selected shape to draw an arrow already bound to that side, and drop it on another shape to bind the far end |
| View > Grid | Show or hide the background grid, in the style chosen in Preferences. Letter G while the View strip is open |
| View > Design | Turn the design tools on, or put them away and go back to the mode you came from. Letter E while the View strip is open, since D, W, and K are taken. **Preferences → Mode** is the same setting |
| Ctrl + tap or drag | Add what is tapped, or what the area takes, to the selection instead of replacing it |
| Shift + drag a shape | Constrain the new shape to a square |
| Ctrl at release (connector) | Bind the endpoint to the nearest point anywhere on the object's border, rather than to one of its eight binding points. **Anchors → Auto** on the property bar moves a bound end to the side facing the other end instead |
| Help > Preferences | Searchable settings: mode, startup monitor, full screen at startup or when idle, no-pen warning, finger drawing, mouse drawing, pen button, snippet format order, area selection, background grid, laser trail, toolbar position and layout, Insert and Lasso on the toolbar, the Insert palette, after inserting an object, always show the Eraser, update checks. Each setting is one line; its chevron opens the reasoning behind it. Search marks what it matched, and marks the chevron when the match is in the text behind it |
| View > Bring to front / Bring forward / Send backward / Send to back | Reorder the selection (and its linked strokes) as one block. Letters B, W, K, and S while the View strip is open, and the same four behind the property bar's … |
| Help > About | Version, channel, license, the product site, and a download link when a newer release is known |
| View > LiveView | Capture, freeze, disconnect, or reconnect a window or display |
| F11 | Fill the current monitor and hide title and tabs. Escape leaves it when a text container is not being edited. **Preferences → Startup → Go full screen when idle** does the same after 20 seconds without input in Whiteboard, even while another app has focus |
| Ctrl+F11 | Hide title and tabs but keep this window’s size and place |

With the mouse, selection is automatic: click a container to move it, or drag the circular bottom-right handle to resize it while preserving its aspect ratio. Double-click a container to center it and fit it to the canvas. Releasing the mouse returns to the previously selected drawing tool.

**Help → Preferences → Mouse drawing** changes what the left button means, and it defaults to on when Windows reports neither a pen tablet nor a touchscreen. With it on the left button uses the selected tool, the tool stays selected rather than being handed back, and Eraser and Pan join the toolbar as they do for finger drawing. Everything above then moves to Ctrl: Ctrl and the left button select, move, and resize a container and return to the previous drawing tool, and Ctrl with a double-click centers and fits one. Two plain clicks with an ink tool are two strokes, which is why framing moves out of the way. Shift still constrains a stroke to horizontal or vertical, and Alt+L is still the laser. A mouse reports no pressure, so ink is drawn at an even width; Calligraphy still varies, because its width comes from speed. The pen path is untouched, so Mouse drawing can be left on beside a pen.

Imported images, LiveViews, text objects, shapes, and text labels act as containers. A completed stroke is linked when it touches exactly one container, including crossing its edge; a stroke touching multiple containers remains independent. A shape is picked up by its outline rather than by its interior, so ink drawn inside one stays reachable, and a label is picked up anywhere on its rotated rectangle. Moving, resizing, or turning a container transforms its linked strokes with it. **View → Bring to front**, **Bring forward**, **Send backward**, and **Send to back** reorder the selection and those linked strokes as one block. Deleting a container also deletes all of its linked strokes. Undo/redo treats each complete container operation as one action.

Paste plain text to create a selected text container in display mode. **Help → Preferences** has Snippet format order: paste tries those languages from top to bottom and uses the first that accepts the text. Plain text always accepts and comes last by default, so DAX, SQL, KQL, and Markdown are recognized on paste; a language claims a snippet only when it carries an operator, function, or keyword, so a bare word stays a note. Put Plain text first to keep every paste plain. A language added by an update joins in front of Plain text unless Plain text is first. Recognized extensions (`.dax`, `.sql`, `.kql`, `.txt`) keep their language; other dropped text files use the same order. Choose **Plain text**, **Markdown**, **DAX**, **SQL Server**, or **KQL** from the title-bar chip afterward, or one of **Python**, **C**, **C++**, **Java**, **C#**, **JavaScript**, **TypeScript**, **Visual Basic .NET**, **R**, **Rust**, and **PHP**, which are highlighted but not formatted and are never chosen by paste; **F6** on one of them opens that language's issue on GitHub. Press **F6** to format DAX, SQL, or KQL on the selected container without entering edit. Press **F2** to edit the body; the same list is in the title bar while editing. In F2, **F6** formats in place; **Ctrl+Enter** commits that edit (including the format) and returns to display. **Escape** restores the previous text, language, and dimensions. Text reflows while its edit-mode resize grip changes the width, and the height grows automatically when necessary. Double-click still centers and fits the container. Syntax highlighting applies in both edit and display modes. A language-aware title identifies a defined DAX, SQL, or KQL object when possible. SQL Server mode targets SQL Server 2025 T-SQL, preserves `GO` batch separators, and leaves invalid scripts unchanged. KQL mode reads Kusto through Microsoft's own parser and likewise leaves an invalid query unchanged. In display mode, resizing preserves the aspect ratio and scales the complete text visual without reflowing it.

Choose **Prompt** from the title-bar chip for AI instructions, or paste text tagged as Prompt by Prompt Assistant to select it automatically. Lines beginning with `- ` display as bullets; wrapped lines align with the item text, including space-indented lists. In F2 the original `- ` stays visible and editable, with the same hanging indentation. Copying and saving preserve the source text exactly, and F6 does not rewrite it. This is list layout, not a full Markdown renderer.

Prompt detection uses clipboard format `SQLBI.PromptAssistant.Metadata.v1` containing UTF-8 JSON `{"type":"Prompt","version":1}`, alongside the clipboard text. This explicit tag takes priority over snippet detection and accompanying images. Missing, invalid, or unsupported metadata keeps the usual paste behavior. Pasting into an existing editor inserts text without changing its language.

Choose **Markdown** from the title-bar chip for a formatted answer or a note: headings, emphasis, lists, tables, quotations, and code blocks keep their layout. Paste recognizes Markdown through Snippet format order, and converts structured clipboard HTML when it carries formatting the plain text has lost. **F2** edits the Markdown source and **Ctrl+Enter** renders it again; **F6** does nothing on it, the corner handle scales the rendered visual, and the right edge reflows it. Markdown exports as a picture to preserve its layout, with its source retained in PowerPoint notes; see [docs/markdown.md](docs/markdown.md) for clipboard behavior, supported structures, and limitations.

Image files, and `.txt`, `.dax`, `.sql`, and `.kql` files, can be dropped directly from File Explorer. Their initial center is the board position at which they were dropped. DAX, SQL, and KQL files open in the matching language mode. Other dropped text files use Snippet format order.

### `.wimport` recipes

A `.wimport` file is Markdown that builds containers on a board. It is import-only: there is no export, and saving always writes a `.wboard`.

- `#` is an optional title (not a container).
- `##` starts one container. The heading is the title.
- An image (`![](path)`), a `dax` / `sql` fence, a link to `.dax` / `.sql` / an image file, or leftover prose chooses the container kind.
- A thematic break (`---`) starts a new row. When the file uses these breaks, each row stays together regardless of width; only files without breaks wrap automatically.
- Paths are local and relative to the `.wimport` file. Missing files are skipped and listed in a dialog.

Drop a `.wimport` onto an open board to add its containers, with the pointer as the group’s top-left. **File → Open** or double-click (released installer) starts a new untitled board from the recipe.

The full contract for authors and agents is [docs/wimport.md](docs/wimport.md). A sample lives at `docs/samples/contoso-workshop.wimport`. In VS Code, associate the extension with Markdown to use the built-in preview:

```json
"files.associations": { "*.wimport": "markdown" }
```

Plain `.md` files are not recipes: dropping one adds a text container, in Markdown when the text carries formatting.

A `.wboard` in the same tree opens as the embedded preview if
[SQLBI Whiteboard for VS Code](https://marketplace.visualstudio.com/items?itemName=sqlbi.sqlbi-whiteboard)
is installed. The source is `vscode/sqlbi-whiteboard`.

## Architecture

- `SQLBI.Whiteboard.Core` contains world geometry, camera math, retained board objects, commands, hit testing, and archive persistence. It has no UI-framework dependency.
- `SQLBI.Whiteboard.Dax` contains the framework-neutral DAX lexer, parser, classifier, and deterministic formatter adapted from Prompt Assistant.
- `SQLBI.Whiteboard.SqlServer` contains the framework-neutral SQL Server 2025 adapter over Microsoft's ScriptDOM parser and script generator.
- `SQLBI.Whiteboard.Kql` contains the framework-neutral Kusto Query Language adapter over Microsoft's own Kusto parser, classifier, and formatter.
- `vscode/sqlbi-whiteboard` is a VS Code custom editor that shows `preview.png` from a `.wboard` ZIP. It is not part of the desktop installer.
- `SQLBI.Whiteboard` is the WPF shell. `TouchInkCanvas` supplies system-managed wet ink for the finger, while `BoardSurface` renders completed ink, images, text, and selection on a white canvas in camera space, plus the pen's own wet stroke. A transient AvalonEdit surface is overlaid only while a text container is being edited; language services translate parser classifications into WPF text styles. `Highlighting/` holds the syntax definitions for the languages read lexically rather than parsed, and the adapter that flattens them into the same styled spans.
- `SQLBI.Whiteboard/LiveView` owns Windows Graphics Capture and the Direct3D-to-WPF bridge. Capture retains one GPU frame per active LiveView; CPU bitmap conversion occurs only when copying or saving a snapshot.
- `SQLBI.Whiteboard.Core.SmokeTests` is a package-free executable test harness for camera anchoring, commands, hit testing, and archive round trips.
- `SQLBI.Whiteboard.SmokeTests` is the same kind of harness for what needs WPF: syntax highlighting, the language registry, and the runs an editable export carries. It is never published.

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
6. Turn on **Always show the Eraser**: it joins the row of tools, and moves to its own row under the palette when the layout is Dual palette. Pan does not appear either way, the tip erases while the Eraser is selected, and turning the setting back off returns to the last drawing tool.

Wacom driver settings can remap the barrel and eraser controls, so validate both Windows Ink mode and the intended application profile.

## Mouse drawing validation

Mouse drawing cannot be covered by the smoke tests, which are UI-free. Walk this after
touching any mouse handler:

1. On a machine with no pen and no touchscreen, confirm the default turns it on and the
   startup notice describes it.
2. Draw with Pen, Highlighter, and Calligraphy. Only Calligraphy should vary its width.
3. Click without moving, and confirm a dot is drawn rather than nothing.
4. Hold Shift before drawing and confirm the stroke is constrained. Release it to return to freehand, and confirm pressing it during a stroke does nothing.
5. Erase, and confirm the dashed square matches what is removed.
6. Ctrl-drag a container, and confirm the drawing tool comes back on release.
7. Two quick clicks with the Pen, and confirm the board does not reframe.
8. Select the Eraser, pan with the right button, and confirm the Eraser is still selected.
9. On a pen machine with Mouse drawing **On**, draw with the pen and confirm nothing about
   it changed. This is the regression that matters.

With Mouse drawing **Off**, the offer has its own list. Every one of these is about a key
doing nothing, which is the part a later change is most likely to undo:

1. Pick a tool from the toolbar with the mouse. The tool is selected, and the offer appears
   after it rather than instead of it.
2. Pick another tool. The offer does not appear again this session.
3. Press Enter with nothing focused. Nothing happens and the dialog stays open.
4. Tab to the checkbox and press Enter. Still nothing.
5. Tab to a button and press Enter. That button, and only that button, acts.
6. Press Escape. The dialog closes and Mouse drawing stays off.
7. Choose **Enable mouse mode**. The left button draws immediately, and Eraser and Pan
   appear on the toolbar.
8. Tick **Don't show me this again**, choose **Cancel**, restart, and confirm the offer is
   gone and **Help → Preferences → Input** can bring it back.
9. Reach for the toolbar with the pen on a pen machine, and confirm no offer appears.
