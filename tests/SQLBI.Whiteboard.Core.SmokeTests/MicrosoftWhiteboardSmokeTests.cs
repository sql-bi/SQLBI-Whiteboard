using System.IO.Compression;
using System.Text;
using SQLBI.Whiteboard.Core.Geometry;
using SQLBI.Whiteboard.Core.Import;
using SQLBI.Whiteboard.Core.Model;

namespace SQLBI.Whiteboard.Core.SmokeTests;

/// <summary>
/// A page shaped like a Microsoft Whiteboard export, reduced to the elements the
/// importer reads. Stroke coordinates are in 1/128 of a pixel, as in the real files.
/// </summary>
internal static class MicrosoftWhiteboardSmokeTests
{
    private const string OnePixelPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

    private static readonly string Page = $$"""
        <html><head><style>.anchor { position: absolute; }</style></head>
        <body style="overflow: auto;">
        <div class="transformComponent" style="transform: translate(10px, 20px) scale(0.5);">
        <div id="canvasContent" class="contentOrigin" data-blueprint-type="Canvas">
        <div class="anchor includedInExportedPdf align topLeft" data-whiteboard-type="InkGroup" style="left: 100px; top: 200px; transform: matrix(0.5, 0, 0, 0.5, 4, 6);">
          <div class="canvasChild"><svg class="inkGroup ink PenStroke" viewBox="-10 -20 100 50" overflow="visible" width="100" height="50">
            <g class="inkStroke interactive" transform="matrix(0.0078125, 0, 0, 0.0078125, 0, 0)">
              <path d="M0,-256L2560,-256A256,256 0 0 0 2560,256L0,256A256,256 0 0 0 0,-256" fill="rgba(231,18,36,1)" opacity=""></path>
              <polyline class="inkHitTestOverlay" stroke-linecap="round" stroke-width="2639" points="0,0 1280,0 2560,0 "></polyline>
            </g>
            <g class="inkStroke interactive" transform="matrix(0.0078125, 0, 0, 0.0078125, 0, 0)">
              <path d="M0,1152A128,128 0 0 0 0,1408M1280,1024A256,256 0 0 0 1280,1536M2560,896A384,384 0 0 0 2560,1664" fill="rgba(0,105,191,1)" opacity=""></path>
              <polyline class="inkHitTestOverlay" stroke-linecap="round" stroke-width="2639" points="0,1280 1280,1280 2560,1280 "></polyline>
            </g>
          </svg></div>
        </div>
        <div class="anchor includedInExportedPdf align center" data-whiteboard-type="FluidImage" style="left: 500px; top: 400px; transform: matrix(2, 0, 0, 2, 1, -1);">
          <div class="canvasChild"><div class="content imageComponent interactive" role="img" style="height: 50px; width: 100px;">
            <div class="ms-Image"><img draggable="false" src="data:text/plain;base64,{{OnePixelPng}}" class="ms-Image-image" style="max-height: 50px; max-width: 100px;">
          </div></div></div>
        </div>
        <div class="anchor includedInExportedPdf align topLeft" data-whiteboard-type="InkGroup" style="left: 450px; top: 380px; transform: matrix(1, 0, 0, 1, 0, 0);">
          <div class="canvasChild"><svg class="inkGroup ink Mixed" viewBox="0 0 40 40" overflow="visible" width="40" height="40">
            <g class="inkStroke interactive" transform="matrix(0.0078125, 0, 0, 0.0078125, 0, 0)">
              <path d="M0,-256L1280,-256A256,256 0 0 0 1280,256L0,256A256,256 0 0 0 0,-256" fill="rgba(31,31,31,1)" opacity=""></path>
              <polyline class="inkHitTestOverlay" points="0,0 1280,0 "></polyline>
            </g>
            <g class="inkStroke interactive" transform="matrix(0.0078125, 0, 0, 0.0078125, 0, 0)">
              <path d="M0,0L1024,0L1024,2048L0,2048L0,0M1024,0L3000,0L3000,4096L3000,0L4000,0L4000,4096L4000,0L0,2048" fill="rgba(40,246,45,0.4)" opacity=""></path>
              <polyline class="inkHitTestOverlay" stroke-linecap="square" points="512,1024 3500,1024 "></polyline>
            </g>
          </svg></div>
        </div>
        <div class="anchor includedInExportedPdf align topLeft" data-whiteboard-type="StickyNote" style="left: 0px; top: 0px;"><div>A note</div></div>
        <div class="anchor includedInExportedPdf align topLeft" data-whiteboard-type="CommentThread" style="left: 0px; top: 0px;"></div>
        <div id="remoteInkPool" class="canvasChild anchor ink" aria-hidden="true">
          <svg class="inkGroup ink PenStroke" viewBox="0 0 10 10" width="10" height="10"><g class="inkStroke" transform="matrix(0.0078125, 0, 0, 0.0078125, 0, 0)"><path d="M0,0A64,64 0 0 0 0,128" fill="rgba(0,0,0,1)"></path><polyline class="inkHitTestOverlay" points="0,64 "></polyline></g></svg>
        </div>
        </div></div>
        </body></html>
        """;

    public static void Run()
    {
        var board = MicrosoftWhiteboardExport.Parse(Page, "Sample");
        var strokes = board.Strokes.ToArray();
        var image = board.Images.Single();

        Assert(board.Items.Count == 5 && strokes.Length == 4,
            "Four strokes and one picture, in page order; the pool of remote ink is not an object.");
        Assert(board.Items[2] is MicrosoftWhiteboardImage,
            "Items keep the page's drawing order, so the picture sits between the two ink groups.");
        Assert(board.Skipped.Count == 1 && board.Skipped["StickyNote"] == 1,
            "An object with no counterpart is counted by type. Comment threads are not objects.");

        // The group's view box starts at (-10, -20), so stroke (0, 0) is 10 and 20 pixels
        // into the group, scaled by 0.5 around the anchor at (100, 200) and moved by (4, 6).
        var line = strokes[0];
        Assert(line.Kind == PenKind.Pen && line.Argb == 0xFFE71224,
            "An opaque rgba fill is the pen's color.");
        AssertNear(109, line.Points[0].X, "The anchor's scale and translation apply to ink.");
        AssertNear(216, line.Points[0].Y, "The view box origin is subtracted before scaling.");
        AssertNear(119, line.Points[^1].X, "A stroke keeps its length through the transforms.");
        Assert(line.Widths.All(width => Math.Abs(width - 2) < 0.000001),
            "Arcs of one radius are one width: 2 × 256/128 pixels, halved by the anchor.");

        var tapered = strokes[1];
        Assert(tapered.Widths.Count == 3 &&
               Math.Abs(tapered.Widths[0] - 1) < 0.000001 &&
               Math.Abs(tapered.Widths[1] - 2) < 0.000001 &&
               Math.Abs(tapered.Widths[2] - 3) < 0.000001,
            "Each point takes the radius of the arc drawn around it.");
        var taperedInk = MicrosoftWhiteboardExport.ToInk(tapered, new PointD(0, 0), 0);
        AssertNear(2, taperedInk.Style.Thickness, "The thickness is the typical width.");
        AssertNear(0.5, taperedInk.Points[1].Pressure, "A point at the typical width has pressure 0.5.");
        AssertNear((1.5 - 0.25) / 1.5, taperedInk.Points[2].Pressure,
            "WPF draws thickness × (0.25 + 1.5 × pressure), so pressure recovers the width.");

        Assert(strokes[2].Kind == PenKind.Pen && strokes[3].Kind == PenKind.Highlighter,
            "In a Mixed group, a translucent fill is a highlighter.");
        AssertNear(32, strokes[3].Widths[0],
            "A highlighter's width is the upper quartile of its outline's vertical edges.");
        var highlight = MicrosoftWhiteboardExport.ToInk(strokes[3], new PointD(0, 0), 0);
        Assert(highlight.Style.Argb == 0xFF28F62D && Math.Abs(highlight.Style.Thickness - 16) < 0.000001,
            "A highlighter is drawn opaque at half the height, because its tip is twice as wide as tall.");

        Assert(image.ContentType == "image/png" && image.Extension == ".png",
            "Pictures are labelled text/plain, so their bytes decide the type.");
        Assert(image.Bounds == new RectD(401, 349, 200, 100),
            "A picture is centered on its anchor and scaled around it.");

        // Below existing content, aligned with its left edge.
        var existing = new RectD(-500, -200, 800, 600);
        var placed = board.Place(existing, firstZIndex: 7, Measure);
        AssertNear(-500, placed.Bounds.Left, "The import is aligned with the board's left edge.");
        AssertNear(400 + MicrosoftWhiteboardExport.Gap, placed.Bounds.Top, "The import goes below the board's content.");
        Assert(placed.Objects.Select(item => item.ZIndex).SequenceEqual(Enumerable.Range(7, 5)),
            "Objects are stacked in page order from the first free depth.");
        Assert(placed.Assets.Count == 1 && placed.Objects[2] is ImageBoardObject { AssetId: var assetId } &&
               assetId == placed.Assets[0].Id,
            "The picture refers to its asset.");
        var container = (ImageBoardObject)placed.Objects[2];
        var carried = placed.Objects.OfType<InkStrokeObject>().Where(stroke => stroke.ContainerId == container.Id).ToArray();
        Assert(carried.Length == 2 && placed.Objects.OfType<InkStrokeObject>().Take(2).All(stroke => stroke.ContainerId is null),
            "Strokes over the picture travel with it, and strokes elsewhere do not.");
        AssertNear(
            container.Bounds.Left - ((InkStrokeObject)placed.Objects[0]).Points[0].Position.X,
            image.Bounds.Left - line.Points[0].X,
            "Placement moves everything by the same offset.");

        var alone = board.Place(null, 0, Measure);
        AssertNear(0, alone.Bounds.Left, "On an empty board the import starts at the origin.");
        AssertNear(0, alone.Bounds.Top, "On an empty board the import starts at the origin.");

        RunArchive();
        RunObjects();
    }

    /// <summary>
    /// Every character is half its font size wide and every line 1.25 times it tall.
    /// </summary>
    private sealed class FixedMeasure : IBoardTextMeasure
    {
        public (double Width, double Height) Label(string text, string fontFamily, double fontSize, bool bold, bool italic)
        {
            var lines = text.Split('\n');
            return (lines.Max(line => line.Length) * fontSize / 2, lines.Length * fontSize * 1.25);
        }

        public double TextContainerHeight(string text, string languageId, double width) => 100;
    }

    private static readonly FixedMeasure Measure = new();

    /// <summary>
    /// The other objects a board can hold, reduced to the elements the importer reads,
    /// as found on a test board with one of each.
    /// </summary>
    private static readonly string ObjectsPage = $$"""
        <html><body><div id="canvasContent" class="contentOrigin">
        <div class="anchor align center" data-whiteboard-type="Shape" style="left: 500px; top: 400px; transform: matrix(-0.422618, 0.906308, -0.906308, -0.422618, 0, 0);">
          <div class="filledShape"><svg aria-label="Blockpfeil" class="shape" width="187" height="259">
            <g transform="translate(93.5 129.5)" fill="rgba(153, 201, 239, 1)" stroke="rgba(31, 31, 31, 1)" stroke-width="2pt">
              <path d="M-92,-36L0,-128L92,-36L46,-36L46,128L-46,128L-46,-36Z"></path></g></svg>
            <div class="textBoxContainer" style="transform: rotate(-90deg);"><div class="textbox shapeText interactive" style="font-size: 20px;">
              <div class="textBoxCore textArea"><div data-block="true"><span data-text="true">Arrow</span></div></div></div></div></div>
        </div>
        <div class="anchor align center" data-whiteboard-type="Shape" style="left: 100px; top: 100px;">
          <div class="filledShape"><svg class="shape" width="200" height="100">
            <g transform="translate(100 50)" fill="none" stroke="rgba(0, 0, 0, 0)" stroke-width="2pt">
              <path d="M-100,-50L100,-50L100,50L-100,50Z"></path></g></svg></div>
        </div>
        <div class="anchor align topLeft" data-whiteboard-type="Connector" style="left: 200px; top: 90px;">
          <svg width="100" height="30" class="shape connector"><g stroke-width="2.5" fill="none" stroke="rgba(31, 31, 31, 1)">
            <path d="M0 10L11 11L100 10"></path><path d="M-5 -9 L0 0 L5 -9" transform="translate(0,10), rotate(90)"></path></g></svg>
        </div>
        <div class="anchor align topLeft" data-whiteboard-type="Note" style="left: 1000px; top: 0px;">
          <div class="textBoxBackground noteVisualUpdate softBlueGradient"><div class="textbox stickyNote" style="width: 304px; height: 265px; font-size: 32px;">
            <div class="textBoxCore textArea" style="font-weight: 400;"><div data-block="true"><span data-text="true">First line</span></div><div data-block="true"><span data-text="true">Second &amp; last</span></div></div></div></div>
          <div class="ReactionTagContainer"><button type="button" class="ms-Button ReactionPill"></button></div>
        </div>
        <div class="anchor align topLeft" data-whiteboard-type="PlainText" style="left: 0px; top: 600px;">
          <div style="width: 400px; display: flex; justify-content: center;"><div class="textbox plainText" style="max-width: 400px; font-size: 20px;">
            <div class="textBoxCore textArea" style="color: rgb(193, 0, 81); font-family: &quot;Segoe Print&quot;, &quot;ink free&quot;; font-weight: 700; font-style: italic;">
              <div data-block="true"><span data-text="true">one two three four five six seven eight</span></div></div></div></div>
        </div>
        <div class="anchor align topLeft" data-whiteboard-type="GridList" style="left: 0px; top: 1000px;">
          <div class="listTitleContainer"><div class="textbox listTitle"><div data-block="true"><span data-text="true">Grid</span></div></div></div>
          <div class="listChildren" style="grid-template-columns: repeat(2, auto);">
            <div class="semanticDraggable listChild" data-whiteboard-type="Note"><div class="textBoxBackground softRedGradient"><div class="textbox stickyNote" style="width: 304px; height: 265px; font-size: 32px;"><div data-block="true"><span data-text="true">A</span></div></div></div></div>
            <div class="semanticDraggable listChild" data-whiteboard-type="Note"><div class="textBoxBackground softRedGradient"><div class="textbox stickyNote" style="width: 304px; height: 265px; font-size: 32px;"><div data-block="true"><br data-text="true"></div></div></div></div>
            <div class="semanticDraggable listChild" data-whiteboard-type="Note"><div class="textBoxBackground softRedGradient"><div class="textbox stickyNote" style="width: 304px; height: 265px; font-size: 32px;"><div data-block="true"><span data-text="true">C</span></div></div></div></div>
          </div>
        </div>
        <div class="anchor align topLeft" data-whiteboard-type="Hyperlink" style="left: 2000px; top: 0px;">
          <div class="previewCardTitleContainer" style="width: 320px;"><div class="previewCardTitle"><a href="https://www.sqlbi.com/">Home - SQLBI</a></div><div class="previewCardDescription">Business *Intelligence*</div></div>
        </div>
        <div class="anchor align topLeft" data-whiteboard-type="CommentThread" style="left: 50px; top: 50px;">
          <div class="commentHintContainer" role="button" aria-label="Comment hint: 1"></div>
        </div>
        <div class="anchor align center" data-whiteboard-type="FluidImage" style="left: 3000px; top: 3000px; transform: matrix(0, 1, -1, 0, 0, 0);">
          <div class="content imageComponent" style="height: 50px; width: 100px;"><img src="data:text/plain;base64,{{OnePixelPng}}"></div>
        </div>
        <div class="anchor align topLeft" data-whiteboard-type="InkGroup" style="left: 0px; top: 0px;">
          <svg class="inkGroup ink PenStroke" viewBox="0 0 100 10" width="100" height="10">
            <defs><pattern id="-72"><use href="#galaxy-pen"></use></pattern></defs>
            <g class="inkStroke" transform="matrix(0.0078125, 0, 0, 0.0078125, 0, 0)">
              <path d="M0,-256L2560,-256A256,256 0 0 0 2560,256L0,256A256,256 0 0 0 0,-256" fill="url(#-72)"></path>
              <polyline class="inkHitTestOverlay" points="0,0 2560,0 "></polyline></g></svg>
        </div>
        <div class="anchor align topLeft" data-whiteboard-type="Timer" style="left: 0px; top: 0px;"></div>
        </div></body></html>
        """;

    private const string ObjectsComments = """
        {"commentThreads": [{"id": "1", "comments": [
          {"author": {"name": "Ana", "email": "ana@example.com"}, "body": "Why *this*?\n", "displayDate": "May 1"},
          {"author": {"name": "Bo"}, "body": "Because.", "displayDate": "May 2"}]}]}
        """;

    private static void RunObjects()
    {
        var board = MicrosoftWhiteboardExport.Parse(ObjectsPage, "Objects", ObjectsComments);
        Assert(board.Skipped.Count == 2 && board.Skipped["Timer"] == 1 && board.Skipped["Reactions on notes"] == 1,
            "An unknown object is counted by its type, and reactions on notes by what they are.");

        var arrow = (MicrosoftWhiteboardShape)board.Items[0];
        Assert(arrow.Kind == ShapeKind.BlockArrow && arrow.Text == "Arrow",
            "The kind comes from the outline, not from the name, which is in the board's language.");
        AssertNear(25, arrow.AngleDegrees,
            "A shape stored turned by a quarter, with its text turned back, is the box turned by the difference.");
        Assert(Math.Abs(arrow.Width - 259) < 0.01 && Math.Abs(arrow.Height - 187) < 0.01,
            "The quarter turn swaps the box's sides.");
        Assert(arrow.Font.Bold && Math.Abs(arrow.Font.Size - 20) < 0.01,
            "Shape text is bold unless the page says otherwise.");
        AssertNear(8.0 / 3, arrow.Thickness, "Points are 4/3 of a pixel.");

        var frame = (MicrosoftWhiteboardShape)board.Items[1];
        Assert(frame.Kind == ShapeKind.RoundedRectangle && frame.FillArgb is null && frame.OutlineArgb == 0,
            "No fill and a transparent outline stay without paint.");

        var note = (MicrosoftWhiteboardShape)board.Items[3];
        Assert(note.FillArgb == 0xFF99C9EF && note.Text == "First line\nSecond & last" && !note.Font.Bold,
            "A note is a shape in its color, one line per block, entities decoded.");
        Assert(note.Center == new PointD(1152, 152.5) && note.Width == 304 && note.Height == 305,
            "A note covers its text and the author bar above it.");

        var label = (MicrosoftWhiteboardLabel)board.Items[4];
        Assert(label.Font is { Family: "Segoe Print", Bold: true, Italic: true, Argb: 0xFFC10051 } &&
               label.WrapWidth == 368 && label.CenteredWidth == 400,
            "A text box keeps its font, its wrap width inside the padding, and the box that centers it.");

        var grid = board.Items.Skip(5).Take(5).ToArray();
        Assert(grid[0] is MicrosoftWhiteboardShape { FillArgb: 0xFFFFFFFF, Width: 658, Height: 722 } &&
               grid[1] is MicrosoftWhiteboardLabel { Text: "Grid" } &&
               grid[2] is MicrosoftWhiteboardShape { Text: "A", Center: { X: 169, Y: 1233.5 } } &&
               grid[3] is MicrosoftWhiteboardShape { Text: "", Center.X: 489 } &&
               grid[4] is MicrosoftWhiteboardShape { Text: "C", Center: { X: 169, Y: 1553.5 } },
            "A grid is a panel, its title, and its notes in rows of the declared width.");

        Assert(board.Items[10] is MicrosoftWhiteboardTextContainer { Title: "Link" } link &&
               link.Markdown == "[Home - SQLBI](https://www.sqlbi.com/)\n\nBusiness \\*Intelligence\\*",
            "A link card becomes Markdown, with the page's text escaped.");
        Assert(board.Items[11] is MicrosoftWhiteboardTextContainer { Title: "Comment" } thread &&
               thread.Markdown.Contains("**Ana** · May 1\n\nWhy \\*this\\*?", StringComparison.Ordinal) &&
               thread.Markdown.Contains("**Bo** · May 2", StringComparison.Ordinal) &&
               !thread.Markdown.Contains("example.com", StringComparison.Ordinal),
            "A comment pin finds its thread by number. Authors are named and their addresses left out.");

        Assert(board.Items[12] is MicrosoftWhiteboardImage { ContentType: DroppedFileImport.SvgContentType } turned &&
               Math.Abs(turned.Bounds.Width - 50) < 0.01 && Math.Abs(turned.Bounds.Height - 100) < 0.01 &&
               Encoding.UTF8.GetString(turned.Bytes).Contains("rotate(90 25 50)", StringComparison.Ordinal),
            "A turned picture becomes an SVG that turns it, in the box the turned picture covers.");
        Assert(board.Items[13] is MicrosoftWhiteboardStroke { Argb: 0xFF312E81 },
            "Galaxy ink is drawn in the color that stands for it.");

        var placed = board.Place(null, 0, Measure);
        var connector = placed.Objects.OfType<ConnectorBoardObject>().Single();
        var rectangle = placed.Objects.OfType<ShapeBoardObject>().Single(shape => shape.FillArgb is null);
        Assert(connector.Kind == ConnectorKind.Arrow && connector.EndAnchor?.ObjectId == rectangle.Id && connector.StartAnchor is null,
            "A head drawn at the start is turned round to the end, and the end on a shape is bound to it.");

        var wrapped = placed.Objects.OfType<FreeTextBoardObject>().Single(text => text.FontFamily == "Segoe Print");
        Assert(wrapped.Text == "one two three four five six seven\neight",
            "A label keeps the lines the text box wrapped it into.");
        var panel = placed.Objects.OfType<ShapeBoardObject>().Single(shape => shape.FillArgb == 0xFFFFFFFF);
        AssertNear((400 - 330) / 2.0, wrapped.Bounds.Left - panel.Bounds.Left,
            "Centered text sits in the middle of its box, whose left edge is where the panel's is.");

        var comment = placed.Objects.OfType<TextBoardObject>().Single(text => text.Title == "Comment");
        Assert(comment.Bounds.Left > placed.Objects.Where(item => item != comment).Max(item => item.Bounds.Right),
            "A comment goes in a column to the right of everything else.");
    }

    private static void RunArchive()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wb-mswb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var export = Path.Combine(folder, "Board 1.zip");
            using (var archive = ZipFile.Open(export, ZipArchiveMode.Create))
            {
                Write(archive, "Board 1.html", Page);
                Write(archive, "Board 1-comments.json", "{\"commentThreads\": []}");
            }

            var other = Path.Combine(folder, "Other.zip");
            using (var archive = ZipFile.Open(other, ZipArchiveMode.Create))
            {
                Write(archive, "index.html", Page);
            }

            Assert(MicrosoftWhiteboardExport.IsExport(export) &&
                   DroppedFileImport.Classify(export) == DroppedFileKind.MicrosoftWhiteboard,
                "A ZIP with one page and its comments file is an export.");
            Assert(!MicrosoftWhiteboardExport.IsExport(other) &&
                   DroppedFileImport.Classify(other) == DroppedFileKind.Unsupported,
                "Any other ZIP is refused rather than dropped as text.");
            Assert(DroppedFileImport.Classify(Path.Combine(folder, "Missing.zip")) == DroppedFileKind.Unsupported,
                "A ZIP that is not there is not an export.");

            var read = MicrosoftWhiteboardExport.Read(export);
            Assert(read.Name == "Board 1" && read.Items.Count == 5,
                "Reading the archive gives the page's name and content.");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static void Write(ZipArchive archive, string name, string content)
    {
        using var stream = archive.CreateEntry(name).Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        stream.Write(bytes);
    }

    private static void AssertNear(double expected, double actual, string message) =>
        Assert(Math.Abs(expected - actual) < 0.0001, $"{message} Expected {expected}, got {actual}.");

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
