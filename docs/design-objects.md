# Design objects: grid, area selection, shapes, text, and connectors

Status: shipped in 1.6.0, on 17 September 2026. This was the plan for
version 1.6.0, which ships issues
[#129](https://github.com/sql-bi/SQLBI-Whiteboard/issues/129) (background grid),
[#133](https://github.com/sql-bi/SQLBI-Whiteboard/issues/133) (selection by area),
[#130](https://github.com/sql-bi/SQLBI-Whiteboard/issues/130) (shapes),
[#132](https://github.com/sql-bi/SQLBI-Whiteboard/issues/132) (text), and
[#131](https://github.com/sql-bi/SQLBI-Whiteboard/issues/131) (connectors) as one release
with one `CHANGELOG.md` section. Each feature is its own pull request; the version bump is
the last one. The order above is the build order, and the reason is in
[Phases](#phases-and-order).

## Outcome and scope

What a person gets by upgrading:

- **A faint grid behind the board**, off by default, that scales with the zoom so the zoom
  level is visible at a glance. It never appears in a preview or an export.
- **Selection by area**: drag with Select on empty canvas for a rectangle, or turn on
  Lasso for a freehand outline. Everything the area takes — ink, pictures, text, shapes —
  moves, resizes, reorders, and deletes as one, and a color or size chosen with several
  things selected applies to all of them. A tap on a stroke selects that stroke.
  Preferences say whether an object must be fully or only partly inside, and whether the
  selection grows to what touches it.
- **Shapes**: rounded rectangle, ellipse, triangle, pentagon, block arrow, parallelogram,
  diamond, and stadium — the eight in the issue — drawn by dragging, with outline color,
  fill, and line thickness.
- **Text**: click to type a label anywhere, with font, size, color, bold, italic,
  underline, and rotation in 45° steps.
- **Connectors**: straight line, straight arrow, and curved arrow, that bind to a corner
  or a side midpoint of a shape or a container when an end is dropped there, and follow
  it when it moves.
- An **Insert** tab in the tab strip holds all of them. A Preference also puts an Insert
  button and a Lasso chevron on the floating toolbar, for those who accept it growing.
- Everything above is saved in the board, undone and redone, exported, and drawn by the
  same surface as the rest, so it shows in the Explorer thumbnail and the VS Code preview.

Out of scope for 1.6.0, unless a decision below says otherwise: text inside a shape,
rotating a shape, elbow connectors, connectors with an arrowhead at both ends, per-board
grid settings, and grouping as a saved object.

## Decisions taken

Settled with the maintainer on 16 September 2026. An implementer does not reopen these;
a change to one is a change to this file first.

1. **Merging while unattended.** Every feature lands as a pull request to `main`, and
   each merge publishes a Dev pre-release. The coordinator merges a pull request as soon
   as its checks pass, so the next one starts from `main`.
2. **Where the new tools live.** An **Insert** tab in the tab strip, between View and
   Help, holds the shape grid, the three connectors, and Text. The floating toolbar does
   not grow by default: it is critical that the compact layouts stay the width they are.
   A Preference, **Insert and Lasso on the toolbar** (off), adds an **Insert** button
   beside Select whose flyout is the same content, and a chevron on Select offering
   Rectangle and Lasso. With it off, Lasso is a toggle on the Edit row.
3. **After drawing one shape, connector, or label,** the tool stays, so the next drag
   draws another. Escape, or picking any tool, leaves it. A Preference, **After inserting
   an object** (Keep the tool / Return to Select), gives the one-shot behaviour.
4. **The shape set** is the eight in the issue's screenshot: rounded rectangle, ellipse,
   triangle, pentagon, block arrow, parallelogram, diamond, stadium. The first is a
   rectangle with rounded corners, not a square.
5. **Colors.** The six pen swatches for shape outline, connector, and text, and the same
   six as a translucent tint plus **None** for a shape's fill. Default outline and
   connector color and thickness: the current pen's. Default fill: None. Default text:
   Segoe UI 24, near-black.
6. **Connectors bind on release.** The binding points of a shape or a container are its
   four corners and four side midpoints. An endpoint released within 16 screen pixels of
   one binds to it and follows that object. With Ctrl held at release, the endpoint binds
   to the nearest point anywhere on the border instead. Ctrl is the modifier because
   Shift already means a straight line and Alt opens the tab mnemonics; under Mouse
   drawing, Ctrl at mouse-down still borrows Select, so the gesture starts without it.
7. **Fonts.** A curated list — Segoe UI, Calibri, Arial, Georgia, Times New Roman,
   Consolas, Cascadia Mono, Comic Sans MS, Segoe Print — with Segoe UI when a saved font
   is not installed. Consolas and Cascadia Mono are the monospace entries.
8. **What an area picks up.** Two settings under **Preferences → Selection**. **Area
   selects**: objects partly inside (default) or only objects fully inside. **Extend to
   touching**: Ignore (default), Single, or Recursive. An object touches another when its
   geometry intersects the other's bounds: a stroke by its points and segments, a
   connector by its path, anything else by its box. Single adds one round of touching
   objects; Recursive repeats the round while the last one added anything, never
   revisiting an object already examined. Ink strokes take part in area selection, and a
   single tap on a stroke with Select selects it.
9. **The grid** is an application preference, not saved in the board. Three choices in
   **Preferences → Board**: Off (default), Lines, Dots. A **Grid** toggle on the View row
   switches between Off and the last style. Spacing is 40 board pixels, coarsened by
   fours while it would be closer than 12 screen pixels.
10. **Editable exports.** Every feature pull request draws its objects into the overlay
    picture, so nothing vanishes from a deck or a PDF. A sixth pull request then writes
    shapes, connectors, and labels as native PowerPoint shapes and as vector PDF paths
    and text, in 1.6.0.
11. **Shapes and labels are containers,** so ink that touches only one links to it and
    moves with it. A shape is selected by its outline — the same 8-pixel band a frame
    uses — not by its interior, so what is drawn inside a shape stays reachable. A label
    is selected anywhere on its rotated rectangle.
12. **Copying several things.** Ctrl+C with more than one object selected puts a PNG of
    the selection on the clipboard, drawn by the rasterizer Export uses. A single text
    container or picture still copies as it does today.

## What all five build on

### The model

Three records join `BoardObjects.cs`, beside the four that exist. All are immutable
records with `Bounds` as the axis-aligned box the document indexes, so `Query`,
`ContentBounds`, the partitioner, and the preview need no change.

```text
ShapeBoardObject(Id, ZIndex, Bounds, Kind, OutlineArgb, FillArgb?, Thickness) : IBoardContainer
FreeTextBoardObject(Id, ZIndex, Bounds, Text, FontFamily, FontSize, Argb,
                    Bold, Italic, Underline, AngleDegrees, LayoutWidth, LayoutHeight) : IBoardContainer
ConnectorBoardObject(Id, ZIndex, Bounds, Kind, Start, End, Argb, Thickness,
                     StartAnchor?, EndAnchor?)
ConnectorAnchor(ObjectId, U, V)     // a point on that object's bounds, 0..1 each way
```

- `ShapeKind`: RoundedRectangle, Ellipse, Triangle, Pentagon, BlockArrow, Parallelogram,
  Diamond, Stadium, in the flyout's order. The outline geometry of each is a function of its
  `Bounds` in `Core` (`ShapeGeometry`), returned as a polygon or a list of segments and
  arcs, so hit testing and the vector export share it with the renderer.
- `ConnectorKind`: Line, Arrow, CurvedArrow. A curved connector is one cubic Bézier whose
  control points leave each end along the attached side's outward normal, or horizontally
  when free, by 40% of the end-to-end distance. `Bounds` is the box of the polyline that
  approximates the curve, inflated by the arrowhead.
- A label's `Bounds` is the axis-aligned box of its rotated layout rectangle, which is
  `LayoutWidth × LayoutHeight` centred on `Bounds.Center`. The WPF side measures the text
  and writes the layout size; `Core` never measures text. Rotation is about the centre.
- `WithBounds`, `WithZIndex`, and the selectable test move from `MainWindow` switches to
  the records themselves (`BoardObject.WithBounds`, `BoardObject.WithZIndex`, and a
  `HitTest(point, zoom)` per type), so adding a type does not touch `MainWindow`.
  This is the first thing the first pull request does, and it is what lets the later
  ones run in parallel.

### The archive

`BoardArchive.CurrentVersion` becomes 7. A board that contains any of the three new types
is written as 7; a board with only frames is still 6, and a board with neither is still 5,
extending the existing rule so a board keeps opening in the release that can read it. A
release before 1.6.0 refuses a version 7 board with the message it already has. The DTO
gains the fields the new records need, named for what they are (`ShapeKind`, `FillArgb`,
`Points`, `FontFamily`, …), all optional, all normalized on read: an unknown shape kind
becomes a rounded rectangle, an unknown font becomes the default, an angle is snapped
to 45°.

### Settings

All new settings go in `AppSettings`, normalized in `AppSettingsSerializer` and offered
through `SettingsCatalog`. `AppSettingsSerializer.CurrentVersion` becomes 19 once, in the
first pull request to merge; the later ones add their fields without bumping it again,
since every field has a default and 18 → 19 needs no migration.

| Setting | Values | Category | Pull request |
| --- | --- | --- | --- |
| `AreaSelection` | PartlyInside (default), FullyInside | Selection | 1 |
| `ExtendSelection` | Ignore (default), Single, Recursive | Selection | 1 |
| `AreaSelectionTool` | Rectangle (default), Lasso — what the Edit-row toggle remembers | Selection | 1 |
| `Grid` | Off (default), Lines, Dots | Board | 2 |
| `InsertOnToolbar` | off (default), on | Toolbar | 3 |
| `AfterInsert` | KeepTool (default), ReturnToSelect | Toolbar | 3 |
| `Shape`, `Connector`, `Label` | last-used colors, fill, thickness, font, size, style | not in Preferences | 3, 5, 4 |

### Rendering and hit testing

`BoardSurface.OnRender` draws the three new types in z-order with everything else, and
the grid first, under everything, only on screen: `BoardRasterizer` and
`BoardPreviewRenderer` leave `DrawGrid` false, as they already leave `DrawFrames` false.
`BoardDocument.HitTestTopContainer` becomes `HitTestTopSelectable(point, zoom)`, asking
each object's own `HitTest`: pictures, LiveViews, and text containers by interior, frames
by band or tab, shapes by an 8-screen-pixel band along their outline (decision 11),
connectors by distance to the path within 8 screen pixels, labels by the rotated
rectangle, strokes by the existing `HitTest` with the same 8-pixel radius.

### The selection set

`MainWindow._selectedObjectId` becomes a set of ids, and `BoardSurface.SelectedObjectId`
becomes `SelectedObjectIds`. A single selection is a set of one, and every place that reads
the old field reads the set: Delete, Copy, Bring to front, Send to back, F2 (only when the
set has one text container, label, or frame), the language chip and the LiveView overlay
(only when the set has one of theirs). The surface draws a thin outline on every selected
object, and one bounding rectangle with the existing corner handle around the whole set.
Moving the set translates every member and every stroke linked to a selected container
that is not itself selected; resizing scales the set about its top-left corner, aspect
preserved, through `TransformWithContainer` for strokes and `WithBounds` for the rest.
One `ReplaceObjectsCommand` records the gesture, as it does today for one container.

### The property bar

A floating bar above the selection's bounding rectangle, in `TextEditorLayer` beside the
language chip, that shows what the selected objects have in common and applies a change
to all of them as one `ReplaceObjectsCommand`:

| Row | Shown when every selected object is | Applies to |
| --- | --- | --- |
| Color swatches (the six pen colors) | a stroke, shape, connector, or label | stroke color, outline, line, text |
| Thickness chips (the four pen sizes) | a stroke, shape, or connector | thickness |
| Fill (None + six tints) | a shape | fill |
| Line kind (Line, Arrow, Curved) | a connector | kind |
| Font, size, B / I / U, rotate ↶ ↷ | a label | the label |

Nothing is shown for a picture, a LiveView, a text container, or a frame alone: they have
nothing to set here, and the language chip stays where it is. The bar flips below the
selection when there is no room above, and hides while a gesture or a text edit is in
progress. Its last choices are what the next shape, connector, or label is created with,
and they persist in `AppSettings` (`Shape`, `Connector`, `Label`), settings version 19.

### The Insert tab, and the optional toolbar button

An **Insert** tab joins the tab strip between View and Help (`SessionChrome`, with its
own row and Alt mnemonic like the others). Its row holds the eight shapes, a separator,
the three connectors, a separator, and Text — the issue's screenshot, laid out as one
row of the strip's buttons. Picking one selects that tool, which then behaves as
decision 3 says: it stays until Escape or another tool, unless the **After inserting an
object** preference says to return to Select.

With **Insert and Lasso on the toolbar** on (decision 2), the floating toolbar gains an
**Insert** button beside Select that opens a flyout in the style of the ink options with
the same content, and the Select button gains a chevron offering **Rectangle** and
**Lasso**, exactly as Pen has Pen and Calligraphy. In the Dual palette layout the Insert
button sits in the row with Select and Laser. With the preference off — the default —
neither exists, and the toolbar's width is unchanged from 1.5.2; the pull request states
the measured width of each layout with the preference off to prove it.

Lasso without the toolbar chevron: a **Lasso** toggle on the Edit row, checked while the
area tool is Lasso, remembered in `AreaSelectionTool`.

Icons come from Fluent UI System Icons (MIT, already used), added to `Toolbar.xaml` as
`Geometry` resources with the existing naming. If a glyph cannot be fetched, a plain path
of the same 24-unit grid is drawn by hand and noted in the pull request.

## Features

### Grid (#129)

- `GridStyle` enum (Off, Lines, Dots) in `AppSettings.Grid`; `SettingsCatalog` row
  `board.grid` under a new **Board** category with the three choices drawn as swatches.
- `Core.Viewport.GridGeometry.SpacingFor(zoom)`: 40 world pixels, multiplied by 4 until
  the screen spacing is at least 12 pixels. The spacing changes in steps, so a zoom shows
  as the lines spreading and then snapping coarser, which is the cue the issue asks for.
- Drawn in `BoardSurface` before the objects with a frozen pen of `#14000000` (lines) or
  1.5-pixel dots of the same gray on the intersections. Clipped to the viewport.
- **View → Grid** toggle button with the grid icon, checked while the style is not Off.
- Smoke test: spacing steps at zoom 1, 0.25, 0.05, and 16; a preview rendered with the
  grid on is byte-identical to one rendered with it off.

### Area selection (#133)

- Drag with Select on empty canvas: a dashed rectangle in the selection blue, updated
  each move; on release, the objects the area takes are selected. Under **Partly inside**
  (default) an object is taken when its geometry intersects the area: a stroke by any
  point or segment, a connector by its path, anything else by its bounds. Under **Fully
  inside** every point of a stroke, every point of a connector's path, and every corner of
  anything else must be inside. Frames are never taken by an area.
- Lasso: a dashed closed polyline drawn from the pen or mouse path; the same two rules
  against the polygon by the even-odd test. The polygon and rectangle tests live in
  `Core.Geometry.Polygon` and are what the smoke tests exercise.
- **Extend to touching** then grows the set: Single adds every object that touches a
  selected one; Recursive repeats until a round adds nothing, keeping a visited set so no
  object is examined twice. "Touches" is the Partly-inside test of one object's geometry
  against the other's bounds, in `BoardDocument.Touching(id)`. Strokes linked to a
  selected container come along regardless, as they do today.
- Ctrl and a tap or a rubber band adds to the selection instead of replacing it.
- Escape, a tap on empty canvas, and picking a drawing tool clear the selection.
- Delete removes the set and the strokes linked to selected containers. Bring to front
  and Send to back move the set as one block, keeping its internal order.
- Ctrl+C with several objects renders the selection's bounds through `BoardRasterizer`
  with a filter to the selected ids and puts the PNG on the clipboard.
- The property bar with Color and Thickness for strokes: recoloring a highlighter stroke
  keeps its kind and its highlighter alpha, so it stays a highlighter.
- Pen path: the same gesture from `InkSurface_PreviewStylusDown` under Select, so the
  rubber band and the lasso work with a pen, a finger under Finger drawing, and the mouse.
- Smoke tests: rectangle and lasso containment, group translate and scale with linked
  strokes, deletion group of a set, z-order of a block.

### Shapes (#130)

- Drag defines the bounding box; Shift constrains it to a square. A tap without a drag
  inserts a 160 × 120 screen-pixel shape at the tap, as Microsoft Whiteboard does. The
  new shape is selected, so the property bar can change it, and the tool stays for the
  next drag (decision 3); Escape or any tool button leaves it, and the **After inserting
  an object** preference makes it hand back to Select instead.
- Selected by its outline band, never its interior (decision 11); moved by dragging that
  band; a tap inside an unfilled or filled shape reaches whatever is drawn there.
- Drawn as a `StreamGeometry` from `ShapeGeometry` transformed by the camera, filled then
  stroked, with round joins. The stroke width scales with the zoom like ink.
- Corner handle: free resize (width and height follow the pointer); Shift keeps the
  aspect. This differs from pictures, whose handle keeps the aspect, and the cursor is the
  same; a shape is the one container that is allowed to change proportion.
- Editable export in this pull request: shapes are drawn into the ink overlay picture, in
  both the editable deck and the vector PDF, by widening the overlay filter and by emitting
  the overlay in the vector path too. The native mapping is pull request 6.
- Smoke tests: geometry of each kind at a known box, hit test inside and outside, archive
  round trip and version 7, normalization of an unknown kind.

### Text (#132)

- Click with the Text tool places the caret at the click and opens the editor: a WPF
  `TextBox` in `TextEditorLayer` with the label's font, size, color, and style, a
  `LayoutTransform` for the angle, no title bar, transparent background, and a thin
  border. Enter is a new line. It grows with its content.
- Commit on click-away and Ctrl+Enter; Escape cancels, restoring the previous text, as it
  does for a text container. An empty label on commit is deleted, and if it was new,
  nothing is recorded in the history.
- F2 on a selected label reopens the editor. Double-click still centres and fits.
- Rotation: ↶ and ↷ in the property bar step 45°; the layout box is re-measured and the
  bounds recomputed as the rotated box. Selection outline follows the rotated rectangle.
- Corner handle scales the font size, as it scales a text container's `VisualScale`.
- Rendered with `FormattedText` under a `RotateTransform` about the centre; underline
  through `TextDecorations`.
- Editable export in this pull request: same overlay picture as shapes. Native rotated
  text is pull request 6.
- Smoke tests: bounds for a 90° and a 45° label, hit test on a rotated label, archive
  round trip with every font property, snapping of a saved angle of 50° to 45°.

### Connectors (#131)

- Drag from start to end. While the pointer is within 16 screen pixels of one of the eight
  binding points of a shape or container — four corners, four side midpoints — those
  eight are drawn as small dots, and release binds the endpoint to the nearest and records
  the anchor. With Ctrl held at release, the endpoint binds to the nearest point anywhere
  on that object's border instead (decision 6). Both ends can bind, to different objects.
  The tool stays after a connector is drawn, as it does for a shape.
- Selecting a connector shows the two endpoint handles instead of the corner handle;
  dragging one re-routes it and can re-attach it. Dragging the body moves both ends and
  detaches them.
- When an attached object moves or resizes — in a container gesture, a group gesture, an
  undo, or a load — the connector's attached endpoint is recomputed from the anchor's
  U, V on the new bounds. `MainWindow` does this in the same place it transforms linked
  strokes; `BoardDocument.ConnectorsAttachedTo(id)` finds them. Deleting a shape detaches
  the connectors that touched it rather than deleting them.
- Arrowhead: a filled triangle, 4 × thickness long, at the end. A curved arrow's head
  follows the curve's end tangent.
- `BoardPartitioner.BuildUnits` puts a connector in the unit of the objects it attaches
  to, so an export area does not cut between a shape and its arrow.
- Editable export in this pull request: same overlay picture. Native `cxnSp` and PDF
  paths are pull request 6.
- Smoke tests: anchor following on move and resize, detachment on delete, curve bounds,
  hit test near and far from the path, partitioner unit merging.

### Native editable export (pull request 6, if decision 10 says so)

- `EditableSlide` emits a `SlideShapeElement` (preset geometry name, outline, fill,
  thickness), a `SlideConnectorElement` (start, end, kind, arrowhead), and a
  `SlideTextElement` with rotation for a label. `PptxDeckWriter` writes `p:sp` with
  `a:prstGeom` (`rect`, `roundRect`, `ellipse`, `triangle`, `pentagon`, `diamond`,
  `parallelogram`, `flowChartTerminator`, `rightArrow`), `p:cxnSp` with
  `straightConnector1` or `curvedConnector3` and a `tailEnd` triangle, and `a:xfrm rot`
  on the label's shape. `PdfDocumentWriter` draws the same as paths and rotated text.
- The overlay picture then excludes what went out natively, as it already excludes
  images and text containers.
- The WPF smoke harness checks the runs and shapes an editable slide carries for a board
  with one of each.

## Phases and order

| PR | Feature | Depends on | Runs in parallel with |
| --- | --- | --- | --- |
| 1 | Selection set, per-type hit test and `WithBounds`, rubber band, lasso, property bar with color and thickness, stroke selection (#133) | — | 2 |
| 2 | Grid (#129) | — | 1 |
| 3 | Shapes, the Insert tab, the optional toolbar button and Select chevron, the two Toolbar preferences (#130) | 1 | 4 |
| 4 | Text labels (#132) | 1 | 3 |
| 5 | Connectors (#131) | 3 | — |
| 6 | Native editable export | 3, 4, 5 | 7 (docs part only) |
| 7 | Cut 1.6.0: version, `CHANGELOG.md`, `site/`, `README.md`, `docs/decisions.md` | all | — |

PR 1 goes first because it changes the one field every other feature would otherwise
also change, and because it moves the per-type switches out of `MainWindow`, which is
what lets 3 and 4 add a type each without editing the same lines. PR 2 is small and
touches the start of `OnRender`, settings, and the View row, none of which PR 1 edits
heavily. PRs 3 and 4 both add a record, an archive case, a draw method, and a tool; the
second to merge rebases and resolves those additions, which are appends rather than
rewrites. PR 5 needs shapes to attach to. PR 6 is optional. PR 7 is the only one that
changes `VersionPrefix`, so it is the only one the **Release notes** check applies to.

Pull request 7 also writes decision 31 in `docs/decisions.md` — that design objects are
board objects drawn by the same surface, saved as version 7, and that a shape is a
container — and updates `TODO.md`.

## How the work is run

- One agent per pull request, on its own worktree and branch: `feature/area-selection`,
  `feature/background-grid`, `feature/shapes`, `feature/text-labels`,
  `feature/connectors`, `feature/native-design-export`, `release/1.6.0`.
- Every agent reads `CONTRIBUTING.md`, this file, and `docs/decisions.md` before
  editing, builds with `dotnet build Whiteboard.sln -c Release`, and runs both smoke
  harnesses. `TreatWarningsAsErrors` is on. A build shell must not override `APPDATA`
  in the same command as a `gh` call, or `gh` reports it is not logged in.
- Pull request title in the imperative; description says why and what it affects, with
  the attribution trailer. No version bump and no `CHANGELOG.md` entry before PR 7.
  `README.md` and `site/` change only in PR 7, so the feature branches do not collide on
  prose.
- The coordinator reviews each pull request's diff against this file, runs the build and
  the harnesses from the branch, merges when the checks pass (decision 1), and then starts
  the next agent from the new `main`. A pull request that needs a rebase is rebased by its
  own agent, never by merging `main` into it.
- Anything that cannot be tested by the harnesses — the feel of a drag, the flyout with
  a pen, a rotated label under the mouse — is listed in the pull request under
  **To try by hand**, for the maintainer in the morning.

## Where the code goes

| What | Where |
| --- | --- |
| Records, `ShapeKind`, `ConnectorKind`, `ConnectorAnchor`, per-type `HitTest`, `WithBounds` | `src/SQLBI.Whiteboard.Core/Model/BoardObjects.cs` |
| `ShapeGeometry`, `Polygon`, connector curve math | `src/SQLBI.Whiteboard.Core/Geometry/` |
| `GridGeometry` | `src/SQLBI.Whiteboard.Core/Viewport/` |
| Archive version 7 | `src/SQLBI.Whiteboard.Core/Persistence/BoardArchive.cs` |
| `GridStyle`, shape/connector/label defaults, settings version 19 | `src/SQLBI.Whiteboard.Core/Settings/` |
| Drawing, selection set, grid | `src/SQLBI.Whiteboard/BoardSurface.cs` |
| Property bar | `src/SQLBI.Whiteboard/PropertyBar.xaml`, `.cs`, hosted from `MainWindow.xaml` |
| Insert tab, Lasso and Grid toggles | `src/SQLBI.Whiteboard/SessionChrome.xaml`, `.cs` |
| Optional Insert button, Select chevron, icons | `src/SQLBI.Whiteboard/MainWindow.xaml`, `Themes/Toolbar.xaml` |
| Tools, gestures, label editor | `src/SQLBI.Whiteboard/MainWindow.xaml.cs` |
| Grid row, View toggle | `SettingsCatalog.cs`, `PreferencesWindow.xaml.cs`, `SessionChrome.xaml`, `.cs` |
| Editable export | `src/SQLBI.Whiteboard/Export/EditableSlide.cs`, `src/SQLBI.Whiteboard.Export/` |
| Tests | `tests/SQLBI.Whiteboard.Core.SmokeTests/Program.cs`, `tests/SQLBI.Whiteboard.SmokeTests/` |

## Toward a solid 1.6.0: what first use showed, and the order to fix it

Status: shipped in 1.6.0, on 18 September 2026. Approved earlier the same day after the
maintainer tried the Dev build, and everything in priorities 1 to 4 is in. The version did
not change.

What first use showed, and the cause of each in the code as it stands:

- **Lasso on the Edit row is not where anyone looks for it.** The mode belongs with the
  Select tool, and the toolbar was not allowed a chevron by default.
- **A shape tool that stays makes the shape just drawn hard to change.** The tool stays by
  default (decision 3), so a tap on the new shape starts another shape instead of selecting
  it; and the Insert row is a sticky tab, so it covers part of the toolbar while drawing.
- **Connectors are hard to bind.** `BindingAt` binds only when the pointer is within 16
  screen pixels of one of the eight points. Over the middle of a shape, or near its edge
  but not near a point, an endpoint is left free, and the dots only appear inside that
  reach, so there is nothing to aim at until one is already hit.
- **Shapes cannot rotate,** and a block arrow that cannot be turned points one way only.
- **A shape cannot carry text,** which is the first thing a diagram needs.
- **There is no select-all,** and the only way to Delete, Copy, or reorder a selection is
  the keyboard or the View row.

### Priority 1 — make the tools behave (one pull request)

1. **One shape, then Select.** `AfterInsert` defaults to ReturnToSelect; the preference
   stays for whoever wants a sticky tool. The new object is selected, so the property bar
   is right there, and a tap on it moves it rather than starting another.
2. **The Insert row closes when drawing starts.** The Insert tab leaves `IsStickyTab`, so
   it closes on the first press on the canvas like File and View do. It reopens with one
   tap or Alt+I.
3. **Lasso lives on the Select button.** The Edit-row toggle goes. Holding Select for one
   second switches between Rectangle and Lasso, and the button's glyph shows which one is
   active; a tap on Select while it is already the active tool does the same, so a mouse
   can reach it without waiting. The tooltip says both. The chevron stays behind the
   toolbar preference. `ClickMode=Press` means the press already selects the tool; the
   timer only adds the switch.
4. **A connector binds wherever it is dropped on a shape.** While the pointer is over a
   target (its bounds inflated by 16 screen pixels), the eight dots show, the one that
   would be taken is drawn larger, the preview endpoint snaps to it live, and the target's
   outline is tinted; release binds there. Ctrl still means the nearest point anywhere on
   the border. A drop over nothing leaves the end free. The same rule applies to the
   start of a drag and to an endpoint handle being dragged.
5. **Select all.** With Select active, Ctrl+A selects every object on the board except
   frames (the same set an area can take), and Ctrl+Shift+A selects the ink strokes only.
   Both go through `SelectMany`, so the property bar, Delete, Copy, and the group gesture
   follow. Ctrl+S stays Save: it is Save in every Windows application and in this one
   since 1.0, and a Save that instead selected ink would lose someone their work.
   Neither shortcut does anything while a text is being edited (the editor owns them).

### Priority 2 — shapes complete (four pull requests)

6. **Angle on a shape.** `ShapeBoardObject` gains `AngleDegrees` (default 0), kept beside
   the unrotated box the way a label keeps its layout size: `Bounds` becomes the box of
   the rotated outline, the outline and hit band rotate about the centre through
   `RotatedRectangle`, the eight binding points rotate with it (anchors stay U,V in the
   unrotated frame, so a bound arrow turns with the shape), and the corner handle keeps
   scaling the unrotated box. The property bar's ↶ ↷ row applies to shapes and labels
   alike. PowerPoint takes `a:xfrm rot`; the PDF rotates the path. The archive field is
   optional, so an earlier Dev build reads the file and ignores the angle.
7. **Text inside a shape.** `ShapeBoardObject` gains `Text`, `FontFamily`, `FontSize`,
   `TextArgb`, `Bold`, `Italic`, `Underline`, the same set a label has, defaulting to the
   Label defaults with the text empty. The text is drawn centred in the shape's unrotated
   box, wrapped to the box's width less a margin, turned with the shape, in z-order with
   the shape itself; `LabelVisual` measures it. Editing: **F2**, the **Text** button on the
   property bar, or simply typing a printable character while a lone shape is selected
   (as PowerPoint does) opens the same `TextBox` editor a label uses, centred over the
   shape and sized to its box; Ctrl+Enter and click-away commit, Escape cancels, and an
   empty text is fine, the shape stays. The property bar then shows the Font row (font,
   size, B / I / U) for a shape too, and a text color swatch row distinct from the outline
   row: the outline row keeps its place, the text row sits with the font. Export: the
   PowerPoint shape carries the text in its `p:txBody`, centred and wrapped, so it is
   editable in place; the PDF draws it centred. The partitioner's `DefaultTitle` for a
   shape becomes its first line. Ctrl+C on a lone shape with text copies the text.
8. **The property bar's overflow.** A **…** button at the end of the bar opens a menu for
   the current selection: **Delete**, **Copy**, **Duplicate**, **Bring to front**,
   **Bring forward**, **Send backward**, **Send to back**. Duplicate (also Ctrl+D) adds a
   copy of the selection offset by 24 screen pixels right and down, with new ids, the
   strokes linked to a duplicated container duplicated with it, and a connector between
   two duplicated objects bound to the copies; the duplicate becomes the selection. The
   four z-order commands act on the selection as one block, as the View row's two do
   today; forward and backward move the block past the one object it meets next. The View
   row gains Forward and Backward beside the two it has. No Lock, no Alt text, no Comment.
9. **A rotation handle.** A small circle above the selection's top edge, for a lone shape
   or label; dragging it turns the object freely, Shift snaps to 15°, and the ↶ ↷ buttons
   keep the 45° steps. Angles are then any value, and the archive's snapping to 45° is
   dropped for both.

### Priority 3 — a palette for inserting, that can be moved (one pull request)

The Insert row is a menu, not a palette: it is far from the pen and it covers the
toolbar. Four designs were considered:

- **A. Pin the Insert row into a floating palette.** A pin at the end of the Insert row
  turns its content, the eight shapes, the three connectors, and Text in two short rows,
  into a second floating palette that is dragged by a grip, kept anywhere in the window,
  remembered in settings, shown or hidden from the pin, the View row, or Preferences. It
  reuses the shared button list the toolbar flyout is built from, never touches the main
  toolbar, and stays in F11 and canvas-only. *Recommended.*
- **B. The existing toolbar Insert button, always on.** Already built, one preference
  away; it widens the compact toolbar by 42 px, which decision 2 rules out by default,
  and its flyout closes after one pick.
- **C. A bubble beside the last inserted object** offering the next shape. Quick for a
  run of shapes, but it moves with the work and covers it.
- **D. Tear-off: drag the Insert row out** into a palette. The same result as A with a
  gesture nobody will find.

### Priority 4 — connectors that start from a shape

10. **Connector handles on a selected shape.** Four small arrows at the side midpoints of
    a selected or hovered shape; dragging one out draws an Arrow already bound at that
    point, and dropping it on another shape binds the end by rule 4. This is what draw.io
    and Visio do, and it removes the trip to the Insert row for the common case. Needs 6,
    since the handles turn with the shape.
11. **Automatic anchors.** An anchor mode, Auto, where the bound end moves to the side
    facing the other end whenever either object moves, so an arrow between two shapes
    stays sensible as they are rearranged. Fixed stays the default for an end dropped on a
    specific point.
12. **Connection sites in the deck.** Write `stCxn`/`endCxn` on the PowerPoint connector
    so it re-routes when a shape is dragged in PowerPoint.

### Order and parallelism

| PR | Items | Depends on | Parallel with |
| --- | --- | --- | --- |
| A | 1, 2, 3, 4, 5 | — | B, D |
| B | 6 rotation | — | A, D |
| C | 7 text in shapes | B (the text turns with the shape) | D, E |
| D | 8 overflow, Duplicate, z-order | — | A, B, C |
| E | 9 rotation handle | B | C, F |
| F | Design A palette | A | E, G |
| G | 10 connector handles | B, 4 | F |
| H | 11, 12 | G | — |

CHANGELOG.md's 1.6.0 section is rewritten at the end, not per pull request, and stays at
three or four entries; the guide, shortcuts, and README follow in the same last pull
request, as PR 7 did.

### Decisions taken

Settled with the maintainer on 18 September 2026. An implementer does not reopen these.

1. **Select all.** Ctrl+A selects everything an area can take; **Ctrl+Shift+A** selects the
   ink strokes only. Ctrl+S stays Save.
2. **The long press** on Select is **600 ms**, the Windows touch long-press, and a second
   tap on the already-active Select button also switches Rectangle and Lasso.
3. **Typing while a lone shape is selected** starts its text, as PowerPoint does; F2 and
   the bar's Text button do the same.
4. **Rotation** has both the ↶ ↷ 45° buttons and the free handle with Shift snapping
   to 15°, for shapes and labels.
5. **Z-order** has four commands: Bring to front, Bring forward, Send backward, Send to
   back, on the bar's overflow and on the View row.
6. **The palette** is design A: a pin on the Insert row makes a second floating palette.
7. **1.6.0 includes all of priority 4**, connector handles, automatic anchors, and
   PowerPoint connection sites. There is no release date; the Dev channel carries every
   merge until the maintainer promotes the build.

### Estimate

Sizes are agent working time on Opus, from the seven pull requests of 16 and 17
September (30 to 55 minutes each, 215k to 485k tokens each, about 2.6M tokens in all).
Coordinator review, a local build and both harnesses, and the CI run add 15 to 25
minutes per pull request.

| PR | Items | Agent time | Tokens |
| --- | --- | --- | --- |
| A | Tools behave: 1 to 5 | 1 h | 350k |
| B | Rotation on shapes | 1 h | 350k |
| C | Text inside a shape | 1.5 h | 500k |
| D | Overflow menu, Duplicate, four z-order commands | 1 h | 350k |
| E | Rotation handle | 45 min | 250k |
| F | Floating Insert palette | 1 h | 350k |
| G | Connector handles on a shape | 1 h | 350k |
| H | Automatic anchors, connection sites | 1.5 h | 450k |
| I | Notes, guide, shortcuts, README, decision 32 | 30 min | 200k |

About 9.5 agent hours and 3.2M tokens. Run in the waves the order table allows — A, B,
D together; then C, E, F; then G; then H; then I — the wall clock is 6 to 8 hours
unattended, the same shape as the first night. Each wave lands on the Dev channel, so the
maintainer can try a wave's build while the next one runs; anything that comes back from
that goes in before I, not after.

### How the work is run

As for the first seven pull requests: one Opus agent per pull request, on its own
worktree and branch (`feature/tools-behave`, `feature/shape-rotation`,
`feature/shape-text`, `feature/selection-menu`, `feature/rotation-handle`,
`feature/insert-palette`, `feature/connector-handles`, `feature/connector-routing`,
`docs/1-6-0-notes`). The coordinator plans, reviews each diff against this file, builds
and runs both harnesses from the branch, merges when the checks pass, starts the next
wave from the merged `main`, and writes no code. A pull request that meets a conflict
merges `origin/main` in, never rebases. Each pull request lists under **To try by hand**
what the harnesses cannot see.

## Modes: Teaching, Design, Custom

Status: approved on 19 September 2026, not started. One pull request, `feature/modes`.

1.6.0 added controls that a person who only annotates never needs: an Insert tab, a
palette, handles on a selected shape, a bar over every selection, a menu on it, depth
commands, a lasso. A **mode** says which of those exist. It is a setting, changed in
Preferences or with one tap on the View row, and it never changes what a board contains:
a board made in Design mode opens in Teaching mode with every shape, label, and connector
still drawn, still a container, still exported; only the tools to make or restyle them are
absent.

### The three modes

- **Teaching**: the 1.5.2 experience plus what is invisible until used, and the extended
  selection with it. Ink, laser, eraser, pan; paste, drop, import, LiveView, text
  containers; frames and export; the grid; rubber band, lasso, a tap on a stroke, Ctrl+A,
  Ctrl+Shift+A, group move and resize; Bring to front and Send to back. Nothing else.
  The lasso is in because picking up what you have just drawn is annotation, not design,
  and it puts nothing on the screen: it is the Select button behaving differently after a
  hold.
- **Design** (default): everything.
- **Custom**: whichever of four feature groups are switched on, and nothing else. It
  exists so that a change meant for Teaching can be tried one group at a time.

### The four feature groups

Each is one switch, and Teaching is Extended selection alone, Design all four on:

| Group | What it turns on |
| --- | --- |
| **Design tools** | The Insert tab and its row, the Insert palette and its pin, the toolbar Insert button and the Select chevron (still behind their own preference), the shape, connector, and label tools, typing / F2 / the bar's button to edit a shape's or a label's text, the rotation handle and the ↶ ↷ row, the connector handles on a selected shape, the Anchors row, and the Toolbar preferences that only concern them (Insert and Lasso on the toolbar, After inserting an object, Insert palette). |
| **Property bar** | The bar above a selection: color and thickness for strokes, fill, font and text color, and the … menu. With this off and Design tools on, a shape's text and rotation are reached by F2 and the handles only. |
| **Extended selection** | A tap selecting a stroke, Lasso (the hold, the second tap, the chevron), and the two Selection preferences. With it off, the rubber band takes what is partly inside and never grows, whatever the stored preferences say. |
| **Depth and duplicate** | Bring forward, Send backward (View row and menu), Duplicate and Ctrl+D. |

What is passive in Teaching or with a group off: a shape, label, or connector already on
the board is drawn, selected by its outline or rectangle, moved, resized from the corner,
deleted with its ink, exported natively; a connector still follows its shapes. A key or a
command that belongs to a group that is off does nothing — no dialog, no beep.

### Where it is set

- **Preferences → Mode**, a new first category: a **Mode** row (Teaching, Design, Custom)
  and the four group switches, enabled only while Custom is chosen. Choosing Teaching or
  Design does not overwrite the Custom switches, so Custom remembers its last
  arrangement, and while one of them is chosen the four rows show the feature set that
  mode resolves to rather than the switches behind it — a greyed row says what the mode
  does. The Toolbar and Selection rows that belong to a group are hidden while
  that group is off, so Teaching's Preferences are 1.5.2's plus Mode, Board, and
  Selection. The four group rows draw their two pictures where a combo sits, beside the
  words rather than on a line below them, and at half the size of the rows drawn before
  1.6.0: all five rows of this category have to be on the screen together at the dialog's
  default size, and on their own line they cost more height than five rows have.
- **View → Design**, a toggle beside Grid: checked while the mode is Design; a press
  switches to Design, and a press while in Design returns to the last mode that was not
  Design (Teaching, or Custom if that is where the person came from), the way Grid
  remembers its last style. Access key D is taken on the View row; pick a free letter.
- Settings version stays 19; `Mode` (default Design), `LastNonDesignMode` (default
  Teaching), and the four switches (default true), all normalized.

### What it touches

`AppSettings` and `SettingsCatalog`; a `FeatureSet` resolved from the settings in Core
(`Modes.Resolve(settings)`), which is the one thing the window asks; `SessionChrome` (the
Insert tab collapsed, the two depth buttons collapsed, the Design toggle); `MainWindow`
(every creation tool, handle, hover, key, and the palette consult the feature set; the
property bar is not shown at all with its group off); `PropertyBar` (rows and the menu
by group); `BoardSurface` (handles only when their group is on). Tests: settings round
trip and normalization, and `Resolve` for Teaching (Extended selection alone), Design
(all on), and a
Custom arrangement.

The notes: one sentence folded into an existing 1.6.0 entry, not a fifth entry; the
README's Preferences sentence and a Controls row for View → Design; one sentence in the
guide where Preferences are described; decision 33 in `docs/decisions.md`.
