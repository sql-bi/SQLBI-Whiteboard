# Markdown containers

Markdown is a text type alongside Plain text and Prompt. It renders headings, bold and
italic text, strikethrough, inline code, nested bullet and numbered lists, quotations,
horizontal rules, links, fenced code, and pipe tables. Table cells wrap, row heights fit
their contents, and left/center/right column alignment is preserved. Recognized fenced
code languages reuse the application's syntax coloring.

## Using it

Paste a formatted answer or Markdown source onto the board. Markdown participates in
**Preferences → Snippet format order**, immediately before Plain text in a new setup.
An existing customized order keeps its priorities. You can also choose **Markdown** from
the container's language chip.

The normal view is noninteractive, so writing on a table or a link annotates the container
rather than activating a document control. **F2** opens the source editor; **Ctrl+Enter**
commits and measures the rendered content so the last row or line fits. **Escape** cancels
the edit. F6 does not rewrite Markdown.

The corner handle scales the complete visual. The right edge or Shift+handle reflows it.
Moving, scaling, deleting, undo, and redo use the existing container and linked-ink rules.
Ink is linked to the container, not to individual words or cells: editing/reflowing can
change which content lies underneath existing annotations.

## Clipboard handling

- The existing explicit Prompt Assistant metadata, file/image paste, and SVG precedence
  are unchanged.
- A `text/markdown` or `text/x-markdown` clipboard format explicitly selects Markdown.
  Copying a Markdown container supplies its exact source in both Unicode text and
  `text/markdown`, so even a container containing only a plain sentence keeps its type.
- Untagged text follows Snippet format order. Raw Markdown is retained verbatim when it
  preserves the formatting. If HTML carries more structure, it is converted to Markdown,
  including tables, list nesting, emphasis, and code fences.
- Native Windows CF_HTML byte offsets and fragment markers are understood, including
  Unicode before the selection. HTML can arrive as a string, UTF-8 bytes, or a stream.
- A plain-text-first preference keeps the original plain text. A recognized code snippet
  stays DAX, SQL, or KQL even if a rich clipboard also wraps it in HTML.
- Pasting inside an existing Markdown editor also accepts rich HTML, without changing the
  editor's type. Other editors keep their existing paste behavior.

This supports the formats used for copied chat responses without depending on an app name
or a particular chat's HTML classes. It cannot reconstruct formatting that was absent from
every clipboard format.

## Rendering and persistence

Markdig parses the source and a native WPF renderer creates a frozen drawing. Parsed
documents are weakly cached by source; a document keeps at most four layouts by unscaled
width. Camera movement and pen frames replay the drawing without reparsing or reflowing.
HtmlAgilityPack is used only to read pasted HTML; neither parser executes scripts or
fetches resources. External images are not loaded.

The board still saves the original source and language ID through its existing text
container schema. No new archive version or first-party assembly is required. Older
applications that do not recognize Markdown show its source as plain text.

The screen, board preview, and image exports use the same renderer. Editable PowerPoint
and vector PDF currently embed each Markdown container as a rendered picture, not editable
table/text objects. PowerPoint notes retain the source. The image fallback is capped at
4096 pixels on its longest edge.

## Initial scope

- Links are styled, not clickable.
- Images show an alt-text placeholder; neither local files nor remote URLs are fetched.
- Raw HTML is displayed as literal text, except simple line breaks in table cells.
- HTML clipboard conversion preserves semantic markup, not arbitrary CSS, fonts, or colors.
  Merged/nested HTML tables are not reproduced as merged/nested tables.
- Mathematical notation, task-checkbox widgets, and other Markdown
  extensions beyond the supported subset are not rendered specially.

## Mermaid prototype

Fenced `mermaid` blocks render the 32 diagram families built into Mermaid 12.0.0.
Paste their Markdown source, or drop
[the sample file](samples/mermaid-prototype.md) onto the board. Choose **Markdown**
if your Snippet format order selects another language. Edit with **F2** and commit
with **Ctrl+Enter**. A copied diagram picture alone does not carry editable Mermaid source.

Mind maps support indented branches, node shapes, and multiline styled labels. State
diagrams support transitions, start/end markers, nested states, notes, choices,
forks/joins, and concurrent regions. Try [the mind-map and state samples](samples/mermaid-mindmaps-states.md).

The full catalogue is listed below. Aliases, layouts, and notations within a family
are not counted as separate families; `info` is a library diagnostic, not a diagram family.

| Group | Families | Samples |
| --- | --- | --- |
| Original diagrams | Flowchart, sequence, entity relationship, mind map, state | [Basic diagrams](samples/mermaid-prototype.md), [mind maps and states](samples/mermaid-mindmaps-states.md) |
| Charts | Pie, XY, quadrant, radar, Sankey, treemap, Venn, Ishikawa | [Chart samples](samples/mermaid-catalogue-charts.md) |
| Processes | Timeline, Gantt, user journey, Kanban, swimlanes, AgentFlow, event modeling, Cynefin, Wardley | [Process samples](samples/mermaid-catalogue-processes.md) |
| Structures | Class, block, architecture, C4, use case, requirement, Git graph, packet, tree view, railroad | [Structure samples](samples/mermaid-catalogue-structures.md) |

The samples show the exact keywords, including `-beta` where Mermaid requires it.
C4 includes context, container, component, dynamic, and deployment diagrams. Railroad
includes its native notation, EBNF, ABNF, and PEG. Mermaid owns syntax detection; Whiteboard
does not keep a second list of allowed diagram types. Third-party plugins are not bundled.

Rendering uses the bundled Mermaid engine through one hidden WebView2 controller, created
on the first new or changed diagram. Generating diagrams requires the Microsoft Edge WebView2 Runtime. No diagram source
is sent to a server. Generated SVG becomes a frozen WPF drawing, shared by unchanged
diagram sources and replayed during drawing, pan, and zoom. No browser control is placed
on the canvas. See [mermaid-prototype.md](mermaid-prototype.md) for implementation details.

Diagrams use their natural size at 100%, shrinking proportionally only when they exceed
the available width. Smaller diagrams are centered, and height fits the rendered content.
Canvas zoom and corner resizing still scale the whole container; reflow recalculates
the width limit. After rendering, the container's height updates without an extra undo
step or stretching existing ink. Wait for the diagrams to appear before annotating, since
the content below a pending diagram moves when its height is known. Syntax errors,
unsupported diagram types, or an unavailable runtime show an explanation and source.

The board saves the original Markdown source and successful SVG diagram snapshots.
Reopening uses those snapshots without starting WebView2. They retain the saved diagram
layout, including when the runtime is unavailable. Editing a diagram regenerates only
the sources that have changed; unchanged diagrams remain visible if regeneration fails.
Missing or invalid snapshots are regenerated from the source when the runtime is available.

Save, session recovery copies, export previews, PowerPoint, and PDF wait for pending
diagrams and use their final container heights. Failed diagrams retain an explanation
and their source in the output. A save captures the board at the time of the request;
edits made during the wait remain unsaved. Older releases ignore the optional snapshot
metadata and can drop it when saving. Runtime installation is not added by this prototype.

The prototype accepts up to 16 distinct diagrams per Markdown container, 12,000 characters
per diagram, and 200 flowchart edges. Embedded configuration, interactive links,
remote images, external fonts, and icon-font packs are not enabled. Built-in architecture
icons work locally. HTML labels generated internally by Mermaid are converted to passive
SVG text, preserving line wrapping and basic font styles; no HTML is stored or executed
when reopening a board. Experimental families retain Mermaid's beta status. The catalogue
has representative coverage, not a guarantee for every combination of syntax or styling.

## Validation

Core smoke tests cover detection-order migration, source/archive fidelity, linked ink,
scaling, deletion, and undo/redo. WPF smoke tests cover parsing, wrapping, cached drawings,
syntax colors, Unicode clipboard fragments, HTML safety, and PowerPoint/PDF output.
Set `SQLBI_WHITEBOARD_MARKDOWN_PREVIEW` to a PNG path when running the WPF tests to render
the representative table/list/code sample for inspection.

Manual checks on the target device:

1. Copy a whole Codex answer containing a table, then copy a selected part of that answer;
   paste each onto the board and compare its structure.
2. Use F2, change a table cell, paste formatted content into the source, and commit with
   Ctrl+Enter; repeat with Escape to check cancellation.
3. Write over a table, pan/zoom, scale and reflow the container, then move/delete/undo it.
4. Save and reopen, and export a deck/PDF. Check the pen, rear eraser, mouse, and touch
   behavior alongside a normal image, a Prompt, and a code container.
