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

## 1.5.0 - 11 September 2026

### The board you had open comes back
Close SQLBI Whiteboard and the board you were working on is there again the next time you
start, at the zoom and position you left it. **Reopen the last board** in
**Preferences → Startup** turns this off.

### Closing a board with unsaved changes asks first
If the board has a name and has changed since you saved it, closing offers **Save**,
**Keep for next time**, **Discard changes**, or **Cancel**. Keep leaves the file untouched and
brings the board back at the next start; Cancel is the one answer that keeps a LiveView feed
connected. The title bar now names the board, with an asterisk while it differs from the file.

### Work survives a crash
The board is copied beside your settings every thirty seconds. If SQLBI Whiteboard stops
unexpectedly, the next start offers it back, and so does opening that board again. Nothing
here writes to your `.wboard` without being asked.

## 1.4.0 - 8 September 2026

### Eleven more languages in a text container
The title-bar chip now also offers Python, C, C++, Java, C#, JavaScript, TypeScript,
Visual Basic .NET, R, Rust, and PHP. Choose one and the text is highlighted, on the board
and in an export. These languages are not formatted and are not recognized on paste: pick
them by hand.

### F6 asks for a vote on a language it cannot format
On one of the new languages, **F6** opens a short note with a link to that language's issue
on GitHub. A thumbs-up there is how the next formatter is chosen. DAX, SQL, and KQL still
format, and Plain text is still quiet.

## 1.3.1 - 5 September 2026

### KQL joins DAX and SQL in text containers
A text container can now be KQL: paste a query or drop a `.kql` file and choose **KQL**
from the title-bar chip. It is highlighted, **F6** formats it, and a query that does not
parse is left unchanged. Both use Microsoft's Kusto parser and run locally.

### Pasted code is recognized without a setting
Plain text now comes last in the snippet format order, so pasted DAX, SQL, or KQL arrives
as code. A bare word or a number still pastes as a note. An order you chose in
Preferences is kept; a language added by an update joins it in front of Plain text.

### A snippet's width is its line width
Drag a text container's right edge, or hold **Shift** on its corner handle, to change its
width in columns; the handle shows the count, and a plain drag of the corner still scales.
**F6** wraps DAX to that width. A new container is 65 columns wide, where the DAX
formatter wraps.

## 1.3.0 - 3 September 2026

### Export a board to PowerPoint
**File → Export** (Ctrl+E) turns the board into a deck. The board is cut into areas where
it is empty, one slide per area, with an overview slide first. Text containers go in the
speaker notes. The dialog shows the areas on the board and updates as you change the
settings.

### Export a board to PDF
Choose **PDF** in the same dialog: one A4 or Letter page per area, with a bookmark and a
footer, or the whole board on one page to zoom into.

### A deck you can rework
**Slide content → Editable**, the default, puts images and text containers on the slide
as objects, keeps DAX and SQL colors, and lays the ink over them as one picture.
**Picture** is one exact picture of the area.

### PDF pages that stay sharp
**Page content → Vector** draws the ink as paths and the text as text, so the page stays
sharp at any zoom and code can be selected and copied.

### Slides drawn on the board
**View → Frame** adds a frame the size of the screen. Whatever is inside a frame is that
slide; the rest of the board is still cut automatically. Select a frame by its edge or its
title tab, and press F2 to rename it. A board with a frame needs this release to open.

### SVG pictures land where the author put them
An SVG with an embedded, clipped bitmap drew it shifted and partly missing, and an
embedded picture saved at a DPI other than 96 came out the wrong size. Both now draw where,
and as large as, the SVG says.

### A centred, letter-spaced label no longer collapses
A label with `letter-spacing` and `text-anchor="middle"` or `end` piled its letters up.
It now appears where it was placed, set slightly tighter.

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
