# Mouse mode

**Accepted and implemented in 1.2.0, with every recommendation below taken.** The settled
form is decision 23 in [decisions.md](decisions.md); this document is kept because it holds
the alternatives that were weighed and rejected, which the decision only summarizes. Where
the two disagree, the decision is current.

It states what the mouse did before the change, every place where letting it draw
contradicted a decision already taken, what was proposed instead, and what the choice costs.
The "today" throughout is 1.1.5.

Background: [README.md](../README.md) for what the application does,
[decisions.md](decisions.md) for the choices behind it — decision 22 in particular, which
explains why pen ink is read from the pen rather than from the InkCanvas.

## Why this is being asked now

The application was built on a constraint: each input device gets the behaviour it is good
at, and none of them is asked to imitate another. The pen inks, with pressure, a rear
eraser and a barrel button. Touch navigates, and draws only when there is no pen. The mouse
navigates and moves things, and does not draw. That constraint is not a gap; it is the
reason the pen path is as direct as it is.

What happened after release is that people downloaded a whiteboard onto a laptop with no
pen and no touchscreen, and found that the toolbar did nothing. Version 1.1 says so out
loud at startup — `NoDigitizerWindow` — and points at
[discussion 78](https://github.com/sql-bi/SQLBI-Whiteboard/discussions/78) to collect
votes. That notice is already an admission: the application opens, and there is nothing
to draw with.

So the question is not whether a mouse is a good drawing instrument. It is not, and no
amount of code changes that. The question is whether the application should fail silently
for someone who has no pen, or give them something honest and limited.

## The organizing rule

If this ships, one sentence should govern every later decision about it, so that mouse
support does not become a tax on every future input feature:

> **A mouse gets the tools, not the gestures.**

Everything on the toolbar becomes reachable with a mouse. Nothing that exists because of
what a hand and a pen can do — pressure, hover, the rear end, the barrel button, palm
rejection, two fingers — is simulated for the mouse with modifier keys and timers. Where a
gesture has no honest mouse equivalent, the mouse does without it and the documentation
says so.

## What the mouse does today

Read off `MainWindow.InkSurface_PreviewMouseDown`, `_PreviewMouseMove`, `_PreviewMouseUp`,
`_LostMouseCapture` and `CompleteMouseAction`. Every mouse handler returns immediately when
`e.StylusDevice is not null`, which is how pen- and touch-promoted mouse events are kept out
of this path.

| Gesture | Behaviour | Mechanism |
| --- | --- | --- |
| Left drag | Borrows `Select` for one gesture: click a container to move it, drag the bottom-right handle to resize it. On release the tool reverts to `_lastDrawingTool` | `SetActiveTool(BoardTool.Select)` then `BeginContainerGesture`; `PointerAction.Container` |
| Left double-click | Center and fit the container under the pointer, or frame the whole board on empty canvas | `e.ClickCount >= 2` → `FrameContentAt` |
| Left drag, Laser active | Draws the laser trail at a fixed pressure of 0.5 | `PointerAction.Laser`, `MouseLaserPressure` |
| Middle drag | Pan | `PointerAction.Pan` |
| Right drag | Pan; returns to the drawing tool on release | `PointerAction.Pan` |
| Right-click on the palette | Hide or show the palette | `ToolPalette_PreviewMouseRightButtonDown` |
| Wheel | Zoom at the pointer; Shift+wheel zooms more slowly | `ZoomAtMouseWheel` |
| Movement | Restores the normal arrow and hides the pen's hover dot. Over a container with `Select` active, the cursor becomes a move or resize cursor | `UpdateSelectHover`, `SelectCursorAt` |

Two facts from that table matter more than the rest.

**The left button has one meaning, and it is not the active tool.** Left-drag is always
select-and-move, whatever tool is chosen, and the tool is handed back on release. That is
possible only because the mouse never draws. It is the single most convenient thing about
mouse input today — you can move an image without leaving the Pen — and it is exactly what
mouse drawing has to take away.

**The erase path is already written and currently unreachable.** `InkSurface_PreviewMouseMove`,
`CompleteMouseAction` and `InkSurface_LostMouseCapture` all handle `PointerAction.Erase`,
but no mouse-down ever assigns it. Mouse erasing is a branch in one method away from
working. That is not an accident to be tidied up; it is a measure of how much of this
feature already exists.

## What the mouse cannot report

These are physical, not oversights, and they define the ceiling.

- **No pressure.** `InkPoint` carries a pressure per point, `BoardSurface` renders width
  from it, and `CalligraphyDynamics.AdjustPressure` shapes it by speed. A mouse reports a
  button state.
- **No hover.** A pen in the air has a position and no contact, which is what
  `UpdateHoverPointerDot`, the laser's comet, and the dashed eraser square all attach to.
  For a mouse, button-up movement *is* the hover state, and there is no separate "about to
  touch".
- **No inverted end.** `e.StylusDevice.Inverted` is how the rear eraser and the upper side
  button are recognized (see TODO.md). A mouse has nothing equivalent.
- **No barrel button.** Laser and Straight line are assigned to it in Preferences. Both
  already have keyboard homes — `Alt+L` and `Shift` — so this costs nothing.
- **A lower, coalesced event rate.** `AppendPenInk` reads bursts of packets through
  `e.GetStylusPoints`. WPF coalesces mouse moves to roughly the frame rate. A fast mouse
  stroke will have visibly fewer points than a fast pen stroke and will look more angular.

## The conflicts, and what to do about each

### 1. The left button stops meaning "select"

**The conflict.** `InkSurface_PreviewMouseDown` hardwires left to `SetActiveTool(BoardTool.Select)`;
`CompleteMouseAction` and `InkSurface_LostMouseCapture` hand the tool back with
`SetActiveTool(_lastDrawingTool)`. If the left button draws, both have to go, and moving an
image stops being free.

**Proposed.** With mouse mode on, the left button does what the active tool does — ink,
erase, select, pan, laser — exactly as `InkSurface_PreviewStylusDown` already branches for
the pen. The tool becomes sticky: no automatic revert.

**And the convenience is kept, on a modifier.** `Ctrl` + left restores today's behaviour
precisely: borrow `Select` for one gesture, then return to the previous drawing tool. `Ctrl`
is free here — there is no multi-select to collide with — and it gives the mouse-mode user
one thing to learn rather than a lost capability. With mouse mode off, plain left is
unchanged, so nobody with a pen notices anything.

**Considered and rejected:** giving select to the right button. Right-drag pan is
documented, liked, and the only pan that needs no keyboard. Trading it for a modifier is a
worse deal.

### 2. Double-click to frame fires while drawing

**The conflict.** `e.ClickCount >= 2` is tested *before* the tool branch. Two quick dabs
with the Pen would center and fit the board. This is the conflict most likely to be found
by a user rather than by us.

**Proposed.** With mouse mode on and an ink or eraser tool active, plain double-click is
just two strokes, and framing moves to `Ctrl` + double-click — consistent with rule 1,
where `Ctrl` means "behave like the old mouse". With `Select` or `Pan` active, or with
mouse mode off, plain double-click still frames. Nothing changes for the pen, where
double-click framing lives on `Select` anyway.

### 3. Ink has no pressure

**The conflict.** Every stroke needs a pressure per point.

**Proposed.** A constant `MousePressure = 0.5f` — the same neutral value
`StraightLinePressure` already uses, chosen because WPF draws it at exactly the configured
thickness with no taper at either end. Then:

- **Highlighter** loses nothing: `InkDrawingAttributes` already sets `IgnorePressure` for it.
- **Calligraphy** loses almost nothing: its width comes from *speed*, through
  `AdjustPressure(pressure, _penInkSpeed)`, and speed is something a mouse has. A
  calligraphic mouse stroke will still thin as it accelerates, which is the whole point of
  the nib.
- **Pen** loses its taper. A mouse line is a uniform line.

**Considered:** deriving pen pressure from speed as well, so a mouse stroke tapers. Rejected
as a default because the width would vary for a reason the hand cannot feel or control, and
a wobbling line that the user did not ask for reads as a bug. Worth revisiting as a
Preference if anyone asks. **Open question 4** below.

### 4. Nothing shows what a click will do

**The conflict.** The pen's hover indicators — the high-contrast dot, the laser halo and
comet, the dashed eraser square — exist because a pen has an "about to touch" state.
`InkSurface_PreviewMouseMove` deliberately calls `HidePointerDot()` and restores
`Cursors.Arrow` for a physical mouse.

**Proposed, per tool:**

- **Ink tools:** replace the arrow with the same small high-contrast dot the pen hover uses.
  The arrow's hotspot is its tip, so it is not inaccurate, but its body covers the canvas
  down and to the right of where the ink will land. This is arguable; the arrow is
  defensible. **Open question 5.**
- **Eraser:** show the dashed `EraserHint` square, always, not only on hover. `EraserScreenRadius`
  is 12 px at zoom 1 and has nothing to do with the shape of an arrow, so without it
  nothing on screen says what a click will remove.
- **Laser:** no change. The pen's hover comet exists so the room can follow a pointer it
  cannot otherwise see; a mouse arrow is already on the projector. Today's behaviour —
  arrow while hovering, trail while dragging — is correct as it stands.
- **Select and Pan:** unchanged. `SelectCursorAt` already gives move and resize cursors.

### 5. Eraser and Pan are not on the toolbar

**The conflict.** `FingerToolsRow` holds exactly those two buttons and `ApplyFingerMode`
shows it only when finger drawing is effective — because with a pen, erasing is the rear
end and panning is touch or Space. A mouse has neither.

**Proposed.** Show the same row when *either* finger drawing or mouse drawing is on, and
rename it to say so. No new toolbar chrome: the palette is deliberately small, and the two
buttons that mouse mode needs are the two that already exist for the same reason.

`ApplyFingerMode` also forces the tool back to `_lastDrawingTool` when Eraser or Pan is
active and the row disappears. That guard has to consider both modes, or turning finger
drawing off would strand a mouse user on a tool whose button just vanished.

### 6. The startup notice becomes wrong

**The conflict.** `NoDigitizerWindow` says, in as many words, that there is no mouse
drawing yet, and links a vote for it. `SettingsCatalog`'s `WarnWhenNoDigitizer` description
repeats the claim. So do the site FAQ, guide and shortcut pages.

**Proposed.** Keep the notice and change its job. It stops being an apology and becomes an
orientation: mouse drawing is on, the left button draws with the selected tool, `Ctrl` moves
things, and here is what a pen or touchscreen would add. Keep the "do not show this again"
box — it is still the way out of a false positive from the tablet list.

Removing the notice entirely was considered. Against it: a whiteboard that silently behaves
differently on two machines is worse than one that says which mode it is in, and the list of
what a mouse cannot do is genuinely useful to someone deciding whether to buy a tablet.

### 7. Detection is unreliable, so this cannot be automatic only

**The conflict.** `NoDigitizerWindow.HasDrawingDevice()` and `HasStylusDigitizer()` both read
`Tablet.TabletDevices`. The codebase already warns twice — in `AppSettings` and in the
`FingerMode` help text — that this is a list of digitizers rather than an answer about what
is plugged in: a pen that has never been brought into range can be missing from it, and a
Surface reports a stylus whether or not one is in the room.

**Proposed.** A setting, mirroring `FingerMode` exactly:

```
MouseMode.WhenNoDigitizer   // default: on when Windows reports neither stylus nor touch
MouseMode.Off
MouseMode.On
```

Three reasons it is a setting and not pure detection: the probe is unreliable in both
directions; someone recording a demo may want mouse mode on a pen machine; and someone
whose Surface reports a stylus they do not own needs a way in.

Note the deliberate asymmetry with finger drawing. On a touchscreen with no pen,
`FingerMode.WhenNoPen` turns finger drawing on and `MouseMode.WhenNoDigitizer` leaves mouse
drawing off — because there is already something to draw with. Mouse mode is the last
resort, not the second choice.

### 8. Pen and mouse together

**Not a conflict, and worth stating so.** With `MouseMode.On` on a machine that has a pen,
nothing about the pen changes. Every mouse handler already returns early on a non-null
`StylusDevice`, so pen-promoted mouse events never enter the mouse path, and `AppendPenInk`
never sees a mouse. `_lastContactWasPen`, palm rejection, the barrel-button recovery in
`AccumulateMaskedTipInk` and the whole of decision 22 are untouched. The two paths run
beside each other and never meet.

This is the load-bearing fact of the whole proposal: mouse mode is an addition, not a
redesign. If it required one change to the pen path, it should be rejected.

## The proposed behaviour, in full

With **Mouse drawing** on:

| Gesture | Behaviour |
| --- | --- |
| Left drag | The active tool. Pen, Highlighter and Calligraphy ink; Eraser erases whole strokes; Select moves and resizes; Pan pans; Laser draws the trail |
| Shift + left drag | Constrain the stroke to horizontal or vertical, at uniform width. Works already: the constraint is read per point from `Keyboard.Modifiers` |
| Ctrl + left drag | Select, move or resize one container, then return to the previous drawing tool — today's plain left-drag |
| Ctrl + left double-click | Center and fit the container, or frame the board |
| Left double-click | With Select or Pan: frames, as today. With an ink or eraser tool: two strokes |
| Middle drag, right drag, Space | Pan. Unchanged |
| Wheel | Zoom at the pointer. Unchanged |
| Right-click on the palette | Hide or show the palette. Unchanged |
| `Alt+L` | Laser. Unchanged |
| Toolbar | Eraser and Pan buttons appear, as they do for finger drawing |
| Cursor | A dot for ink tools, the dashed square for the eraser, today's cursors for Select and Pan |

Everything else — undo and redo, containers, text, LiveView, import, paste, save, full
screen, the tab strip — is keyboard and menu work that already has no opinion about the
pointing device.

## What it costs

Said plainly, because the documentation will have to say it too:

- **A mouse line has no taper.** Only Calligraphy still varies its width.
- **Handwriting with a mouse is bad.** This makes the application usable without a pen. It
  does not make it good without one, and marketing should not imply otherwise.
- **Strokes are more angular** at speed, because of the event rate. Smoothing would change
  the ink model for every device and is deliberately out of scope — **open question 8**.
- **The rear eraser, palm rejection, pressure and hover have no mouse equivalent** and are
  not simulated.
- **A permanent tax on future input work**: every new input feature now has a third column
  to fill in. The organizing rule above is what keeps that column mostly reading "n/a".

## Implementation sketch

Included so the cost can be judged, not as a plan of record.

**`src/SQLBI.Whiteboard.Core/Settings/AppSettings.cs`** — a `MouseMode` enum and property,
a clause in `Normalize`, and `AppSettingsSerializer.CurrentVersion` from 12 to 13.

**`src/SQLBI.Whiteboard/SettingsCatalog.cs`** — one `SettingDescriptor` in the Input
category, `EnumChoice`, three choices; and a rewrite of the `WarnWhenNoDigitizer`
description, which currently states that a mouse cannot draw.

**`src/SQLBI.Whiteboard/MainWindow.xaml.cs`** — the whole of the behaviour:

- `IsMouseModeEffective`, beside `IsFingerModeEffective`.
- `PointerAction.Ink` added to the enum; `PointerAction.Erase` becomes reachable from the
  mouse for the first time.
- `InkSurface_PreviewMouseDown` branches on `EffectiveTool` when mouse mode is on, mirroring
  `InkSurface_PreviewStylusDown`, with the `Ctrl` path preserving today's behaviour.
- `InkSurface_PreviewMouseMove` gains an `Ink` case that feeds `AppendPenInkPoint` — which
  takes a screen point and a pressure and is already device-agnostic, so it should lose the
  `Pen` in its name — and sets `SceneSurface.PendingStroke`.
- `InkSurface_PreviewMouseUp`, `CompleteMouseAction` and `InkSurface_LostMouseCapture` gain
  an `Ink` case calling `EndPenInk`, and stop reverting the tool when mouse mode is on and
  the gesture was not a `Ctrl` borrow.
- `ApplyFingerMode` becomes the shared "which extra tools are on the toolbar" routine.
- Cursor and eraser-hint handling for the mouse, per conflict 4.

**`src/SQLBI.Whiteboard/MainWindow.xaml`** — rename `FingerToolsRow`.

**`src/SQLBI.Whiteboard/NoDigitizerWindow.xaml`** — rewritten per conflict 6.

**Documentation** — the README feature list and Controls table; `site/guide.html`,
`site/shortcuts.html`, `site/faq.html`; a new entry in `docs/decisions.md`.

No new subsystem, no new project, no change to persistence or to `Core` beyond one setting.
The reason it is this small is decision 22: because pen ink is collected from raw points
rather than from the InkCanvas, the ink pipeline from `AppendPenInkPoint` through
`CommitInkPoints` to `BoardSurface.PendingStroke` already has no idea what device it is
serving.

## Testing

`SQLBI.Whiteboard.Core.SmokeTests` is package-free and UI-free, so it can cover the
device-agnostic parts and nothing else: settings round-tripping with the new enum, the
version-12-to-13 migration, and the pressure constant if the policy moves into `Core`.

Everything else is manual, and the checklist should sit beside the Wacom Cintiq list in the
README:

1. On a machine with no pen and no touchscreen, confirm the default turns the mode on.
2. Draw with each of Pen, Highlighter and Calligraphy; confirm width behaviour matches
   conflict 3.
3. Shift mid-stroke, pressed and released, and confirm the constraint starts and ends there.
4. Erase; confirm the dashed square matches what is removed.
5. `Ctrl` + drag a container, and confirm the tool comes back.
6. Two quick dabs with the Pen, and confirm the board does not reframe.
7. On a pen machine with `MouseMode.On`, draw with the pen and confirm nothing about it
   changed — this is the regression that matters.

## Open questions

Numbered so they can be answered one at a time.

1. **Does the left button change meaning?** If not, there is no feature. Recommend yes.
2. **`Ctrl` + left for select-and-move, or make Select a tool you must pick from the
   toolbar?** Recommend `Ctrl`: it keeps the one genuinely good thing about mouse input
   today.
3. **Double-click framing behind `Ctrl` for ink tools?** Recommend yes; the alternative is a
   board that reframes itself while someone is drawing.
4. **Constant pressure, or speed-derived pressure for the Pen?** Recommend constant, and
   revisit only if someone asks.
5. **A dot cursor for ink tools, or keep the arrow?** Genuinely arguable. Recommend the dot.
6. **Default `WhenNoDigitizer`, or `Off` with an opt-in?** Recommend `WhenNoDigitizer`: the
   person this is for is the one who downloaded the application, found nothing worked, and
   will not go looking in Preferences.
7. **Does the no-digitizer notice stay?** Recommend yes, rewritten.
8. **Smoothing for mouse strokes?** Recommend not in a first version. It changes ink for
   every device and deserves its own decision.
9. **Is the mouse allowed into the marketing?** Recommend no more than a line in the FAQ and
   the shortcut page. The application stays a pen application that no longer fails silently
   on a mouse.

## Recommendation

**Build it, as a fallback, and say what it is.**

The case against is real and should be recorded: the founding constraint was that no device
imitates another, a mouse whiteboard is a worse whiteboard, and supporting one invites
requests from people the product was not built for. That last cost is permanent, and it is
the one to watch.

The case for is stronger on the specifics. The cost of building it is unusually low — a few
hundred lines, one setting, no new subsystem, and not one line changed in the pen path —
because the ink pipeline was already made device-agnostic for an unrelated reason. The
alternative is not "keep the constraint pure"; it is "keep shipping a startup dialog that
apologizes". And the constraint survives intact if the organizing rule is written into
decisions.md alongside the feature: a mouse gets the tools, not the gestures.

If any of the load-bearing pieces fails on inspection — if the pen path has to change, or if
the left button cannot be given up — that is the signal to stop, and the honest answer to
discussion 78 becomes no.
