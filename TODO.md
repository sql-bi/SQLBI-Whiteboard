# TODO

Outstanding work. Items are written to be picked up cold: each says what to change
and why it matters.

Background reading, in this order:

- [CONTRIBUTING.md](CONTRIBUTING.md) — branch and pull request workflow. `main` is protected.
- [docs/release-management.md](docs/release-management.md) — how the project is built and shipped.
- [docs/decisions.md](docs/decisions.md) — why it is built that way. Each entry says whether it
  is implemented or only agreed.

## Where the project stands

The delivery chain works end to end: a merge to `main` builds, signs, and publishes a
pre-release to GitHub Releases, and one approval promotes that same build to a release.
<https://whiteboard.sqlbi.com> reads its download links from the release manifest
deployed beside it and needs no edit per release. The current product version is `VersionPrefix` in `Directory.Build.props`
(1.6.0). Identity version for the Store package is `VersionPrefix.0` (`1.6.0.0`). 1.6.0 is
the design objects release — a background grid, selection by area, shapes that carry their
own text and turn to any angle, text labels, connectors that bind anywhere on a shape and
can route themselves, a movable Insert palette, and a commands menu on the selection,
planned in [docs/design-objects.md](docs/design-objects.md) and settled in decisions 31 and
32. Left out on purpose: text inside a connector, elbow connectors, arrowheads at both ends,
Lock, handles on a label or a picture, and connection sites for a curved connector in
PowerPoint.

Declaring that number is decision 20 in [docs/decisions.md](docs/decisions.md). What 1.0
was waiting on shipped during 0.9.x: Preferences, `.wimport`, Explorer and VS Code
previews, the public documentation site, and Finger drawing (default when no pen is
detected).

No numbered work remains. 1.2.0 answered
[discussion 78](https://github.com/sql-bi/SQLBI-Whiteboard/discussions/78) with Mouse
drawing — decision 23, with the alternatives kept in
[docs/mouse-mode.md](docs/mouse-mode.md) — and 1.2.1 made it discoverable from the
toolbar, decision 24. 1.2.2 puts the Eraser within reach of a pen whose back end is not
one, as an option that is off by default. The video teaser is recorded and served from the landing page
itself as `site/teaser-av1.mp4` / `site/teaser-h264.mp4` — the Vimeo-embed plan was
reversed, see decision 19 in [docs/decisions.md](docs/decisions.md); the production
script and staging assets are in `docs/teaser/`. The release manifests and the Store
submission are done and proven: the manifests are live at
<https://whiteboard.sqlbi.com/stable.json>, and the pipeline's Store stage has carried
two releases through certification unattended. The install-side verification list was
walked in full for 1.0.0, which was the last part of the chain that had only ever been
reasoned about. winget closed the chain on 24 September 2026, when the first submission
merged. All of it is described in [docs/release-management.md](docs/release-management.md).

## Show frames: review once it has been used

**View → Show frames** hides frames while presenting. It is saved with the board, hidden
frames still define the slides for Export, and they are neither drawn nor hit by the pen.
One rule was agreed provisionally: **View → Frame** on a board whose frames are hidden
shows them all again, in the same undo step as the new frame, because otherwise the new
frame would arrive selected and invisible. Collect usage feedback once it ships and decide
whether that rule stays. The alternatives are to show only the new frame, which needs a
per-frame visibility the board does not have, or to leave the frames hidden and say so.
The PPTX import will create a frame per slide, which is what will exercise this most.

## Pen buttons: what was settled, and what is left

The barrel button is the only assignable one, and it takes Laser or Straight line. Adding
an action means an entry in `PenButtonAction`, a choice in `SettingsCatalog`, and — if it
swaps the tool rather than acting as a modifier — a case in `MainWindow.BarrelToolFor`.
Nothing else needs to know.

Erasing is not assignable. The reverse end of the pen erases, and so does the upper side
button, because they cannot be told apart:

- **The upper button and a reversed pen are the same signal.** A trace from the
  development pen (`PenTrace`, enabled by pointing `SQLBI_WHITEBOARD_PENTRACE` at a file)
  settles what several rounds of inference could not. The device exposes exactly two
  buttons, `Tip Switch` and `Barrel Switch` — no eraser button, no secondary tip button.
  Clicking the upper side button and turning the pen round produce identical events:
  `Inverted` goes true, both switches stay up, pressure stays zero, and when either one
  lands the same tip switch closes. So an inversion is the eraser, full stop. A device
  that reports a real `SecondaryTipButton` would be distinguishable, and supporting one
  would mean re-introducing a second slot — worth doing only if such a device turns up.

- **The barrel switch masks the tip switch, and the ink there is recovered by hand.** A
  barrel press and a barrel release each arrive as a stylus up with `InAir` true. After a
  release the pen keeps reporting `Tip Switch=Up` and `InAir` until the button is pressed
  again - while the tip is still on the glass, and while the packets still carry its real
  pressure (0.54 rising to 0.69 across one such gap in the trace). WPF delivers those as
  in-air moves, so the InkCanvas collects nothing and the ink drawn in between was lost.
  `AccumulateMaskedTipInk` keeps them instead and commits them as a freehand stroke when
  the gap ends. Two consequences worth knowing: a barrel transition splits the line into
  separate stroke objects, which shows as a seam where a highlighter overlaps itself and
  as several undo steps; and the recovered stretch appears when the gap closes rather
  than under the tip, because there is no wet-ink path for points the InkCanvas never
  sees. Giving it one means drawing a provisional stroke on the scene surface.

- **Straight line is chosen at stroke start.** Hold Shift or the barrel button before
  touching down; a press during writing is ignored. Releasing a modifier disables its
  constraint for the rest of the stroke. This also applies to a pen button mapped to
  Shift. The pressure-based contact tracking is described in decision 22.

- **Two constants stand in for signals the hardware does not give.** `AppendPenInk` calls
  four consecutive weightless packets a lift rather than a dropped reading — no digitizer
  misses four readings in a row. `DefaultActivationDistance` is 24 px: how far the pen
  must travel before the axis is settled. It was 8 px, which let a few milliseconds of
  the previous direction pick the axis; a trace of real strokes is the way to revisit it.
  Once settled the axis is kept for the whole segment, however far off it the hand
  drifts — turning a corner instead was tried and produced a staircase out of a diagonal.
