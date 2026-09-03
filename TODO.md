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
(1.3.0). Identity version for the Store package is `VersionPrefix.0` (`1.3.0.0`).

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
reasoned about. winget is the one piece still waiting, below. All of it is described in
[docs/release-management.md](docs/release-management.md).

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

- **The straight-line constraint cannot start mid-stroke from a button on this pen.** It
  can from Shift, and from the barrel button, because both are reported while the tip is
  down. Anything reported only through `Inverted` is not, since Invert and Tip are
  mutually exclusive on this device.

- **Two constants stand in for signals the hardware does not give.** `AppendPenInk` calls
  four consecutive weightless packets a lift rather than a dropped reading — no digitizer
  misses four readings in a row. `DefaultActivationDistance` is 24 px: how far the pen
  must travel before the axis is settled. It was 8 px, which let a few milliseconds of
  the previous direction pick the axis; a trace of real strokes is the way to revisit it.
  Once settled the axis is kept for the whole segment, however far off it the hand
  drifts — turning a corner instead was tried and produced a staircase out of a diagonal.

## Waiting on the first winget submission

Not work, but the reason winget is not finished yet.
[microsoft/winget-pkgs#421386](https://github.com/microsoft/winget-pkgs/pull/421386)
submits `SQLBI.Whiteboard` 0.9.2 and is in review. Nothing in this repository depends on
it: the workflow that keeps the package current afterwards is already in place, and the
token it needs is set.

Until that pull request merges, `.github/workflows/publish-winget.yml` fails on every
release, because `wingetcreate update` has no previous version to read. That failure is
visible and gates nothing.
