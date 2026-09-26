# PowerPoint import

**Implemented for 1.6.3.** This document is the plan for bringing a PowerPoint deck onto
a board, one picture per slide. The decisions below were taken on 26 September 2026 after
a spike, a throwaway console tool that wrote a PNG board and an SVG board from a deck; its
findings are recorded here so that the reasons survive the code. *As built* at the end
lists where the implementation differs from the plan.

Background: [export.md](export.md) for frames and how Export cuts a board into slides, and
[decisions.md](decisions.md) for decision 14 (nothing leaves the machine) and decision 21
(managed-only dependencies). The import meets both: it drives the PowerPoint already on
the machine, adds no package, and sends nothing anywhere.

## What it does

A deck becomes one picture per slide, laid out on the board, each optionally inside a
frame. The picture is SVG where that looks right and PNG where it does not. Slides are not
turned into editable shapes and text; that was considered and left out, because it is
weeks of work and lossy on every deck that uses gradients, groups, or rich text.

It needs PowerPoint from Microsoft 365. Without it, or on ARM64, the import explains that
and does nothing else. The Microsoft 365 conversion service was considered as a fallback
and left out: it needs sign-in, an app registration, and an upload, and the use this is
for — preparing a talk on one's own machine — always has PowerPoint.

## Where it starts

- **File → Open** accepts `.pptx` and builds a new, untitled board from the deck. Saving
  offers the deck's name.
- **File → Import** adds a deck to the current board. It is a new command on the File row.
- **Dropping a `.pptx` from Explorer** does what Import does.
- A `.wimport` directive for decks is not part of this release.

All three show the same dialog.

## The dialog

It appears on every import, filled in with the choices made last time. Those choices are
remembered in the application settings and have no Preferences section of their own,
because they are made at import time and seen there.

```
Import PowerPoint — Inside the VertiPaq Engine.pptx (39 slides, 3 hidden, 4 sections)

  Pictures    (•) Auto   ( ) Sharp at any zoom   ( ) Exact look      Resolution [2560 ▾]
  Layout      (•) A row per section   ( ) One row   ( ) One column
  [ ] A frame around each slide
  [ ] Include hidden slides

                                                         [ Import ]  [ Cancel ]
```

| Setting | Choices | Default |
| --- | --- | --- |
| Pictures | Auto, Sharp at any zoom (SVG), Exact look (PNG) | Auto |
| Resolution | 1920, 2560, 3840 pixels wide | 2560 |
| Layout | A row per section, One row, One column | A row per section |
| A frame around each slide | on, off | off |
| Include hidden slides | on, off | off |

- **Pictures.** The labels say what a person gets; the format names are in the tooltips.
  **Auto** uses SVG for each slide and PNG for any slide whose SVG could not be captured
  or names a font Whiteboard cannot find (see *Fonts*).
- **Resolution** sits on the Pictures line and applies to PNG only. It is disabled when
  Pictures is SVG, because nothing would use it. 2560 is sharp on a 1440p projector and
  is about 45% of the size of 3840: the 39-slide deck made a 74 MB board at 3840.
- **A row per section** becomes a single row when the deck has no sections.
- **Frames are off** by default. A frame titles and outlines a slide, which helps while
  arranging a board and is noise while presenting; **View → Show frames** hides frames
  that were added. With frames on, each is titled with the slide number and title,
  `3. Tabular query architecture`, or `3. Slide 3` for a slide without a title. The
  section is left out because the row already shows it.
- A range of slides cannot be chosen yet. It is a later addition to the same dialog.

While the import runs the dialog shows `Slide 12 of 36` and Cancel. Cancelling adds
nothing to the board.

When it finishes and Auto used PNG for any slide, one line says so before the dialog
closes: `4 slides use a picture because their fonts are not on this PC`. Otherwise the
dialog closes on its own.

## On the board

- **Size.** Each slide is 1920 wide in board units, and as tall as the deck's aspect ratio
  makes it, so at 100% zoom a slide is one 1080p screen and pen widths feel as they do on
  an empty board. The gap between slides is a tenth of a slide width.
- **Placement.** On an empty board the first slide's top-left is at the origin. On a board
  with content, the slides go below it, left-aligned with it, so a second deck adds rows
  instead of lengthening the first deck's.
- **Afterwards.** The view fits the first imported slide, nothing is selected, and the tool
  is Select.
- **Undo.** The whole import is one step.

## How a slide becomes a picture

PowerPoint is driven through COM, late-bound, with no interop assembly, on a background
thread set up for COM, so the window stays responsive. The deck is opened read-only
without a window. A PowerPoint the person already has open is used and left open; one the
import started is closed when it finishes. The spike took 13 seconds for 38 slides.

- **PNG.** `Slide.Export(path, "PNG", width, height)`.
- **SVG.** `Slide.Export` has no SVG filter in the automation model, and no
  `SaveAs` format produces SVG. Copying a slide does: `Slide.Copy()` puts `image/svg+xml`
  on the clipboard, background included. The import reads it from there, retrying while
  another process holds the clipboard; the spike needed that on its first runs. A slide
  whose SVG cannot be read in five tries uses PNG.
- **The clipboard.** Capturing SVG this way replaces whatever was on the clipboard. The
  import saves the text or picture that was there and puts it back when it finishes, and
  says so only if that fails.
- **Slide facts** come from the same COM session: `PageSetup` for the size,
  `SectionProperties` and `Slide.sectionIndex` for sections,
  `SlideShowTransition.Hidden` for hidden slides, and the title placeholder for the title.

## Fonts

PowerPoint's SVG keeps text as text and names the deck's fonts. Microsoft 365 cloud fonts
such as Aptos, its default since 2023, and Segoe Sans live in Office's own cache
(`%LOCALAPPDATA%\Microsoft\FontCache\4\CloudFonts`), not in Windows. Since 1.6.3
`SvgImageCodec` hands the renderer the cache folders for the families an SVG names, so
those draw correctly. A font found in neither place is drawn with a substitute, and
because PowerPoint places each run of text at an absolute position, a wider substitute
runs words together. That is the case Auto sends to PNG.

Two more rewrites, in `SvgText`, make the right font land where PowerPoint put it:

- **Kerning.** SharpVectors draws a run as wide as its glyphs laid end to end; PowerPoint
  kerns, and places each run of a line where its kerned layout reached, having dropped the
  space that ended the run before. Unkerned, a long run reached into the next ("thecore").
  Each run is now squeezed horizontally, from where it starts, to the width WPF gives it
  with kerning: under 1% for ordinary text, and never more than 5%.
- **Weights written into a family.** PowerPoint names some weights as families, such as
  *Segoe Sans Small Semilight*. When that name cannot be found and the shorter one can, it
  becomes the family and a `font-weight` (350 here), which the renderer resolves.

## Without PowerPoint

The commands stay where they are. **File → Open** still lists `.pptx`, and a dropped deck
is still accepted, so that a person who expects it to work finds out why it does not:
`Importing a PowerPoint deck needs PowerPoint from Microsoft 365 on this PC.` Hiding the
commands would leave nothing to find.

## Not yet verified

- COM automation from the Store (MSIX) build. Out-of-process COM from a packaged app is
  expected to work; it has not been tried.
- Decks that are password-protected, open for editing in PowerPoint, or blocked by
  Protected View. Each needs a message rather than a stack trace.

## As built

- **Where the code is.** `SlideDeckLayout` in Core places the slides and titles the frames,
  with smoke tests. `PowerPointDeck` in the application drives PowerPoint on a thread of
  its own, `SlideClipboard` reads the SVG and keeps what the clipboard held, and
  `PowerPointImportWindow` is the dialog. `SvgImageCodec.CanDrawAsAuthored` is the test
  Auto applies to each slide. The dialog's settings are `PowerPointImport` in the
  application settings.
- **The dialog uses tiles, not drop-downs.** Pictures is three tiles, Auto, SVG, and PNG,
  each with its description on it, so the choice is read before it is made; a tooltip was
  considered and left out, because a pen or a finger cannot hover. Layout is three drawn
  tiles, like the pictured rows in Preferences, whose drawing helpers it borrows. The PNG
  resolution is one line, *PNG resolution: 2560 pixels wide*, whose value opens the list
  when clicked, and which is not shown when Pictures is SVG, since SVG has no pixels. The
  hidden-slides switch is always there, and disabled with *This deck has none* when the
  deck has no hidden slides.
- **Opening says what it is waiting for.** Starting PowerPoint and opening the file take
  seconds and cannot be counted, so the dialog shows the step, *Starting PowerPoint…* then
  *Opening the deck…*, an indeterminate bar, and the wait cursor; reading the slides is
  counted, as a percentage.
- **The summary names the reason.** A slide that falls back because PowerPoint did not hand
  over its SVG is counted apart from one whose fonts are missing.
- **Face names and kerning are rewritten** (see *Fonts*). Before the rewrite, Auto sent 8
  slides of the 47-slide test deck to PNG over *Segoe Sans Small Semilight*; afterwards all
  38 imported slides of it, and all 39 of the other deck, draw as SVG.
- **File → Import is new.** Before it, importing a file had no command on the File row; it
  now takes decks, images, and `.wimport` recipes, as a drop does.
- **Tried** on three decks: 13, 38 of 47, and 39 slides, with and without sections, hidden
  slides, and frames, into a new board and below an existing one. Not tried: the Store
  build, and password-protected or Protected View decks.
