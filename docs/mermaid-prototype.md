# Mermaid prototype

This branch tests local diagram rendering inside existing Markdown containers. It does
not change the board schema, container commands, input routing, or language selection.
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
scripts, and event attributes. `SvgImageCodec` converts that normalized SVG to a frozen
WPF drawing off the UI thread. Ordinary SVG import is unchanged.
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
limits. Multiple-diagram results are checked for atomic layout publication.

Run the opt-in integration checks on Windows with WebView2 installed:

```powershell
dotnet build Whiteboard.sln -c Release
$env:SQLBI_WHITEBOARD_MERMAID_BROWSER = '1'
$env:SQLBI_WHITEBOARD_MERMAID_PREVIEW = "$env:TEMP\whiteboard-mermaid.png"
dotnet run --project tests/SQLBI.Whiteboard.SmokeTests -c Release --no-build
```

The integration checks render all three diagram families and styled multiline Unicode
labels; inspect the WPF glyphs and SVG normalization; check cache reuse and syntax-error
recovery; and dispose during rendering and startup. The optional preview path produces
a mixed Markdown PNG and normalized SVG files for the three diagram families.

Manual checks before expanding the feature:

1. Drop `docs/samples/mermaid-prototype.md` onto the board and compare the diagrams.
2. Edit a label with **F2**, commit with **Ctrl+Enter**, then undo and redo. Try malformed
   syntax and correct it. Try a larger diagram from an actual workshop.
3. Draw with the Cintiq while a new diagram is rendering, then after it completes.
   Check pen, highlighter, calligraphy, rear eraser, mouse, touch, pan, and zoom.
4. Annotate a diagram, move and scale its container, and undo/delete it. Reflow changes
   where content lies under annotations, as it does for other Markdown.
5. Save and reopen to regenerate diagrams. Wait for completion before testing exports.
6. Close Whiteboard while a diagram is rendering and while the renderer is starting.

## Deferred integration

Persisted SVG snapshots, export completion barriers, and runtime
installer/distribution validation follow user testing. Save/load currently persists only
the Markdown source; previews and exports can capture a pending placeholder. Additional
Mermaid families, custom themes, and wider syntax compatibility are outside this prototype.
