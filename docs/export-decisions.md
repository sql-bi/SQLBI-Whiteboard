# Export: calls made while implementing

The plan in [export.md](export.md) was approved as written, with its recommendations
taken as decisions. This file lists every further call made during implementation that
was not spelled out there, so that each can be reviewed and reversed. Each entry says
which phase made it and why.

## Branches and pull requests

- One branch per phase, each branched from the previous one because each phase builds on
  the last: `feature/export-e1-powerpoint`, `feature/export-e2-pdf`,
  `feature/export-e3-editable-deck`, `feature/export-e4-vector-pdf`,
  `feature/export-e5-frames`. Merge them in that order; after each squash merge, the next
  pull request's base has to be retargeted to `main`.
- The product version was not bumped and no CHANGELOG entry was written. Deciding when a
  release ships is not part of implementing a feature, and the Release notes check only
  fires when `VersionPrefix` changes. The changelog lines are ready to paste from the
  pull request descriptions.

## E1 — PowerPoint

- **Ctrl+E** is the shortcut, and **E** the mnemonic on the File strip (N, O, S, A were
  taken; Alt+E still opens the Edit tab because Alt always selects a tab). Both places in
  the key handler that know about Ctrl+S learned Ctrl+E.
- **The Export command sits at the end of the File row**, after Save As, with the same
  icon treatment as the rest.
- **The dialog owns the whole flow**: preview, settings, the save dialog, progress, and
  the error box. `MainWindow` only refreshes LiveView snapshots, hands over the document,
  the settings, the live-frame provider, the title resolver, and the current path, and
  refuses to open the dialog for an empty board.
- **Settings are remembered on Cancel too.** Someone who tuned the threshold and left
  will want it back next time, and nothing about the tuning is a commitment.
- **Text container titles come from the language service**, so a DAX container is named
  "DAX Code of Sales Amount" exactly as on screen. The partitioner in Core only sees the
  stored title; the application passes a resolver. Image areas are named after the image's
  original file name without extension, LiveViews after their source.
- **The dominant container is the largest one by area**, ties broken by creation order.
- **The picture on the slide keeps the area's own aspect ratio** and is fitted into the
  slide's picture box, rather than being cropped or stretched to 16:9. The bitmap is
  rendered at up to 3840×2160 (twice a full-HD slide).
- **The overview slide appears only when there are at least two areas** and the page
  model is one per area; with a single area it would repeat the only slide.
- **A whole-board export names its one slide after the board file** (or "Untitled board").
- **The gap slider snaps to 10 world units**, from 20 to 400, with 80 as default.
- **Smallest text offers 9, 12, and 14 points**, as the plan said; a settings file with
  another value falls back to 12.
- **The export is written to `<name>.pptx.tmp` and moved into place**, so a failure part
  way through never leaves a truncated deck.
- **The new assembly is `SQLBI.Whiteboard.Export`** (net10.0, no WPF), and it is listed in
  the pipeline's signing step. The smoke test project references it to open the produced
  deck, which means the core-only verification now restores the Open XML SDK package; it
  still needs no WPF.

## E2 — PDF

- **PDF is a choice in the same dialog**, not a second command. The dialog relabels itself
  (pages rather than slides), hides the slide size and the notes switch, and shows the
  page size and the footer switch. The preview and the areas are the same.
- **Pages are landscape only**, A4 or Letter. Boards are landscape; a portrait option
  was left out until someone asks.
- **Each page has a header line with the area's title** as well as the bookmark, so a
  printed page says what it is. The footer (board name, date, page n of m) is a switch, on
  by default.
- **No notes in a PDF.** There is nowhere for them that is not the page, and the text is
  in the picture already. Selectable text is what the vector phase brings.
- **The whole-board page is rendered at up to 6000 pixels on the longer edge** and sized
  at 144 dpi, so a 6000-pixel board becomes a 41-inch page, under the 200-inch limit
  viewers enforce. It has no header or footer: the page is the board.
- **The overview page** works as for the deck: first, only with two or more areas.
- **`PdfSharp` 6.2.4** is the writer, MIT and managed-only. Fonts come from the Windows
  fonts folder through its platform resolver; the export runs only on Windows.

## E3 — Editable deck

- **Slide content is a choice on the dialog, Picture or Editable**, shown for PowerPoint
  only. Picture stays the default: it is exact, and Editable is best effort by design.
- **Elements are placed in the picture's own pixel space.** The rasterizer exposes the
  camera it would have used, and every image, text box, and the ink overlay is mapped
  through it, so switching between the two modes moves nothing.
- **Bitmaps go in as they are when they are PNG or JPEG**, sniffed from the bytes rather
  than trusted to the stored content type, because boards written before SVG support have
  none. SVG, BMP, and GIF are rasterized at the size they take on the slide, through the
  same code the clipboard uses.
- **A text container is one rounded text box**: the language service's title as the first
  paragraph in Segoe UI semibold, then the body in the language's monospace font, with the
  syntax colors and weights carried over as runs. The text box has PowerPoint autofit
  switched off, so the text keeps the size it had on the board; a container that has
  been resized smaller than its text on the board is clipped there, and overflows here.
- **The ink is one transparent PNG over everything**, rendered by the same surface with
  the background off and only strokes drawn. It sits above every container, which is
  wrong only for a stroke drawn before an image was dropped on top of it.
- **A LiveView goes in as its current frame**, or its saved snapshot when nothing is
  connected. A missing image (an asset the board no longer has) is left out rather than
  exported as a placeholder.
- **The picture is still rendered for an editable page** and kept as the page's own
  picture in the model, so a page can fall back to it; PowerPoint does not receive it.
