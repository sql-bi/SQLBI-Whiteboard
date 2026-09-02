# What's new

What a released version gives you that the one before it did not, written for the person
deciding whether to upgrade rather than for the person who wrote the code.

This file is the source for both the [What's new page](https://whiteboard.sqlbi.com/changelog.html)
and the notes on each GitHub release, so it is written once and read in both places. Only
released versions appear. Pre-release **Dev** builds are published continuously from `main`
and are not listed here; their commit history is on GitHub. Nor are the releases before
1.0.0, which built the application up to its first stable version and are on
[GitHub Releases](https://github.com/sql-bi/SQLBI-Whiteboard/releases) with their own notes.

**Adding an entry.** One `## <version> - <date>` heading, then one `###` heading per thing a
person would notice, each with a sentence or two saying what changed and what it is for. A
release with nothing worth noticing still needs an entry — say that it is a fix and what
broke. The heading is parsed by `scripts/release-notes.ps1`, so keep its shape; the prose
under it is ordinary Markdown, and the renderer handles paragraphs, lists, links, `code`
and **bold**.

## 1.2.2 - 2 September 2026

### The Eraser, for a pen that has none
Not every pen has an eraser on its back end, and until now those pens could not reach the
Eraser tool at all — it appears on the toolbar only when finger or mouse drawing puts it
there. **Preferences → Toolbar → Always show the Eraser** keeps it there for the pen too.
It is off by default, so nothing changes for a pen that already erases. In the compact
toolbar layouts the Eraser joins the row of tools rather than taking a row of its own.

### Preferences you can read
Every setting is now one line: a title and a single sentence saying what it is for. The
defaults, the reasoning and the consequences moved behind a chevron, so a category can be
skimmed instead of read. Searching marks what it matched, and marks the chevron when the
match is in the text behind it, and the search box has a button to clear it.

## 1.2.1 - 31 August 2026

### Mouse drawing offers itself
With Mouse drawing off, picking a tool from the toolbar with the mouse now offers to turn
it on. Reaching for the palette with a mouse is the one moment the application can be sure
the question is worth asking — a pen user reaches for it with the pen. The offer appears
once a session, and once declined for good it does not come back.

## 1.2.0 - 30 August 2026

### The mouse can draw
The left mouse button now does what the selected tool does: ink, erase, select, pan, or the
laser. It defaults to on when Windows reports neither a pen tablet nor a touchscreen, so a
machine with nothing else to draw with works out of the box.

Everything the left button used to do moves to **Ctrl**: Ctrl and the left button select,
move and resize a container, and Ctrl with a double-click centers and fits one. A mouse
reports no pressure, so ink is drawn at an even width, and Calligraphy is the one tool that
still varies because its width comes from speed. The pen path is untouched, so this can be
left on beside a pen.

## 1.1.5 - 29 August 2026

### A word at startup when there is nothing to draw with
When Windows reports neither a pen tablet nor a touchscreen, the application now says so at
startup and explains what it is drawing with instead. Dismissable from the notice or from
Preferences, because the list Windows reports can miss a pen that has never been in range.

### Setting choices that do not clip their labels
The drawn choices in Preferences — toolbar position, pen button, laser weight — reflow onto
another row instead of squeezing until their labels are cut off mid-word.

## 1.1.2 - 27 August 2026

### Ink over an image, not under it
A stroke drawn across an imported image, text container or LiveView now appears over it
while you are drawing, as it already did once the stroke was finished.

## 1.1.1 - 26 August 2026

### Steadier pen ink, and straight lines
Pen strokes are now collected from the pen's own points rather than from the ink layer,
which keeps pressure and timing intact along the whole stroke. Holding **Shift** rules a
stroke straight, and **Straight line** is available as the pen barrel button action in
Preferences.

## 1.1.0 - 25 August 2026

### SVG images stay vector
SVG files can be imported like any other image, and stay vector — so they are still sharp
after zooming in or scaling the container up.

## 1.0.3 - 25 August 2026

### LiveView stability
Fixes a crash caused by releasing a LiveView frame surface more than once.

## 1.0.2 - 25 August 2026

### LiveView stability
Fixes a crash in Windows Graphics Capture by keeping it on the UI thread.

## 1.0.1 - 25 August 2026

### LiveView stability in the Store build
Fixes a crash when a LiveView was resized in the Microsoft Store build.

## 1.0.0 - 23 August 2026

### The pen says what a tap would do
While the pen hovers, the board shows what touching down would do: the laser with its halo
and speed trail, a dashed square around what the eraser would clear, and a high-contrast
dot for everything else. All of them disappear on contact.

### 1.0
Everything 1.0 was waiting on had shipped: Preferences, `.wimport` recipes, Explorer and
VS Code previews, the documentation site, and finger drawing.
