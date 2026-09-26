# PowerPoint import

**Implemented for 1.6.3.** This document is the plan for importing a PowerPoint deck onto a
board as one picture per slide. The decisions below were taken on 26 September 2026 after a
spike: a console tool, since deleted, that wrote a PNG board and an SVG board from a deck.
The spike's findings are recorded here. *As built* at the end lists where the
implementation differs from the plan.

Background: [export.md](export.md) for frames and how Export cuts a board into slides, and
[decisions.md](decisions.md) for decision 14 (nothing leaves the machine) and decision 21
(managed-only dependencies). The import is consistent with both. It uses the PowerPoint
installed on the machine, adds no package, and sends nothing over the network.

## What it does

Each slide of a deck becomes one picture on the board, optionally inside a frame. A slide
is imported as SVG unless the SVG would not render correctly, in which case it is imported
as PNG. Converting slides into editable shapes and text was considered and left out,
because it would take weeks and would lose detail on any deck that uses gradients, groups,
or rich text.

The import requires PowerPoint from Microsoft 365. Without it, or on ARM64, the import shows
a message and does nothing else. The Microsoft 365 conversion service was considered as a
fallback and left out, because it requires sign-in, an app registration, and an upload. The
expected use is preparing a talk on one's own machine, where PowerPoint is installed.

## Where it starts

- **File → Open** accepts `.pptx` and creates a new, untitled board from the deck. Save
  suggests the deck's name.
- **File → Import** adds a deck to the current board. It is a new command on the File row.
- **Dropping a `.pptx` from Explorer** behaves like Import.
- A `.wimport` directive for decks is not part of this release.

All three open the same dialog.

## The dialog

The dialog appears on every import, filled in with the previous choices. The choices are
stored in the application settings. They have no section in Preferences, because they are
set in the dialog at import time.

The mock-up below is the plan. *As built* describes the dialog that was implemented.

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
| Pictures | Auto, SVG, PNG | Auto |
| PNG resolution | 1920, 2560, 3840 pixels wide | 2560 |
| Layout | A row per section, One row, One column | A row per section |
| A frame around each slide | on, off | off |
| Include hidden slides | on, off | off |

- **Pictures.** **Auto** uses SVG for each slide, and PNG for a slide whose SVG could not
  be captured or uses a font Whiteboard cannot find (see *Fonts*).
- **PNG resolution** applies to PNG only. 2560 pixels is sharp on a 1440p projector. A
  board made at 2560 is about 45% of the size of the same board at 3840; the 39-slide deck
  made a 74 MB board at 3840.
- **A row per section** places all slides in one row when the deck has no sections.
- **Frames are off** by default. A frame shows a slide's outline and title, which is useful
  while arranging a board and distracting while presenting. **View → Show frames** hides
  frames that were added. Each frame is titled with the slide number and the slide title,
  for example `3. Tabular query architecture`, or `3. Slide 3` when the slide has no
  title. The section name is not included, because each section has its own row.
- A range of slides cannot be selected yet. It can be added to the same dialog later.

While the import runs, the dialog shows `Slide 12 of 36` and a Cancel button. Cancelling
adds nothing to the board.

When the import finishes and Auto used PNG for any slide, the dialog shows a line such as
`4 slides use a picture because their fonts are not on this PC` and waits for **Done**.
Otherwise the dialog closes when the import finishes.

## On the board

- **Size.** Each slide is 1920 board units wide, and its height follows the deck's aspect
  ratio. At 100% zoom a slide fills a 1080p screen, and pen widths look the same as on an
  empty board. The gap between slides is a tenth of the slide width.
- **Placement.** On an empty board, the first slide's top-left corner is at the origin. On
  a board with content, the slides are placed below the content and aligned with its left
  edge, so each new deck adds rows below the previous ones.
- **Afterwards.** The view fits the first imported slide, nothing is selected, and the
  active tool is Select.
- **Undo.** The whole import is one undo step.

## How a slide becomes a picture

PowerPoint is controlled through late-bound COM, with no interop assembly, on a dedicated
background thread set up for COM, so the window stays responsive. The deck is opened
read-only and without a window. If PowerPoint was already running, the import uses it and
leaves it running. If the import started PowerPoint, it closes PowerPoint when it finishes.
In the spike, 38 slides took 13 seconds.

- **PNG.** `Slide.Export(path, "PNG", width, height)`.
- **SVG.** The automation model has no SVG export: `Slide.Export` has no SVG filter, and no
  `SaveAs` format produces SVG. `Slide.Copy()` places `image/svg+xml` on the clipboard,
  including the slide background, and the import reads the SVG from there. It retries
  while another process holds the clipboard; the spike needed the retries on its first
  runs. A slide whose SVG cannot be read after five attempts is imported as PNG.
- **The clipboard.** Reading SVG this way replaces the clipboard content. The import saves
  the text, picture, or files that were on the clipboard and restores them afterwards. It
  shows a message only if the restore fails.
- **Slide information** comes from the same COM session: `PageSetup` for the slide size,
  `SectionProperties` and `Slide.sectionIndex` for sections, `SlideShowTransition.Hidden`
  for hidden slides, and the title placeholder for the slide title.

## Fonts

PowerPoint's SVG keeps text as text and refers to the deck's fonts by name. Microsoft 365
cloud fonts, such as Aptos (the default font since 2023) and Segoe Sans, are stored in
Office's font cache (`%LOCALAPPDATA%\Microsoft\FontCache\4\CloudFonts`) and are not
installed in Windows. Since 1.6.3, `SvgImageCodec` passes the renderer the cache folders
for the font families an SVG uses, so text in those fonts renders correctly. A font found
neither in Windows nor in the cache is replaced by a substitute font. Because PowerPoint
places each run of text at a fixed position, a wider substitute makes words overlap. Auto
imports such slides as PNG.

Two more rewrites in `SvgText` correct the text position:

- **Kerning.** SharpVectors does not apply kerning, so it draws each run of text at the sum
  of its glyph widths. PowerPoint applies kerning and places each run of a line where the
  kerned previous run ended, without the space that separated them. Without kerning, a
  long run overlapped the next one ("thecore"). Each run is now scaled horizontally, from
  its start, to the width WPF computes with kerning. The scaling is under 1% for ordinary
  text and never more than 5%.
- **Weights in family names.** PowerPoint writes some font weights as part of the family
  name, such as *Segoe Sans Small Semilight*. When that name cannot be found and the
  shorter name can, the rewrite uses the shorter name as the family and adds a
  `font-weight` (350 in this example).

## Without PowerPoint

The commands are not hidden. **File → Open** still lists `.pptx`, and a dropped deck is
still accepted. The import then shows the message `Importing a PowerPoint deck needs
PowerPoint from Microsoft 365 on this PC.` Keeping the commands visible tells a person
what is missing when they try to import a deck.

## Not yet verified

- COM automation from the Store (MSIX) build. Out-of-process COM from a packaged app is
  expected to work but has not been tested.
- Decks that are password-protected, open for editing in PowerPoint, or blocked by
  Protected View. Each needs a message instead of an exception.

## As built

- **Where the code is.** `SlideDeckLayout` in Core places the slides and titles the frames,
  and has smoke tests. In the application, `PowerPointDeck` controls PowerPoint on its own
  thread, `SlideClipboard` reads the SVG and saves and restores the clipboard, and
  `PowerPointImportWindow` is the dialog. `SvgImageCodec.CanDrawAsAuthored` is the check
  Auto applies to each slide. The dialog's settings are stored as `PowerPointImport` in
  the application settings.
- **The dialog uses tiles instead of drop-downs.** Pictures has three tiles, Auto, SVG,
  and PNG, and each tile shows its description. Tooltips were considered and not used,
  because pen and touch input cannot hover. Layout has three tiles with drawings, in the
  style of the drawn choices in Preferences, and uses the same drawing helpers. PNG
  resolution is a single line, *PNG resolution: 2560 pixels wide*. Clicking the value
  opens the list of resolutions. The line is hidden when Pictures is SVG, because SVG has
  no resolution. The hidden-slides switch is always shown. When the deck has no hidden
  slides, the switch is disabled and the text under it reads *This deck has none*.
- **Opening shows progress.** Starting PowerPoint and opening the file take several
  seconds and report no progress. During those steps the dialog shows the step name
  (*Starting PowerPoint…*, then *Opening the deck…*), an indeterminate progress bar, and
  the wait cursor. Reading the slides reports progress as a percentage.
- **The summary gives the reason.** Slides that fell back to PNG because PowerPoint did not
  provide their SVG are counted separately from slides whose fonts are missing.
- **Font weights and kerning are rewritten** (see *Fonts*). Before these rewrites, Auto
  imported 8 slides of the 47-slide test deck as PNG because of *Segoe Sans Small
  Semilight*. After them, all 38 imported slides of that deck and all 39 slides of the
  other deck are imported as SVG.
- **File → Import is new.** The File row had no import command before. It now accepts
  decks, images, and `.wimport` recipes, the same files that can be dropped.
- **Tested** on three decks of 13, 47 (38 imported), and 39 slides, with and without
  sections, hidden slides, and frames, into a new board and below an existing board. Not
  tested: the Store build, and password-protected or Protected View decks.
