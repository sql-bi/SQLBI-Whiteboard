# Mermaid prototype

This branch tests local diagram rendering inside existing Markdown containers. It
adds optional snapshot metadata to text containers. It does not change container commands,
input routing, or language selection.
The supported syntax and user workflow are in [markdown.md](markdown.md#mermaid-prototype).

## Rendering

`MarkdownContent` finds Mermaid fences when parsing a source string. A committed document
change schedules their rendering; editor keystrokes and canvas drawing do not run Mermaid.
`MainWindow` owns a lazily created `MermaidRenderer` for its lifetime. The renderer serializes
requests through a hidden native WebView2 controller, uses a 32-entry source cache, and
closes with the window. Identical diagrams share a task and frozen result. WPF layouts
retain those results even after a renderer-cache entry is evicted.

The page receives source as JSON data. Only its three bundled resources can be served.
The controller blocks other resource requests, navigation, new windows, downloads, and
permissions; host objects are disabled. CSP blocks network connections and external
content. Mermaid runs in strict mode, with configuration directives rejected by the host.
Startup stages have 20-second timeouts and individual render requests have 12 seconds.
A timeout disables subsequent requests until the application restarts.

The JavaScript adapter resolves SVG styles and positions text runs using browser metrics.
It removes empty rectangles and flattens nested `tspan` elements because SharpVectors
does not reproduce those Mermaid constructs correctly. It also removes links, stylesheets,
scripts, and event attributes. `MermaidSvg` validates the normalized SVG and `SvgImageCodec` converts it to a frozen
WPF drawing off the UI thread. Ordinary SVG import is unchanged.
The adapter centers SVG-only mind-map labels on their nodes: Mermaid 12 can leave them
left-anchored in a centered shape, notably a circle. This adjustment uses the browser's
label bounds before text flattening; it does not modify the bundled engine.
The resulting drawing is bounded to Mermaid's declared SVG viewport, because invisible
geometry in the converted drawing can otherwise inflate its reported natural size.

Diagram scale is `min(1, availableWidth / naturalWidth)`, with 10 board units of padding
on each side. Diagrams are centered horizontally and use their scaled natural height
plus padding. Pending and failed diagrams occupy a 160-unit placeholder. A source's
results are published together, so multiple diagrams cause only one layout update.

`MermaidContainerLayout` derives the container height from the prepared source, current
width, and visual scale. It refreshes after rendering and after document changes, including
undo/redo, without adding a history entry or transforming linked ink. Pending sources and
the active source editor are left alone. Exiting the editor applies any measurement that
finished during the edit. A late result fits only current content; it cannot restore a
deleted container or overwrite a newer edit. Existing commands still own movement, corner
scaling, reflow, and edits. During a Mermaid source edit, only an explicit width change
scales linked ink; changing the text does not squeeze annotations into a pending placeholder.
Source edits and reflow can move content under annotations.

## Saving and output

`TextBoardObject.MermaidSnapshots` is an immutable array of exact diagram source/SVG pairs.
`scene.json` writes it as optional `mermaidSnapshots` metadata on the text object. Existing
archive version rules are unchanged; older readers ignore the field. The original Markdown
remains authoritative. Snapshots contain successful results only, at most 16 per container,
with 12,000 source characters and 2,000,000 SVG characters per diagram. Loading drops invalid
metadata and duplicate sources. Snapshots for removed or changed diagrams are omitted on save.

`MermaidDocument.RestoreAsync` decodes matching snapshots off the UI thread before the loaded
board is displayed. It does not create a browser. Each SVG passes a bounded XML reader with
DTD and processing instructions prohibited, an element allowlist, and checks rejecting active
attributes, styles, links, and nonlocal resource references. The normal SVG decoder also blocks
external resources. Corrupt snapshots are ignored and normal source rendering supplies the
fallback. Saved SVG is never passed to WebView2. Unknown or edited sources show an error and
their source if the renderer is unavailable; valid unchanged snapshots remain visible.

Save, autosave, exit recovery, and the export dialog use `MermaidDocument.CaptureAsync`.
It snapshots the model before yielding, awaits its diagram preparations, fits natural heights,
and copies successful output into that private snapshot. Preview rendering and export area
partitioning happen afterward. No completion callback can modify that captured source, nor
does preparing it add history entries or transform linked annotations. Save completion checks
the current model before marking it saved. Concurrent save requests share one operation;
session writes are serialized so a pending autosave cannot overwrite the final exit state.
Cancelling a capture wait does not cancel a rendering task shared with the canvas.

Failures settle as drawable error/source placeholders, so a missing runtime or invalid syntax
does not block saving or exporting indefinitely. Browser startup and render timeouts still
apply. Exported Markdown remains a picture, including editable PowerPoint and vector PDF,
and PowerPoint notes retain the Markdown and Mermaid source.

## Bundled dependency

- Mermaid version: **12.0.0**.
- Source: `https://registry.npmjs.org/mermaid/-/mermaid-12.0.0.tgz`.
- Bundled file: `package/dist/mermaid.min.js`, copied without modification to
  `src/SQLBI.Whiteboard/Mermaid/Web/mermaid.min.js`.
- SHA-256: `28FCA7AE6EBC7ED7BB63BDE63136A74BFEF14F296A57E403657EEB8B32836073`.
- The package's MIT license is kept beside it as `MERMAID-LICENSE.txt`.
- WebView2 SDK: **1.0.4258.31**, referenced by NuGet. The machine must have the
  WebView2 Runtime; runtime installation is not added by this prototype.

Build and publish copy the local web resources. There is no CDN, Node.js runtime, or
Mermaid CLI dependency on the user's machine. The browser profile is under
`%LOCALAPPDATA%/SQLBI/Whiteboard/MermaidPrototype`.

## Validation

The normal WPF smoke suite uses a fake diagram renderer and requires no browser runtime.
It covers fence detection, nested blocks, natural-size/shrink-only layout, cached reflow,
async height fitting, undo/redo, linked ink, ordinary code fences, errors, and document
limits. Multiple-diagram results are checked for atomic layout publication. Archive tests
cover optional metadata, bounds, and old boards. WPF tests cover pending-output waits,
save/reopen without a renderer, pixel-identical previews, stale edits, invalid snapshots,
safe SVG rejection, cancellation, and both picture and editable/vector export modes.

Run the opt-in integration checks on Windows with WebView2 installed:

```powershell
dotnet build Whiteboard.sln -c Release
$env:SQLBI_WHITEBOARD_MERMAID_BROWSER = '1'
$env:SQLBI_WHITEBOARD_MERMAID_PREVIEW = "$env:TEMP\whiteboard-mermaid.png"
dotnet run --project tests/SQLBI.Whiteboard.SmokeTests -c Release --no-build
```

The integration checks render all five diagram families and styled multiline Unicode
labels; inspect the WPF glyphs and SVG normalization; check cache reuse and syntax-error
recovery; reopen the diagrams from saved snapshots with a disposed renderer and compare
preview pixels; and dispose during rendering and startup. Mind-map coverage includes
nested branches, square/rounded/circle/bang/cloud/hexagon shapes, styled multiline labels,
and root-label containment. State coverage includes the legacy keyword, nested states,
notes, choices, forks/joins, concurrent regions, direction, and classes. Both new families
also pass through picture/editable PowerPoint and picture/vector PDF output. The optional
preview path produces a mixed Markdown PNG and normalized SVG files for the test diagrams.

Manual checks before expanding the feature:

1. Drop `docs/samples/mermaid-prototype.md` and `docs/samples/mermaid-mindmaps-states.md`
   onto the board and compare the diagrams.
2. Edit a label with **F2**, commit with **Ctrl+Enter**, then undo and redo. Try malformed
   syntax and correct it. Try a larger diagram from an actual workshop.
3. Draw with the Cintiq while a new diagram is rendering, then after it completes.
   Check pen, highlighter, calligraphy, rear eraser, mouse, touch, pan, and zoom.
4. Annotate a diagram, move and scale its container, and undo/delete it. Reflow changes
   where content lies under annotations, as it does for other Markdown.
5. Save immediately after committing a diagram, reopen, and export a deck/PDF. Repeat with
   the runtime unavailable: saved diagrams should remain visible, and editing a diagram
   should show a runtime explanation without hiding unchanged diagrams.
6. Close Whiteboard while a diagram is rendering and while the renderer is starting.

## Deferred integration

Runtime installer/distribution validation remains outstanding. Additional Mermaid families,
custom themes, and wider syntax compatibility are outside this prototype. Editing or reflowing
Markdown can move content under annotations; ink remains linked to its container rather than
to individual diagram nodes or text runs.
