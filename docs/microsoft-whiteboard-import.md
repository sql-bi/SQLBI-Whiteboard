# Microsoft Whiteboard import

**Implemented in 1.7.0**, with the objects of older versions of the app in 1.7.1 and
1.7.2. This document records what a Microsoft Whiteboard export contains, how the
importer maps it onto a board, and what it cannot carry over. Microsoft retires its
standalone Whiteboard apps on 16 October 2026 (see [retirement](retirement/README.md)),
and the export is the only way to take a board out of it. The format is undocumented.
Everything below was established from eight exports of real teaching boards and one test
board holding every kind of object the Windows client offers, made between 20 and 27
September 2026. Six more teaching boards, exported on 29 September, added the objects
that older versions of the app wrote.

## Where it starts

- **File → Open** accepts the export's `.zip` and creates a new, untitled board from it.
  Save suggests the export's name.
- **File → Import** and dropping the `.zip` from Explorer add the board below the current
  content, aligned with its left edge, as a PowerPoint deck does. One undo removes it.
- A `.zip` that is not an export is refused on all three paths. It used to be tried as
  text and silently skipped.

There is no dialog. When the board holds something the importer does not read, a message
lists it by type with a count after the import.

## What the export contains

The export is a ZIP with two files, `<name>.html` and `<name>-comments.json`. The
importer recognizes it by that pair and reads only the archive's directory to do so, so
classifying a dropped file stays cheap.

The HTML is the page Microsoft's web client renders, saved with its styles. It carries
no model of the board beyond what it draws.

- `#canvasContent` holds one `div.anchor` per object, in drawing order. The object's
  type is `data-whiteboard-type`. The page ends with elements that carry the `anchor`
  class but no type, such as `#remoteInkPool`, and the importer stops an object there.
- An anchor is a box of no size at `left` and `top`, in canvas pixels. Its inline
  `transform: matrix(a, b, c, d, e, f)` scales, turns, and moves the object around that
  point, because the transform origin of a zero-size box is its position. Ink, text,
  notes, grids, connectors, and older polygons start at the point; shapes, older
  ellipses, and pictures are centered on it. The inline transform overrides the
  `translate(-50%, -50%)` of the `center` class.
- Text is a Draft.js editor: one `div[data-block]` per line, with the characters in
  `span[data-text]`. The editor's `textBoxCore` style carries color, family, weight,
  italic, and underline for the whole object; no board in the samples styles a run.
- The comments file is `{"description": …, "commentThreads": [{"id", "comments": [{"author": {"name", "email"}, "body", "displayDate"}]}]}`.
  A pin on the page carries `aria-label="Comment hint: <id>"`. Every export has two
  `CommentThread` anchors with no hint, which hold nothing.

### The object types

| Type | What the page holds |
| --- | --- |
| `InkGroup` | An SVG with a view box whose origin sits at the anchor, class `PenStroke`, `Highlighter`, or `Mixed`. One `g.inkStroke` per stroke with `transform="matrix(0.0078125, …)"`: 1/128 of a pixel. Inside it, a filled `path` outline and a `polyline.inkHitTestOverlay` centerline. |
| `FluidImage` | `div.imageComponent` with the size, and an `img` with a base64 data URI labelled `text/plain` whatever the picture is. |
| `AzureImage` | A picture on a board made with an older version of the app, written as a `FluidImage` is, with the data URI labelled `image/*`. |
| `DocumentPage` | A page of an inserted PDF, written as a `FluidImage` is. |
| `ReactionStickers` | A sticker, written as a 64 × 64 `FluidImage` with an SVG data URI. |
| `Shape` | An SVG whose `g` has `fill`, `stroke`, and `stroke-width` (in `pt`), and whose `path` draws the outline around the middle of the box. The shape's text sits in `div.textBoxContainer`. |
| `LegacyEllipse` | A shape from an older version of the app: an SVG `ellipse` with the outline in its `stroke` attribute and the width and fill in its style. The radii can differ from the SVG's box, in proportion as well as in size, and the page draws the radii. |
| `LegacyPolygon` | A shape from an older version of the app: an SVG `polygon` whose points start at the anchor, styled as a `LegacyEllipse` is. Every sample is a rectangle. |
| `Note` | A sticky note: `div.textBoxBackground` with a color class such as `softBlueGradient`, a 40-pixel author bar, and `div.stickyNote` with the text area's size and font size. |
| `GridList` | A note grid: a title editor, then `div.listChild` notes in a CSS grid of `repeat(n, auto)` columns. |
| `PlainText` | A text box: `div.textbox.plainText` with `max-width` and `font-size`, inside a wrapper whose `justify-content` centers the text in a wider box when the text is centered. |
| `Connector` | An SVG with the route as a path from the anchor, and the head as a small path moved to one end with `translate`. |
| `Hyperlink` | A preview card with the page's picture, an `a` with the link and its title, and the description. |
| `CommentThread` | A pin, described above. |

### Ink

- **Pen outline.** A band along the centerline with a round join at every point, plus
  separate circles and capsules for dots and short pieces. Every arc is drawn around a
  centerline point with half the ink's width there as its radius. About 60% of the
  strokes in the samples have one width; the rest vary by up to 3.3 times, because the
  pen reported pressure.
- **Highlighter outline.** The tip is an axis-aligned rectangle twice as tall as it is
  wide, drawn once at the start and swept along the centerline. Its height follows
  pressure. The fill is the color at 0.4 alpha.
- **Highlighter without a centerline.** Older versions of the app wrote some highlighter
  strokes with an empty `polyline` and a scale other than 1/128. The outline is a square
  tip followed by the sweep, as overlapping pieces, and the path cannot be recovered from
  it reliably. Seven samples on three boards are scribbles that fill a frame, a region,
  or a table, or mark a few words. `Whiteboard 1` had lost its three until 1.7.2, with no
  message, because such a stroke was taken for an empty one.
- **Arrowheads** on a pen are part of the centerline: it runs out to each barb's tip and
  back, so they need nothing of their own.
- **Rainbow and Galaxy** ink fill the outline with a `url(#…)` pattern that uses
  `#rainbow-pen` or `#galaxy-pen`, pictures on Microsoft's servers.
- **Opacity** is the fill's alpha.
- **Partial erasing** splits a stroke into separate strokes. The stylesheet's
  `.eraseMask` class is never used.
- **Ink turned into shapes** by *Enhance inked shapes* stays ink, with a perfect outline.

Only three SVG path commands occur in ink: `M`, `L`, and `A`. Shapes add `Q`.

### Layout the page does not state

Sizes that come from the stylesheet were measured on the test board rendered by Edge:

- A text box has 16 pixels of padding and wraps at its `max-width` less that padding.
- A note is its text area plus the 40-pixel author bar: 304 × 305 by default.
- A note grid puts its notes on a 320-pixel pitch, 17 pixels from the left and 81 from
  the top, below a 30-pixel bold title, with 16 pixels below the last row.
- A link card is 22 pixels wider than its title area.

Note colors are fixed per class in the stylesheet. The importer carries the twelve the
Windows client offers, from `paleYellowGradient` (#FEE15A) to `grayGradient` (#C6C6C6).

## How it maps

| Export | Board |
| --- | --- |
| Pen stroke | `InkStrokeObject`, `PenKind.Pen`, the fill as its color |
| Highlighter stroke, or a translucent stroke in a `Mixed` group | `InkStrokeObject`, `PenKind.Highlighter`, the color made opaque |
| Highlighter stroke without a centerline | An SVG asset of its outline, in its color and opacity, in the box it covers |
| Rainbow or Galaxy stroke | A pen stroke in violet (#8B5CF6) or indigo (#312E81) |
| Picture, document page, sticker | `BoardAsset` with the sniffed type, and an `ImageBoardObject` |
| Turned picture | An SVG asset that draws the picture turned, in the box it covers |
| Shape | `ShapeBoardObject` of the kind its outline has, with fill, outline, angle, and text |
| Older ellipse or polygon | `ShapeBoardObject`, an oval with the ellipse's radii or the kind the polygon's corners have, without text |
| Rectangle, in either form | `ShapeBoardObject`, `ShapeKind.Rectangle`, with square corners |
| Sticky note | `ShapeBoardObject`, rounded rectangle in the note's color, with the note's text |
| Note grid | A white rounded rectangle for the panel, a label for the title, and one note per cell |
| Text box | `FreeTextBoardObject`, wrapped into the lines the box showed |
| Connector | `ConnectorBoardObject`, straight, with its ends bound to shapes they sit on |
| Link card | Markdown `TextBoardObject` titled *Link* |
| Comment thread | Markdown `TextBoardObject` titled *Comment*, in a column to the right of the board |
| Anchor order | Z-order |
| Anything else | Counted and reported, not imported |

Coordinates stay in canvas pixels, one to one with board units, so a 4-pixel Microsoft pen
is as wide as the 4-unit pen here. A new board puts the export's top-left corner at the
origin. A stroke that touches exactly one imported container travels with it, which is the
rule for a stroke drawn by hand.

**Shape kinds** are read from the outline, because the shape's name on the page is
written in the language of whoever made the board. Curves with straight sides are a tube
(`Stadium`), curves alone an oval (`Ellipse`). Three corners are a triangle, five a
pentagon, seven a block arrow. Of four corners, two on the horizontal midline are a
diamond, all four on the box's sides a rectangle, and anything else a parallelogram.

**A shape turned by a quarter** is sometimes stored as its box turned that way, with
`textBoxContainer` turning the text back by -90°. The importer adds that turn to the
anchor's angle and swaps the box's sides.

**Text** is placed at the box's corner plus its padding, turned with the box. Core cannot
measure text, so the window passes in `IBoardTextMeasure`, backed by the same WPF layout
that draws labels and containers. A label here keeps the lines it is given, so the importer
wraps the text at the box's width first, word by word. A label's font is the nearest of
the label fonts: Simple (Aptos) and anything unknown become Segoe UI, Handwritten is
Segoe Print or, on older boards, Ink Free, both of which are label fonts, and a serif
family becomes Georgia. The lines are found by measuring in the family the page names,
because that is where the text box broke them. Until 1.7.2 Ink Free became Segoe Print,
which is wider, and measuring in it split single-line titles in two. A family that is
not installed is measured in WPF's fallback.

**Width.** WPF draws a point at the style's thickness × (0.25 + 1.5 × pressure), so
pressure 0.5 is the thickness itself. Each centerline point takes twice the radius of the
arc whose ends lie one radius from it, and points without such an arc are interpolated
from their neighbours. The stroke's thickness is its median width, raised when the widest
point would need a pressure above 1, and each point's pressure is solved from its width.
An earlier version measured the distance from each point to the nearest edge of the
outline. That made small handwriting too thin, because the outline of a small loop
overlaps itself.

**Highlighter width.** A highlighter here has one width per stroke, a tip twice as wide
as tall, and a fixed opacity. Its thickness is half the upper quartile of the vertical
edges in the outline, so the height of a horizontal highlight matches the body of the
original stroke.

**Connectors** are bound at an end that lies within a few pixels of the edge of an
upright shape, as a fraction of its box. A turned shape is not bound, because its
fractions are measured in its own turned box.

The HTML is read by a small tokenizer in Core rather than by HtmlAgilityPack, which the
application already references, so the importer can stay in the framework-neutral Core
assembly and be covered by its smoke tests. The largest sample, 22 MB with 25 pictures,
parses in about 160 ms.

## What is lost

- **Pressure is approximated.** The export keeps the outline, not the pressure. The
  recovered widths match the original closely at 150% zoom, but not exactly.
- **Highlighter shape.** The tip here is wider than tall, while Microsoft's is taller than
  wide. A horizontal highlight has the same height and starts and ends a few pixels
  further out. A vertical highlight comes out four times as wide as the original. A
  highlight has one width throughout.
- **Rainbow and Galaxy** are one color each.
- **Notes and note grids have rounded corners**, because they are rounded rectangles here.
- **An old highlighter scribble is a picture**, not ink, because its path is not in the
  export. It moves and deletes as a picture, and it keeps the few small gaps the page
  shows where the outline's pieces overlap.
- **Note text is centered** in the note, where Microsoft puts it at the top left, and
  text too large for its note overflows it rather than being cut off.
- **Elbow connectors are straight.** Their ends stay where they were and stay bound.
- **A turned picture cannot be turned back**, because it is an SVG of the turned picture.
- **A link card loses its preview picture.** The link and its description are kept.
- **Reactions on notes** are counted and left out. The board has no reactions.
- **Comments** become text containers beside the board, not pins on their objects.
  Authors keep their names; their email addresses are left out.
- **Author names on notes, locks, and alt text** are left out, because the board has no
  place for them.
- **The board's background** color and grid are left out, because a board here has no
  background color and the grid is a preference.
- **Colors** are kept exactly, so they are usually not colors from this application's
  palettes.
- **Very tall boards** cannot be seen whole. The camera's minimum zoom is 5%, and two
  samples are about 20,000 and 35,000 units tall. At 200% display scaling the whole of
  the first does not fit, and the second does not fit at any scaling.

## Not seen yet

- The Professional font, and text boxes with more than one style in them
- PowerPoint pages, which the Windows client cannot insert, videos, and Loop components
- SVG pictures, which the Windows client refuses to insert
- Groups, which the Windows client does not offer

Each would be reported by type if it arrives as an unknown object. Sticky notes cannot be
rotated in the Windows client, with the mouse, the pen, or touch.

## Verification

Each sample was opened in the application and compared with the same page rendered by
Edge, fitted to the same content. Positions, picture sizes, scaled ink groups, and colors
matched on all eight teaching boards. The six later boards were opened in the
application, where the older ellipses, rectangles, and pictures sit in place against the
ink drawn around them. The highlighter pictures, square frames, and one-line titles of
FEvsSE, Venn diagrams, and Whiteboard 1 were compared with Edge at the same fit. Regions with pressure-drawn handwriting and with highlighters were
compared at 150% against a WPF rendering that uses the application's own drawing
attributes. The test board was compared region by region in the application:
ink, text boxes, notes, the note grid, shapes, connectors, pictures, the template, and the
comments. The Core smoke tests cover the transforms, width recovery, highlighter
classification, shape kinds and quarter turns, notes, grids, wrapping and centering,
connector binding, links, comments, turned pictures, Galaxy ink, placement, container
binding, the objects of older versions, and recognizing the ZIP, on three synthetic pages
built like an export.
