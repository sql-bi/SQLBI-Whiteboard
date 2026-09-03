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
