# Export: PowerPoint, PDF, and Replay

**Proposed. Nothing below is implemented.** This document is the plan for three ways of
getting a board out of the application: a PowerPoint deck, a PDF, and a replay of the
session. The first two share almost everything and are planned to be built; the third is
sketched at the level of modules and file format so that it can be judged and scheduled,
not started. When any part is accepted it becomes a numbered entry in
[decisions.md](decisions.md), and this document is kept for the alternatives.

Background: [README.md](../README.md) for what the application does, and
[decisions.md](decisions.md) for the constraints a new dependency has to meet — decision 21
(managed-only, permissive license, no native binary to sign) and decision 14 (nothing leaves
the machine).

## What all three build on

The board is one unbounded plane. A `.wboard` is a flat list of objects in world
coordinates — ink strokes, images, text containers, and LiveViews — each with a bounding
rectangle and a z-index that is also its creation order, plus the assets the images refer
to. A stroke that touches exactly one container is linked to it and moves with it. Nothing
in the file says where a page begins or ends, which is the whole problem for the first two
exports and no problem at all for the third.

Three pieces are shared, and are the first things to build:

- **A partitioner** in `SQLBI.Whiteboard.Core` that turns a document into an ordered list
  of rectangular *areas*. It has no UI dependency, so the smoke tests can cover it with
  synthetic boards. Both PowerPoint and PDF consume its output; PowerPoint asks for areas
  that fit a slide, PDF for areas that fit a page.
- **A rasterizer** in the WPF project that draws any world rectangle at any pixel size.
  `BoardPreviewRenderer` already does this for the whole board at 1024 pixels, by pointing
  a `Camera2D` at the content and rendering a fresh `BoardSurface` into a
  `RenderTargetBitmap`. It generalizes to a rectangle and a size, and `preview.png` becomes
  one caller of it. A fresh surface has no selection, no hover, and no pending stroke, so
  nothing transient leaks into an export; LiveViews are drawn from the live frame when one
  is connected and from the saved snapshot otherwise, exactly as saving does.
- **An export dialog** reached from **File → Export…** (`SessionCommand.Export`, Ctrl+E,
  which is free). It shows the board with the proposed areas outlined and numbered, and
  the few settings below change that preview live. The format is chosen in the same
  dialog, so the person sees the pagination before choosing between a deck and a PDF.

Rendering is on the UI thread, because `RenderTargetBitmap` requires it; encoding and
writing the file are not. A page at 3840×2160 is a 33 MB transient bitmap, so pages are
rendered one at a time and the dialog shows progress.

## Dividing the board into areas

This is the part that decides whether the export is useful. The proposals are listed from
the one recommended to the ones rejected.

### The units that must not be split

Before any cut is chosen, the objects are grouped into units that stay together:

- A container and every stroke linked to it are one unit, with the union of their bounds.
  The link already exists in the model (`InkStrokeObject.ContainerId`), and it is precisely
  the "this ink belongs to that picture" relationship the export wants.
- Every other stroke is a unit of its own. A stroke that touches two containers is not
  linked to either, and that is fine: because cuts are only placed where there is empty
  space, a stroke that spans two containers guarantees they land on the same page.

### Recursive whitespace cuts (recommended)

Project every unit's bounds onto the X axis and merge the overlapping intervals. What is
left between them is empty vertical bands. Do the same on Y for horizontal bands. Choose the
widest band; if it is at least the *gap threshold*, cut the region there and recurse into
each side. Stop when a region *fits* the target page, or when no band reaches the
threshold.

This is the recursive XY-cut from document layout analysis, and it suits a whiteboard
better than it suits a scanned page: people leave space between the things they draw, and
the space between two ideas is wider than the space inside one. It has three properties the
alternatives lack:

- It cannot cut through anything, by construction.
- It produces reading order for free: walk the cut tree, top side before bottom for a
  horizontal cut, left before right for a vertical one.
- It is deterministic and cheap, so the preview updates as the threshold slider moves.

Two settings drive it, and both are on the dialog:

- **Gap threshold**, the empty space that counts as a separation. Default 80 world units,
  which is about two lines of handwriting at zoom 1, on a slider from 20 to 400. An
  adaptive default — a multiple of the median stroke height on the board — was considered
  and rejected for the first version, because a fixed number with a live preview is easier
  to reason about than a number that depends on what was drawn.
- **Smallest text**, which defines *fits*. A text container's body is 18 world units at
  its default scale, and a 16:9 slide is 960 points wide, so a region of width W renders
  body text at 18 × 960 / W points. Asking for 12-point text means a region is at most 1440
  world units wide (810 tall); 9 points allows 1920 × 1080, which is a full-HD screen at
  zoom 1. The choices offered are 9, 12, and 14 points, default 12. The same limit is used
  for regions that contain no text, since handwriting is larger than body text.

When a region does not fit and has no band wide enough to cut, it is scaled down anyway
and the preview marks it ("Area 3 is at 60%"). Tiling it across several slides was
rejected: a dense drawing cut through the middle is worse on a slide than a small one, and
PowerPoint can zoom.

### Order

Two orders are offered:

- **Drawing order** (default): areas sorted by the smallest z-index they contain. Since
  z-index is creation order, this is the order in which the author started each area — the
  order of the lecture. Bring to front and Send to back disturb it only for the container
  they touched, which is rare and visible in the preview.
- **Reading order**: the cut-tree order, top-left to bottom-right.

### Alternatives considered

- **Bottom-up clustering.** Start from the units and merge any two closer than the
  threshold. It produces nearly the same areas as the cuts but has no natural stopping
  rule for "fits a slide", so oversize clusters need a second pass to split, and that
  second pass is the XY-cut. Rejected as the more complex route to the same place.
- **A fixed grid.** Tile the content bounds at a fixed scale and drop empty tiles. Simple,
  and it splits objects. Acceptable for a poster print, not for a slide. Rejected for the
  first version; it is a page-size option for PDF if anyone asks.
- **Manual frames.** Let the author draw rectangles on the board that *are* the slides,
  the way Miro and tldraw do it. This is the right long-term answer for someone who prepares
  a board for a deck, and it is a later phase rather than the first, for two reasons: it
  needs a new object kind and a bump of the archive version to 6 (a board with frames would
  not open in an older release, which is the established policy but still a cost), and
  frames have to be drawn during a session that the automatic version handles unattended.
  When frames exist, they win: any object inside a frame belongs to it, the rest of the
  board is partitioned automatically, and frames come first in the order.

## PowerPoint

### What goes on a slide

Each area is one slide. In the first version a slide holds:

- **A picture** of the area, rendered by the rasterizer at twice the slide's pixel size
  (3840×2160 for 16:9) and placed with the same 4% padding the board uses when it frames
  content. Calligraphy, the highlighter's blend, and SVG all come out exactly as on screen,
  because the same code drew them.
- **A title**, taken from the area's dominant container: the text container's title (a
  DAX measure or table name when the language service recognizes one) or the image's
  original file name. An area without a container is "Area n". The title is on the slide
  in PowerPoint's title placeholder, so the outline pane and the slide sorter are usable.
- **Speaker notes** holding the full text of every text container in the area, in
  order. Ink cannot be copied from a picture, but DAX and SQL can be copied from the
  notes, and the deck becomes searchable.
- **An overview slide** first, optional and on by default: the whole board, with each
  area outlined and numbered in slide order. It is the map that makes a fifteen-slide
  deck from one board navigable.

Selection, hover, the laser, and the pending stroke are never drawn. LiveViews show
their current frame. Slides are 16:9 by default with 4:3 as an option; the background is
the board's white.

### An editable deck (later phase)

A second mode, **Editable**, keeps images and text as PowerPoint objects:

- Bitmaps go in as they are; an SVG is rasterized through `BoardImageCodec.Rasterize`,
  which the clipboard already uses.
- A text container becomes a text box in the language's monospace font, with syntax
  colors carried over as runs. The classification spans that color the screen are
  available for exactly this; nothing new is parsed.
- All ink on the slide is one transparent PNG overlay on top. This keeps pressure,
  calligraphy, and the highlighter look with no second renderer. It also puts every
  stroke above every container, which is wrong only for a stroke that was drawn before an
  image was dropped on it — rare, and the picture mode is there for it.

Ink as freeform shapes — an outline polygon per stroke, filled — would make the ink
itself editable and vector. It is a third phase, only if someone asks: variable width
becomes a polygon per stroke, the highlighter loses its blend for a flat 50% alpha, and
the result is a second stroke renderer to keep in step with the first.

### Writing the file

`DocumentFormat.OpenXml` (Microsoft's Open XML SDK, MIT, managed-only) meets decision 21.
It is verbose but it is the reference implementation, and the deck built here uses a
dozen element types. A `.pptx` is a ZIP with a handful of XML parts, and the picture-only
deck could be written by hand from an embedded template with `System.IO.Compression`,
which `BoardArchive` already uses; that is the fallback if the dependency is unwanted, and
it stops being attractive as soon as the editable mode exists.

## PDF

### What is a page

PDF has a freedom PowerPoint lacks: every page may have its own size. That gives three
sensible page models, and the first two are the ones to ship:

- **One page per area.** The same partition as the deck, one area per page, scaled to
  fit a fixed page (A4 or Letter, landscape by default, since boards are landscape). The
  same threshold and smallest-text settings apply and produce the same numbering, so a deck
  and a PDF of the same board agree with each other.
- **Whole board on one page.** One page whose aspect is the content's, at the largest
  size PDF viewers accept (200 inches on a side), scaled down if the board is bigger. A
  reader zooms and pans the way the application does. This is the format for a board that
  is one drawing, and it needs no partition at all.
- **Same scale on every page** (later, if asked): each area gets a page sized to it at a
  constant world-to-point scale, so everything is the size it was on the board and a
  small note does not become a full-page note. Pages of different sizes read well on
  screen and print badly, which is why it is not first.

Each page carries, optionally, a footer with the board's file name, the date, and "page
n of m", and the document gets an outline (bookmarks) with one entry per area named like
the slide titles. The overview page with numbered outlines comes first, as in the deck.

### Raster first, vector later

The first version puts one picture per page, rendered by the same rasterizer at 200 dpi
(2339×1654 pixels for A4 landscape). That is the same code path as the deck, it is exact,
and for handwriting it is enough. A second version draws pages as vector content: strokes
as paths whose width follows pressure, text as text with the fonts embedded, bitmaps as
bitmaps, SVG rasterized. It makes the text selectable and the file smaller and it is the
second stroke renderer the editable deck also wants, so the two later phases share it.
Font embedding needs a check of the embedding permissions of the fonts in use before it
ships.

### Writing the file

`PdfSharp` 6 (MIT, managed-only, .NET 6+) fits decision 21 and does both versions: the
raster one is `DrawImage` on each page, the vector one is the same `XGraphics` API with
paths and text. A raster-only PDF could be written by hand in a few hundred lines, and that
was considered as a way to avoid the dependency; it was rejected because the vector phase
would then have to introduce the library anyway.

**Print** was considered as the way to get a PDF with no dependency at all: build a WPF
`FixedDocument` from the same partition and print it, and Microsoft Print to PDF turns it
into a vector PDF that matches the screen exactly, because WPF drew it. It is a genuinely
good result and it would give paper printing as well. It was not chosen as the export
route because the file name is asked for by the print driver rather than by the
application, there are no bookmarks, and the printer is an optional Windows feature. It
remains a cheap **File → Print** to add afterwards, sharing the partition and the page
options.

## Replay

Replay records a session — every stroke as it was drawn, every erase, every pan and zoom,
the laser, and the voice — and plays it back with a timeline. It is not scheduled. The
purpose of this section is to say what it would take, so the work can be sized and so the
smaller prerequisites can be picked up on their own when they are cheap.

### What is recorded

Everything that changed what the audience saw, as one event stream with a time in
milliseconds from the start of the recording:

| Event | Source | Notes |
| --- | --- | --- |
| Command executed, undone, redone | `CommandHistory` | Add, remove, and replace objects, with the objects serialized as `BoardArchive` already serializes them. Erasing and undo are just events; the player watches the stroke vanish as the audience did. |
| Stroke in progress | the ink points themselves | Pen ink already carries a real `Stopwatch` timestamp per point. A stroke's add event holds the whole stroke; the player draws it point by point between the first and last timestamp and commits it at the end. No separate stream is needed. |
| Camera | `Camera2D` | Center, zoom, and viewport size, coalesced to at most 30 per second. |
| Laser | `LaserTrailSurface.AddSample` | Position in world coordinates, pressure, and begin/move/lift, so the trail can be redrawn under either the recorded camera or a free one. |
| Marker | a toolbar button or a shortcut | A named point on the timeline, for chapters. |
| Pause and resume | the recorder | So a coffee break is one gap and not five minutes of nothing. |

Later, if the first version proves the format: text edits as throttled drafts (today a
text edit is one replace command at commit, so the text appears at once rather than being
typed), pen hover at a low rate so the pointer is visible even when it is not drawing, and
tool changes.

LiveView content is **not** recorded. Capturing a window at even one frame per second is
a screen recording with the privacy and size questions that come with one, and the
application already has a rule against content leaving the machine unasked. The replay
shows the LiveView's snapshot as it was when it was added, frozen, or reconnected, which
are the moments the recorder takes one.

### Where it is stored

Inside the `.wboard`, under `replay/`: the scene as it was when recording started, the
event stream as JSON Lines (one event per line, appended and flushed as it happens, so a
crash loses seconds rather than the session), and the audio track. Reading `scene.json`
does not change and the archive version does not move; a release without Replay ignores the
extra entries. A **Remove recording** command drops them.

A sidecar `.wreplay` file was considered and rejected: one file is what gets emailed, and
the VS Code preview and the Explorer thumbnail read the same ZIP either way.

Size is dominated by audio. An hour of lecturing is roughly two thousand strokes, about
4 MB of events compressed; ten minutes of laser at 120 samples a second is 2 MB; an hour of
mono AAC at 64 kbit/s is 28 MB. A one-hour session is a 35 MB board.

The format is plain JSON by design. A player in a browser, on the site, is then possible
without the application, and the format would get a contract document like
[wimport.md](wimport.md) before anything but the application reads it.

### Audio

Windows' own `AudioGraph` (WinRT, which the LiveView code already uses) captures the
default or a chosen microphone and encodes AAC in-box, with no dependency and nothing to
sign. The audio clock is the master clock when a track exists: the timeline position is
the playback position, and events are applied up to it. Drift over an hour is well under
a frame. The Store package needs the `microphone` capability in its manifest, Preferences
gets a microphone choice, and the privacy page says that the recording is written to the
board and nowhere else.

### The player

A timeline bar under the canvas: play and pause, a scrubber, markers, speed (1×, 1.5×,
2×), and two camera modes — **as recorded**, which replays the presenter's pans and zooms,
and **free**, which leaves the camera to the viewer, useful for reading rather than
watching. The laser and the in-progress stroke work in both because they are recorded in
world coordinates.

Seeking rebuilds the document from the start scene by applying events up to the target
time. A few thousand commands apply in milliseconds, so the first version needs no
keyframes; if sessions grow long, a snapshot every minute makes seeking constant-time and
is an internal change.

The in-progress stroke is drawn through `BoardSurface.PendingStroke`, which exists for the
pen's wet ink; the laser through the same `LaserTrailSurface` the live session uses.
Nothing is drawn by new code.

### Modules

- `Core/Replay`: the event records, the JSON Lines serializer, and a `ReplayTimeline`
  that applies events to a `BoardDocument` up to a time. Smoke-testable end to end:
  record synthetic events, play to several times, assert the document.
- `SQLBI.Whiteboard/Replay`: `SessionRecorder` (subscribes to the sources above and
  appends), `AudioRecorder` (WinRT), `ReplayPlayer` (owns the clock and drives the
  surface), and the timeline bar.
- `BoardArchive`: writing and reading the `replay/` entries, with the start scene using the
  same DTOs as `scene.json`.

### Prerequisites worth doing early

Each of these is small, improves the code on its own, and is on the critical path of
Replay:

1. **Real timestamps on every ink path.** The pen and mouse paths share `AppendInkPoint`
   and store a `Stopwatch` timestamp per point. The finger path commits through the
   InkCanvas and stores a sequence number instead (`firstTimestamp + index`), although
   `TouchInkCanvas` already sees the packet timestamps.
2. **`CommandHistory` raises an event carrying the command** and whether it was executed,
   undone, or redone. Today it says only that something changed.
3. **`Camera2D` announces changes**, or the nine places in `MainWindow` that move it go
   through one method that does.
4. **`BoardArchive`'s DTOs become a shared scene serializer**, so a snapshot and a command's
   objects are written by the code that writes `scene.json`.

### Phases

- **R0** — the prerequisites above.
- **R1** — silent recording and playback, embedded in the board. Proves the format and
  the synchronization of strokes, camera, and laser.
- **R2** — audio, the master clock, microphone preference, Store capability, privacy page.
- **R3** — markers, text drafts, hover presence, keyframes, LiveView snapshots.
- **R4** — if wanted: an MP4 render (frames from the rasterizer, muxed with the audio
  through Media Foundation), and a browser player on the site.

Sharing a replay today would mean sharing the board and having the application; R4 is
what removes that condition.

## Where the code goes

| Piece | Project | Notes |
| --- | --- | --- |
| Partitioner, area order, layout options | `SQLBI.Whiteboard.Core/Export` | No UI; smoke-tested with synthetic boards: two clusters separated by more than the threshold give two areas, a bridging stroke gives one, a container is never cut, both orders. |
| PowerPoint and PDF writers | new `SQLBI.Whiteboard.Export` | .NET 10, no WPF; references Core and the two packages; takes pages as title, notes, and PNG bytes. **Add it to the signing step in the pipeline** — see CONTRIBUTING. Tests open the produced ZIP and count parts, and read the PDF's page count back. |
| Rasterizer, export dialog, preview | `SQLBI.Whiteboard/Export` | `BoardPreviewRenderer` becomes a caller of the rasterizer. |
| Replay | `Core/Replay`, `SQLBI.Whiteboard/Replay` | As above. |

Export settings (format, page model, order, threshold, smallest text, overview page,
notes) are remembered in `AppSettings` and do not appear in Preferences: they belong to the
dialog that shows their effect.

Documentation that moves with each phase: the feature list in README, the guide and
shortcuts pages on the site, CHANGELOG, and a numbered decision for each accepted part.

## Phases and size

Sizes are relative to one another, for one person who knows the codebase.

| Phase | Delivers | Size |
| --- | --- | --- |
| E1 | Partitioner, rasterizer, export dialog with live preview, PowerPoint picture deck with titles, notes, and overview | Medium — the partitioner and the dialog are most of it |
| E2 | PDF, one page per area and whole board, raster, bookmarks, footer | Small on top of E1 |
| E3 | Editable deck: native images and colored text, ink overlay | Medium |
| E4 | Vector PDF | Medium; shares the stroke-to-path code with any later freeform ink |
| E5 | Manual frames on the board, archive version 6 | Medium |
| R0–R1 | Silent replay | Large |
| R2 | Audio | Medium |
| R3–R4 | Polish, video, browser player | Large, and optional |

E1 and E2 together are the deliverable; E3 to E5 are each independent of the others.

## Decisions to take before starting

1. Accept `DocumentFormat.OpenXml` and `PdfSharp` as dependencies, or hand-write the
   picture-only formats and accept that the editable and vector phases reopen the question.
2. Drawing order or reading order as the default.
3. A raster PDF as the first PDF, with vector as a later phase.
4. Whether manual frames are wanted at all, since they are the only part that changes the
   file format.
5. For Replay: embedded in the `.wboard` rather than a sidecar; in-box AAC audio only;
   LiveView content not recorded.
