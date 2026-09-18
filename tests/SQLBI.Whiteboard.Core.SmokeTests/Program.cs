using System.IO.Compression;
using System.Text;
using SQLBI.Whiteboard.Core.Commands;
using SQLBI.Whiteboard.Core.Export;
using SQLBI.Whiteboard.Core.Geometry;
using SQLBI.Whiteboard.Core.Import;
using SQLBI.Whiteboard.Core.Model;
using SQLBI.Whiteboard.Core.Persistence;
using SQLBI.Whiteboard.Core.Settings;
using SQLBI.Whiteboard.Core.Updates;
using SQLBI.Whiteboard.Core.Viewport;
using SQLBI.Whiteboard.Dax;
using SQLBI.Whiteboard.Export;
using SQLBI.Whiteboard.Kql;
using SQLBI.Whiteboard.SqlServer;

var camera = new Camera2D();
camera.Resize(1000, 800);
var anchor = new PointD(720, 310);
var worldBeforeZoom = camera.ScreenToWorld(anchor);
camera.ZoomAt(anchor, 2.5);
var worldAfterZoom = camera.ScreenToWorld(anchor);
AssertNear(worldBeforeZoom.X, worldAfterZoom.X, "Zoom must retain the X anchor.");
AssertNear(worldBeforeZoom.Y, worldAfterZoom.Y, "Zoom must retain the Y anchor.");

var framedBounds = new RectD(100, 200, 640, 480);
camera.Frame(framedBounds, 0.05);
AssertNear(framedBounds.Center.X, camera.Center.X, "Framing must center the object on X.");
AssertNear(framedBounds.Center.Y, camera.Center.Y, "Framing must center the object on Y.");
AssertNear(1.40625, camera.Zoom, "Framing must choose the largest fitting zoom.");
var framedTopLeft = camera.WorldToScreen(
    new PointD(framedBounds.Left, framedBounds.Top));
Assert(framedTopLeft.X >= 49.999999, "Framing must preserve the horizontal margin.");
Assert(framedTopLeft.Y >= 49.999999, "Framing must preserve the vertical margin.");

// The grid coarsens in steps rather than continuously, so a zoom is visible as
// the lines spreading and then snapping wider apart.
{
    AssertNear(40, GridGeometry.SpacingFor(1), "At zoom 1 the grid is the base 40 board pixels.");
    AssertNear(40, GridGeometry.SpacingFor(16), "Zoomed all the way in the grid stays at 40: it is already wide enough on screen.");
    AssertNear(160, GridGeometry.SpacingFor(0.25), "40 at quarter zoom is 10 screen pixels, which is under the floor, so the spacing steps by four.");
    AssertNear(640, GridGeometry.SpacingFor(0.05), "At the minimum zoom the spacing steps twice rather than crowding.");

    // Exactly at the floor the spacing holds: the step is for lines that would be
    // closer than it, not for lines that reach it.
    var threshold = GridGeometry.MinimumScreenSpacing / GridGeometry.BaseSpacing;
    AssertNear(40, GridGeometry.SpacingFor(threshold), "12 screen pixels apart is close enough, so the spacing holds.");
    AssertNear(
        160,
        GridGeometry.SpacingFor(Math.BitDecrement(threshold)),
        "A hair under the floor is what steps the spacing, so the comparison cannot drift to the wrong side.");
    AssertNear(40, GridGeometry.SpacingFor(0), "A zoom that cannot be drawn with falls back to the base spacing.");
    AssertNear(40, GridGeometry.SpacingFor(double.NaN), "NaN falls back rather than looping forever.");

    var visible = new RectD(-50, 30, 180, 90);
    Assert(
        GridGeometry.VerticalLines(visible, 40).SequenceEqual([-40d, 0, 40, 80, 120]),
        "Vertical lines cover the visible rectangle from its first multiple of the spacing to its last.");
    Assert(
        GridGeometry.HorizontalLines(visible, 40).SequenceEqual([40d, 80, 120]),
        "Horizontal lines start at the first multiple inside the rectangle, not at its edge.");
    Assert(
        GridGeometry.VerticalLines(new RectD(0, 0, 0, 0), 40).SequenceEqual([0d]),
        "A rectangle standing on a line still carries that line.");
    Assert(
        !GridGeometry.VerticalLines(new RectD(1, 0, 10, 10), 40).Any(),
        "A rectangle between two lines carries none of them.");
    Assert(
        !GridGeometry.VerticalLines(visible, 0).Any(),
        "A spacing of zero yields nothing rather than never returning.");
}

var lineAnchor = new PointD(100, 100);
Assert(
    StraightLineSnap.DetectDirection(lineAnchor, new PointD(200, 120)) ==
    StraightLineDirection.Horizontal,
    "A mostly horizontal constrained stroke should snap horizontally.");
Assert(
    StraightLineSnap.DetectDirection(lineAnchor, new PointD(85, 200)) ==
    StraightLineDirection.Vertical,
    "A mostly vertical constrained stroke should snap vertically in either direction.");
Assert(
    StraightLineSnap.DetectDirection(lineAnchor, new PointD(200, 130)) ==
    StraightLineDirection.Horizontal &&
    StraightLineSnap.DetectDirection(lineAnchor, new PointD(130, 200)) ==
    StraightLineDirection.Vertical,
    "A diagonal constrained stroke should take the nearer axis, never fall back to free ink.");
Assert(
    StraightLineSnap.DetectDirection(lineAnchor, new PointD(160, 160)) ==
    StraightLineDirection.Horizontal,
    "An exactly diagonal constrained stroke should settle on one axis rather than none.");
Assert(
    StraightLineSnap.DetectDirection(lineAnchor, new PointD(105, 102)) ==
    StraightLineDirection.None &&
    StraightLineSnap.DetectDirection(lineAnchor, new PointD(115, 108)) ==
    StraightLineDirection.None,
    "Straight-line direction should wait for enough movement to establish intent.");
Assert(
    StraightLineSnap.DetectDirection(lineAnchor, new PointD(100, 130)) ==
    StraightLineDirection.Vertical,
    "Once the pen has clearly turned, the axis should follow the turn.");
Assert(
    StraightLineSnap.Apply(
        new PointD(180, 116),
        lineAnchor,
        StraightLineDirection.None) == new PointD(180, 116),
    "Releasing the constraint should leave the remaining points exactly where the pen went.");
Assert(
    StraightLineSnap.Apply(
        new PointD(300, 400),
        lineAnchor,
        StraightLineDirection.Horizontal) == new PointD(300, 100),
    "A settled axis keeps its line however far off it the hand drifts.");
Assert(
    StraightLineSnap.Apply(
        new PointD(180, 116),
        lineAnchor,
        StraightLineDirection.Horizontal) == new PointD(180, 100) &&
    StraightLineSnap.Apply(
        new PointD(88, 210),
        lineAnchor,
        StraightLineDirection.Vertical) == new PointD(100, 210),
    "Snapping should preserve travel along the chosen axis and remove only off-axis movement.");

var originalLiveViewBounds = new RectD(100, 50, 400, 200);
var reconnectedLiveViewBounds = originalLiveViewBounds.WithCenteredAspectRatio(9d / 16d);
AssertNear(
    originalLiveViewBounds.Center.X,
    reconnectedLiveViewBounds.Center.X,
    "Changing a LiveView aspect ratio must preserve its center on X.");
AssertNear(
    originalLiveViewBounds.Center.Y,
    reconnectedLiveViewBounds.Center.Y,
    "Changing a LiveView aspect ratio must preserve its center on Y.");
AssertNear(
    9d / 16d,
    reconnectedLiveViewBounds.Width / reconnectedLiveViewBounds.Height,
    "Reconnected LiveView bounds must match the new source aspect ratio.");
AssertNear(
    originalLiveViewBounds.Width * originalLiveViewBounds.Height,
    reconnectedLiveViewBounds.Width * reconnectedLiveViewBounds.Height,
    "Changing a LiveView aspect ratio must preserve its visual area.");

Assert(
    DroppedFileImport.Classify(@"C:\board\shot.PNG") == DroppedFileKind.Image &&
    DroppedFileImport.Classify("notes.txt") == DroppedFileKind.Text &&
    DroppedFileImport.Classify("measure.dax") == DroppedFileKind.Text &&
    DroppedFileImport.Classify("query.sql") == DroppedFileKind.Text &&
    DroppedFileImport.Classify("alerts.kql") == DroppedFileKind.Text &&
    DroppedFileImport.Classify("lesson.wimport") == DroppedFileKind.Import &&
    DroppedFileImport.Classify("board.wboard") == DroppedFileKind.Unsupported &&
    DroppedFileImport.Classify("notes.md") == DroppedFileKind.Text,
    "Drop import should accept images, text, .wimport, and unrecognized text extensions.");
Assert(
    DroppedFileImport.CanImportAny(["skip.bin", "photo.jpg"]),
    "A mixed drop should be accepted when any file can be imported.");
Assert(
    DroppedFileImport.CanImport("notes.md") &&
    !DroppedFileImport.HasRecognizedLanguageExtension("notes.md"),
    "Unrecognized extensions should still drop as text so language order can choose.");
Assert(
    !DroppedFileImport.CanImport("board.wboard"),
    "A .wboard is opened, not dropped as a snippet.");
Assert(
    DroppedFileImport.LanguageIdFor("measure.dax") == TextLanguageIds.Dax &&
    DroppedFileImport.LanguageIdFor("query.sql") == TextLanguageIds.SqlServer &&
    DroppedFileImport.LanguageIdFor("alerts.kql") == TextLanguageIds.Kql &&
    DroppedFileImport.LanguageIdFor("notes.txt") == TextLanguageIds.Plain,
    "Dropped text files should pick a language from the extension.");
Assert(
    DroppedFileImport.HasRecognizedLanguageExtension("measure.dax") &&
    DroppedFileImport.HasRecognizedLanguageExtension("notes.txt") &&
    !DroppedFileImport.HasRecognizedLanguageExtension("notes.md"),
    "Recognized language extensions should skip the paste heuristic.");
Assert(
    DroppedFileImport.LooksLikeText("DEFINE MEASURE Sales[X] = 1"u8.ToArray()) &&
    !DroppedFileImport.LooksLikeText([0x4D, 0x5A, 0x00, 0x00]),
    "Text drops should reject files with a NUL in the header.");
Assert(
    DroppedFileImport.Classify("logo.svg") == DroppedFileKind.Image &&
    DroppedFileImport.Classify(@"C:\art\LOGO.SVG") == DroppedFileKind.Image &&
    ImportCatalog.Default.IsImageExtension(".svg"),
    "An SVG is an image everywhere an image is accepted, not a text snippet.");
Assert(
    DroppedFileImport.LooksLikeSvg("<svg xmlns='x' width='4'><rect/></svg>") &&
    DroppedFileImport.LooksLikeSvg("\uFEFF  \n<?xml version=\"1.0\"?><svg><g/></svg>") &&
    DroppedFileImport.LooksLikeSvg("<!-- note --><!DOCTYPE svg PUBLIC \"x\" \"y\"><svg/>") &&
    DroppedFileImport.LooksLikeSvg("<svg:svg xmlns:svg='x'><svg:g/></svg:svg>") &&
    DroppedFileImport.LooksLikeSvg("<s:svg xmlns:s='x'/>"),
    "Pasted SVG markup should survive a BOM, a prologue, and any namespace prefix.");
Assert(
    !DroppedFileImport.LooksLikeSvg("SELECT * FROM <svg> -- </svg>") &&
    !DroppedFileImport.LooksLikeSvg("<svgx><rect/></svgx>") &&
    !DroppedFileImport.LooksLikeSvg("<html><body><svg><rect/></svg></body></html>") &&
    !DroppedFileImport.LooksLikeSvg("<?xml version=\"1.0\"?>") &&
    !DroppedFileImport.LooksLikeSvg((string?)null),
    "Only a document whose root element is svg is a picture; one that contains svg is not.");
Assert(
    DroppedFileImport.LooksLikeSvg("<svg><g/></svg>"u8.ToArray()) &&
    !DroppedFileImport.LooksLikeSvg([0x89, 0x50, 0x4E, 0x47]) &&
    !DroppedFileImport.LooksLikeSvg([0xFF, 0xD8, 0xFF, 0xE0]) &&
    !DroppedFileImport.LooksLikeSvg((byte[]?)null) &&
    !DroppedFileImport.LooksLikeSvg([]),
    "Asset bytes should choose the SVG decoder without relying on a stored content type.");
Assert(
    !DroppedFileImport.LooksLikeSvg([0x3C, 0xFF, 0xFE, 0x3C]),
    "Bytes that open with '<' but are not UTF-8 text are not SVG.");

var svgImport = ImportDocument.Parse(
    """
    ## Diagram
    ![star](./art/star.svg)

    ## Linked
    [logo](./art/logo.svg)
    """);
Assert(
    svgImport.Items is
    [
        { Kind: ImportItemKind.Image, SourcePath: "./art/star.svg" },
        { Kind: ImportItemKind.Image, SourcePath: "./art/logo.svg" },
    ],
    "A .wimport should build image containers from SVG, both embedded and linked.");

var spacedNameImport = ImportDocument.Parse(
    """
    ## Diagram
    ![short alt](./test - Copy.svg)

    ## Angle brackets
    ![star](<./art/my star.svg>)

    ## Linked with a title
    [logo](./art/my logo.svg "Contoso")
    """);
Assert(
    spacedNameImport.Items is
    [
        { Kind: ImportItemKind.Image, SourcePath: "./test - Copy.svg" },
        { Kind: ImportItemKind.Image, SourcePath: "./art/my star.svg" },
        { Kind: ImportItemKind.Image, SourcePath: "./art/my logo.svg" },
    ],
    "A file name with spaces is the path, bare or in angle brackets, and a title is not part of it.");

var parsedImport = ImportDocument.Parse(
    """
    # Contoso workshop

    This intro is ignored.

    ## Sales model
    ![Sales model](./images/model.png)

    ## Talking points
    - Grain is daily

    ---

    ## Total Sales
    ```dax
    Total Sales := SUM(Sales[Amount])
    ```

    ## Warehouse query
    [top customers](./sql/top-customers.sql)

    ## Unknown fence
    ```python
    print("hi")
    ```
    """);
Assert(
    parsedImport.Title == "Contoso workshop" &&
    parsedImport.Items.Count == 5,
    "A .wimport file should yield one item per ## heading.");
Assert(
    parsedImport.Items[0] is
    {
        Kind: ImportItemKind.Image,
        SourcePath: "./images/model.png",
        Title: "Sales model",
        StartNewRow: false,
    },
    "A Markdown image should become an image item.");
Assert(
    parsedImport.Items[1] is { Kind: ImportItemKind.Text, LanguageId: TextLanguageIds.Plain } &&
    parsedImport.Items[1].Text!.Contains("Grain is daily", StringComparison.Ordinal),
    "Notes should import as plain Markdown source.");
Assert(
    parsedImport.Items[2] is
    {
        Kind: ImportItemKind.Text,
        LanguageId: TextLanguageIds.Dax,
        StartNewRow: true,
        Text: "Total Sales := SUM(Sales[Amount])",
    },
    "A thematic break should force the next container onto a new row.");
Assert(
    parsedImport.Items[3] is
    {
        Kind: ImportItemKind.Text,
        LanguageId: TextLanguageIds.SqlServer,
        SourcePath: "./sql/top-customers.sql",
    },
    "A link to a .sql file should become a SQL text item.");
Assert(
    parsedImport.Items[4] is { LanguageId: TextLanguageIds.Plain } &&
    parsedImport.Items[4].Text!.Contains("print", StringComparison.Ordinal),
    "An unknown fence should fall through to plain text.");

var kqlFromFence = ImportDocument.Parse(
    """
    ## Failed logons
    ```kql
    SecurityEvent | where EventID == 4625
    ```

    ## Alerts
    [alerts](./queries/alerts.kql)
    """);
Assert(
    kqlFromFence.Items is
    [
        { LanguageId: TextLanguageIds.Kql, Text: "SecurityEvent | where EventID == 4625" },
        { LanguageId: TextLanguageIds.Kql, SourcePath: "./queries/alerts.kql" },
    ],
    "A kql fence and a .kql link should both import as KQL.");
Assert(
    ImportCatalog.Default.LanguageForFence("kusto")?.Id == TextLanguageIds.Kql,
    "Kusto is the other name the same fence is written under.");

var pythonCatalog = ImportCatalog.Default.WithLanguage(
    new ImportLanguage
    {
        Id = "python",
        FenceTags = ["python", "py"],
        Extensions = [".py"],
    });
var pythonFromFence = ImportDocument.Parse(
    """
    ## Script
    ```python
    print("hi")
    ```
    """,
    pythonCatalog);
var pythonFromLink = ImportDocument.Parse(
    """
    ## Script
    [script](./util.py)
    """,
    pythonCatalog);
Assert(
    pythonFromFence.Items is [{ LanguageId: "python", Text: "print(\"hi\")" }] &&
    pythonFromLink.Items is [{ LanguageId: "python", SourcePath: "./util.py" }],
    "A new language row should be picked up by fence and by link without matcher changes.");

var importFolder = Path.Combine(Path.GetTempPath(), "wimport-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(importFolder);
File.WriteAllText(Path.Combine(importFolder, "ok.dax"), "x := 1");
var resolvedImport = ImportDocument.Parse(
    """
    ## Present
    [ok](./ok.dax)

    ## Missing
    ![](./missing.png)
    """).Resolve(importFolder);
Assert(
    resolvedImport.Items is [{ LanguageId: TextLanguageIds.Dax, Text: "x := 1" }] &&
    resolvedImport.MissingFiles.Count == 1 &&
    resolvedImport.MissingFiles[0].EndsWith("missing.png", StringComparison.OrdinalIgnoreCase),
    "Resolve should load linked files and list missing paths without aborting.");
Directory.Delete(importFolder, recursive: true);

var placed = ImportLayout.Place(
    [
        (400, 200, false),
        (400, 180, false),
        (400, 200, true),
        (ImportLayout.MaxRowWidth, 100, false),
        (500, 100, false),
    ],
    new PointD(10, 20));
Assert(
    placed[0].X == 10 && placed[0].Y == 20 &&
    placed[1].X == 10 + 400 + ImportLayout.Gap && placed[1].Y == 20 &&
    placed[2].X == 10 && placed[2].Y > placed[0].Bottom &&
    placed[4].Y > placed[3].Y,
    "Flow layout should pack left to right, honor a forced row, and wrap on max width.");
var spacedImport = ImportLayout.Place(
    [(400, 200, false), (400, 300, false), (400, 200, true), (2400, 100, false), (500, 100, false)],
    new PointD(10, 20),
    horizontalSpacing: 80,
    verticalSpacing: 120);
Assert(
    spacedImport.SequenceEqual(
    [
        new RectD(10, 20, 400, 200),
        new RectD(490, 20, 400, 300),
        new RectD(10, 440, 400, 200),
        new RectD(10, 760, 2400, 100),
        new RectD(10, 980, 500, 100),
    ]),
    "Custom spacing should separate columns horizontally and forced or wrapped rows vertically below the tallest item.");
var touchingImport = ImportLayout.Place(
    [(1200, 200, false), (1200, 300, false), (400, 100, false), (200, 100, true)],
    new PointD(10, 20),
    horizontalSpacing: 0,
    verticalSpacing: 0);
Assert(
    touchingImport.SequenceEqual(
    [
        new RectD(10, 20, 1200, 200),
        new RectD(1210, 20, 1200, 300),
        new RectD(10, 320, 400, 100),
        new RectD(10, 420, 200, 100),
    ]),
    "Zero spacing should allow touching edges and an exactly full row without overlapping containers.");
Assert(
    ImportLayout.Place(
        [(1200, 200, false), (1200, 300, false)],
        new PointD(10, 20),
        horizontalSpacing: 1,
        verticalSpacing: 7)[1] == new RectD(10, 227, 1200, 300),
    "The configured horizontal gap must count toward the wrap threshold.");
Assert(
    ImportLayout.ImageSize(1800, 1400) is { Width: 900, Height: 700 },
    "Imported images should use the same 900 by 700 cap as a dropped image.");
Assert(
    ImportLayout.VectorImageSize(24, 24) is { Width: 240, Height: 240 } &&
    ImportLayout.VectorImageSize(1800, 1400) is { Width: 900, Height: 700 } &&
    ImportLayout.VectorImageSize(100, 20) is { Width: 240, Height: 48 } &&
    ImportLayout.VectorImageSize(400, 100) is { Width: 400, Height: 100 },
    "A vector should be grown to a legible edge, keep its aspect, and keep the 900 by 700 cap.");

var document = new BoardDocument();
Assert(document.ContentBounds is null, "An empty document should not have content bounds.");
var history = new CommandHistory();
var stroke = InkStrokeObject.Create(
    [
        new InkPoint(new PointD(-10, 4), 0.25f, 1),
        new InkPoint(new PointD(30, 14), 0.8f, 2),
    ],
    new PenStyle(0xFF2563EB, 6),
    document.NextZIndex);

history.Execute(new AddObjectCommand(stroke), document);
Assert(document.Objects.Count == 1, "Add command should add one object.");
history.Undo(document);
Assert(document.Objects.Count == 0, "Undo should remove the object.");
history.Redo(document);
Assert(document.Objects.Count == 1, "Redo should restore the object.");
Assert(stroke.HitTestWithin(new PointD(10, 9), 4), "Stroke hit testing should find a nearby point.");
Assert(document.ContentBounds == stroke.Bounds, "A single object should define the content bounds.");

var boundsDocument = new BoardDocument();
boundsDocument.AddObject(new ImageBoardObject(
    Guid.NewGuid(),
    boundsDocument.NextZIndex,
    new RectD(-100, -50, 50, 60),
    "bounds-1"));
boundsDocument.AddObject(new ImageBoardObject(
    Guid.NewGuid(),
    boundsDocument.NextZIndex,
    new RectD(200, 100, 100, 100),
    "bounds-2"));
Assert(
    boundsDocument.ContentBounds == new RectD(-100, -50, 400, 250),
    "Content bounds should enclose every retained board object.");

var containerDocument = new BoardDocument();
var firstContainer = new ImageBoardObject(
    Guid.NewGuid(),
    containerDocument.NextZIndex,
    new RectD(100, 100, 200, 100),
    "container-1");
containerDocument.AddObject(firstContainer);
var secondContainer = new ImageBoardObject(
    Guid.NewGuid(),
    containerDocument.NextZIndex,
    new RectD(250, 100, 200, 100),
    "container-2");
containerDocument.AddObject(secondContainer);

var singleContainerStroke = InkStrokeObject.Create(
    [
        new InkPoint(new PointD(40, 125), 0.5f, 1),
        new InkPoint(new PointD(140, 125), 0.5f, 2),
    ],
    new PenStyle(0xFF000000, 4),
    containerDocument.NextZIndex);
Assert(
    containerDocument.FindSingleTouchedContainer(singleContainerStroke)?.Id == firstContainer.Id,
    "A stroke touching exactly one container should link to it.");
var ambiguousStroke = InkStrokeObject.Create(
    [
        new InkPoint(new PointD(120, 150), 0.5f, 1),
        new InkPoint(new PointD(300, 150), 0.5f, 2),
    ],
    new PenStyle(0xFF000000, 4),
    containerDocument.NextZIndex);
Assert(
    containerDocument.FindSingleTouchedContainer(ambiguousStroke) is null,
    "A stroke touching multiple containers must remain unlinked.");

var liveViewContainer = new LiveViewBoardObject(
    Guid.NewGuid(),
    containerDocument.NextZIndex,
    new RectD(500, 100, 320, 180),
    new LiveViewSourceConfiguration(LiveViewSourceKind.Display, "Wacom display", "DISPLAY4"));
containerDocument.AddObject(liveViewContainer);
Assert(
    containerDocument.HitTestTopContainer(new PointD(600, 150))?.Id == liveViewContainer.Id,
    "LiveView should participate in generic container hit testing.");
var liveViewStroke = InkStrokeObject.Create(
    [new InkPoint(new PointD(520, 120), 0.5f, 1)],
    PenStyle.Default,
    containerDocument.NextZIndex);
Assert(
    containerDocument.FindSingleTouchedContainer(liveViewStroke)?.Id == liveViewContainer.Id,
    "A stroke touching one LiveView should link to it like any other container.");
var textContainer = new TextBoardObject(
    Guid.NewGuid(),
    containerDocument.NextZIndex,
    new RectD(900, 100, 420, 220),
    "Text",
    "A plain text container",
    1.5);
containerDocument.AddObject(textContainer);
Assert(
    containerDocument.HitTestTopContainer(new PointD(1000, 150))?.Id == textContainer.Id,
    "Text should participate in generic container hit testing.");

var linkedStroke = singleContainerStroke with { ContainerId = firstContainer.Id };
containerDocument.AddObject(linkedStroke);
var movedContainer = firstContainer with
{
    Bounds = firstContainer.Bounds.Translate(new PointD(50, 25)),
};
var movedStroke = linkedStroke.TransformWithContainer(
    firstContainer.Bounds,
    movedContainer.Bounds);
var containerHistory = new CommandHistory();
containerHistory.Execute(
    new ReplaceObjectsCommand(
        [firstContainer, linkedStroke],
        [movedContainer, movedStroke]),
    containerDocument);
AssertNear(
    linkedStroke.Points[0].Position.X + 50,
    movedStroke.Points[0].Position.X,
    "Moving a container must translate linked stroke points.");
containerHistory.Undo(containerDocument);
Assert(
    containerDocument.Objects.OfType<InkStrokeObject>().Single().Points[0] == linkedStroke.Points[0],
    "Undo must restore a container and its linked strokes together.");

var resizedContainer = firstContainer with
{
    Bounds = firstContainer.Bounds.WithSize(400, 200),
};
var resizedStroke = linkedStroke.TransformWithContainer(
    firstContainer.Bounds,
    resizedContainer.Bounds);
AssertNear(
    180,
    resizedStroke.Points[1].Position.X,
    "Resizing must scale linked stroke positions from the container origin.");
AssertNear(8, resizedStroke.Style.Thickness, "Resizing must scale linked stroke thickness.");
var textLinkedStroke = InkStrokeObject.Create(
    [new InkPoint(new PointD(980, 160), 0.5f, 1)],
    PenStyle.Default,
    containerDocument.NextZIndex,
    containerId: textContainer.Id);
containerDocument.AddObject(textLinkedStroke);
Assert(
    containerDocument.GetDeletionGroup(textContainer.Id)
        .Select(item => item.Id)
        .ToHashSet()
        .SetEquals(new[] { textContainer.Id, textLinkedStroke.Id }),
    "Deleting text should include its linked strokes like every other container.");

var secondLinkedStroke = InkStrokeObject.Create(
    [new InkPoint(new PointD(160, 160), 0.6f, 3)],
    PenStyle.Default,
    containerDocument.NextZIndex,
    containerId: firstContainer.Id);
containerDocument.AddObject(secondLinkedStroke);
var deletionGroup = containerDocument.GetDeletionGroup(firstContainer.Id);
Assert(
    deletionGroup.Select(item => item.Id).ToHashSet().SetEquals(
        new[] { firstContainer.Id, linkedStroke.Id, secondLinkedStroke.Id }),
    "Deleting a container should include all and only its linked strokes.");
var deletionHistory = new CommandHistory();
deletionHistory.Execute(new RemoveObjectsCommand(deletionGroup), containerDocument);
Assert(
    containerDocument.Objects.All(item =>
        item.Id != firstContainer.Id &&
        item.Id != linkedStroke.Id &&
        item.Id != secondLinkedStroke.Id),
    "Container deletion should remove the container and linked strokes.");
Assert(
    containerDocument.Objects.Any(item => item.Id == secondContainer.Id),
    "Container deletion must preserve unrelated objects.");
deletionHistory.Undo(containerDocument);
Assert(
    containerDocument.Objects.Any(item => item.Id == firstContainer.Id) &&
    containerDocument.Objects.Any(item => item.Id == linkedStroke.Id) &&
    containerDocument.Objects.Any(item => item.Id == secondLinkedStroke.Id),
    "Undo should restore the container and linked strokes together.");

var asset = new BoardAsset("asset-1", "pixel.png", "image/png", [1, 2, 3, 4]);
document.AddAsset(asset);
var archivedContainer = new ImageBoardObject(
    Guid.NewGuid(),
    document.NextZIndex,
    new RectD(100, 200, 640, 480),
    asset.Id);
document.AddObject(archivedContainer);
var archivedLinkedStroke = InkStrokeObject.Create(
    [
        new InkPoint(new PointD(120, 220), 0.4f, 1),
        new InkPoint(new PointD(160, 260), 0.7f, 2),
    ],
    new PenStyle(0xFFDC2626, 5, PenKind.Calligraphy),
    document.NextZIndex,
    containerId: archivedContainer.Id);
document.AddObject(archivedLinkedStroke);
var liveSnapshot = new BoardAsset("live-snapshot", "wacom.png", "image/png", [5, 6, 7, 8]);
document.AddAsset(liveSnapshot);
var archivedLiveView = new LiveViewBoardObject(
    Guid.NewGuid(),
    document.NextZIndex,
    new RectD(-500, -200, 800, 450),
    new LiveViewSourceConfiguration(LiveViewSourceKind.Display, "Wacom Cintiq", "DISPLAY4"),
    liveSnapshot.Id,
    DesiredFrameRate: 30,
    CaptureCursor: true,
    IsFrozen: true);
document.AddObject(archivedLiveView);
var archivedText = new TextBoardObject(
    Guid.NewGuid(),
    document.NextZIndex,
    new RectD(400, 300, 500, 240),
    "Notes",
    "First line\nSecond line",
    1.75,
    TextLanguageIds.Dax);
document.AddObject(archivedText);

await using var archive = new MemoryStream();
await BoardArchive.SaveAsync(document, archive);
archive.Position = 0;
var loaded = await BoardArchive.LoadAsync(archive);
Assert(loaded.Objects.Count == 5, "Archive should round-trip scene objects.");
Assert(BoardArchive.VersionFor(document) == BoardArchive.VersionBeforeFrames, "A board without frames is written in the version before them.");
var archivedFrame = new FrameBoardObject(Guid.NewGuid(), document.NextZIndex, new RectD(-50, -50, 1000, 600), "Slide 1");
document.AddObject(archivedFrame);
await using var framedArchive = new MemoryStream();
await BoardArchive.SaveAsync(document, framedArchive);
framedArchive.Position = 0;
var loadedWithFrame = await BoardArchive.LoadAsync(framedArchive);
Assert(
    BoardArchive.VersionFor(document) == BoardArchive.VersionWithFrames &&
    loadedWithFrame.Objects.OfType<FrameBoardObject>().Single() is { Title: "Slide 1", Bounds.Width: 1000 },
    "A frame round-trips with its title, and asks for the version that brought frames.");
document.RemoveObject(archivedFrame.Id);
Assert(loaded.Assets[asset.Id].Data.SequenceEqual(asset.Data), "Archive should round-trip asset bytes.");
Assert(
    loaded.Objects.OfType<InkStrokeObject>()
        .Single(item => item.Id == archivedLinkedStroke.Id) is
        { ContainerId: var loadedContainerId, Style.Kind: PenKind.Calligraphy } &&
    loadedContainerId == archivedContainer.Id,
    "Archive should preserve stroke-container links.");
Assert(
    loaded.Objects.OfType<LiveViewBoardObject>().Single() is
    {
        SnapshotAssetId: "live-snapshot",
        DesiredFrameRate: 30,
        CaptureCursor: true,
        IsFrozen: true,
        Source.Kind: LiveViewSourceKind.Display,
        Source.StableId: "DISPLAY4",
    },
    "Archive should preserve LiveView configuration and its last bitmap asset reference.");
Assert(
    loaded.Objects.OfType<TextBoardObject>().Single() is
    {
        Title: "Notes",
        Text: "First line\nSecond line",
        VisualScale: 1.75,
        LanguageId: TextLanguageIds.Dax,
    },
    "Archive should preserve text content, title, visual scale, and language.");

Guid legacyTextId = Guid.NewGuid();
string legacyScene = $$"""
{
  "version": 4,
  "objects": [
    {
      "type": "text",
      "id": "{{legacyTextId}}",
      "zIndex": 0,
      "bounds": { "x": 10, "y": 20, "width": 300, "height": 120 },
      "textTitle": "Legacy note",
      "textContent": "Saved before language support",
      "textVisualScale": 1
    }
  ],
  "assets": []
}
""";
await using var legacyArchiveStream = new MemoryStream();
using (var legacyArchive = new ZipArchive(
           legacyArchiveStream,
           ZipArchiveMode.Create,
           leaveOpen: true))
{
    ZipArchiveEntry sceneEntry = legacyArchive.CreateEntry("scene.json");
    await using Stream sceneStream = sceneEntry.Open();
    await sceneStream.WriteAsync(Encoding.UTF8.GetBytes(legacyScene));
}

legacyArchiveStream.Position = 0;
BoardDocument legacyDocument = await BoardArchive.LoadAsync(legacyArchiveStream);
Assert(
    legacyDocument.Objects.OfType<TextBoardObject>().Single().LanguageId ==
    TextLanguageIds.Plain,
    "Text containers from version 4 archives should load as plain text.");

var sqlArchiveDocument = new BoardDocument();
var archivedSqlText = new TextBoardObject(
    Guid.NewGuid(),
    sqlArchiveDocument.NextZIndex,
    new RectD(40, 60, 640, 320),
    "Text",
    "SELECT CustomerKey FROM dbo.Customer;",
    1,
    TextLanguageIds.SqlServer);
sqlArchiveDocument.AddObject(archivedSqlText);
await using var sqlArchive = new MemoryStream();
await BoardArchive.SaveAsync(sqlArchiveDocument, sqlArchive);
sqlArchive.Position = 0;
BoardDocument loadedSqlArchive = await BoardArchive.LoadAsync(sqlArchive);
Assert(
    loadedSqlArchive.Objects.OfType<TextBoardObject>().Single().LanguageId ==
    TextLanguageIds.SqlServer,
    "Archive round trips should preserve the SQL Server text language.");

// A language the selector offers has to survive the file whether or not the release
// that saved it could color it, and an identifier no release ever had still opens as
// plain text rather than losing the container.
var languageDocument = new BoardDocument();
foreach (string languageId in TextLanguageIds.All)
{
    languageDocument.AddObject(new TextBoardObject(
        Guid.NewGuid(),
        languageDocument.NextZIndex,
        new RectD(0, 0, 320, 120),
        "Snippet",
        "one" + (char)10 + "two",
        1,
        languageId));
}

await using var languageArchive = new MemoryStream();
await BoardArchive.SaveAsync(languageDocument, languageArchive);
languageArchive.Position = 0;
BoardDocument loadedLanguages = await BoardArchive.LoadAsync(languageArchive);
Assert(
    loadedLanguages.Objects.OfType<TextBoardObject>()
        .Select(text => text.LanguageId)
        .Order(StringComparer.Ordinal)
        .SequenceEqual(TextLanguageIds.All.Order(StringComparer.Ordinal), StringComparer.Ordinal),
    "Every language the selector offers round-trips through a board file.");

const string promptSource = "Keep this text.\r\n- An instruction with Unicode café 世界.\r\n  - An indented instruction.\n\n- ";
var promptObject = new TextBoardObject(Guid.NewGuid(), 0, new RectD(0, 0, 320, 200),
    "Prompt", promptSource, 1, TextLanguageIds.Prompt);
var promptDocument = new BoardDocument();
promptDocument.AddObject(promptObject);
await using var promptArchive = new MemoryStream();
await BoardArchive.SaveAsync(promptDocument, promptArchive);
promptArchive.Position = 0;
var loadedPrompt = await BoardArchive.LoadAsync(promptArchive);
Assert(loadedPrompt.Objects.OfType<TextBoardObject>().Single() == promptObject,
    "Prompt type, geometry, and exact source (including hyphens, spaces, and line endings) should survive saving.");
Assert(
    PromptText.Lines(promptSource).Select(line => line.IsBullet).SequenceEqual([false, true, true, false, true]) &&
    PromptText.Lines("one\rtwo\r\nthree\n").Count() == 4 &&
    PromptText.Lines(string.Empty).Single().Content == string.Empty,
    "Prompt paragraphs should preserve blank lines and recognize all common line endings.");
Assert(
    PromptText.ParseLine("  -  item") is { MarkerOffset: 2, ContentOffset: 5, Content: "item" } &&
    PromptText.ParseLine("- item").Content == "item" && PromptText.ParseLine("- ").IsBullet &&
    !PromptText.ParseLine("-1").IsBullet && !PromptText.ParseLine("---").IsBullet &&
    !PromptText.ParseLine("x - item").IsBullet && !PromptText.ParseLine("-").IsBullet,
    "Only a line-leading dash followed by a space is a bullet; signs and separators are ordinary text.");

string unknownLanguageScene = $$"""
{
  "version": 5,
  "objects": [
    {
      "type": "text",
      "id": "{{Guid.NewGuid()}}",
      "zIndex": 0,
      "bounds": { "x": 10, "y": 20, "width": 300, "height": 120 },
      "textTitle": "From a later release",
      "textContent": "print(1)",
      "textVisualScale": 1,
      "textLanguageId": "brainfuck"
    }
  ],
  "assets": []
}
""";
await using var unknownLanguageStream = new MemoryStream();
using (var unknownLanguageArchive = new ZipArchive(
           unknownLanguageStream,
           ZipArchiveMode.Create,
           leaveOpen: true))
{
    ZipArchiveEntry sceneEntry = unknownLanguageArchive.CreateEntry("scene.json");
    await using Stream sceneStream = sceneEntry.Open();
    await sceneStream.WriteAsync(Encoding.UTF8.GetBytes(unknownLanguageScene));
}

unknownLanguageStream.Position = 0;
BoardDocument unknownLanguageDocument = await BoardArchive.LoadAsync(unknownLanguageStream);
Assert(
    unknownLanguageDocument.Objects.OfType<TextBoardObject>().Single() is
    { LanguageId: TextLanguageIds.Plain, Text: "print(1)" },
    "A language identifier this release does not know opens as plain text, keeping the source.");

await using var previewArchive = new MemoryStream();
var previewBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };
await BoardArchive.SaveAsync(
    sqlArchiveDocument,
    previewArchive,
    previewPng: previewBytes);
previewArchive.Position = 0;
await using var extractedPreview = new MemoryStream();
Assert(
    BoardArchive.TryCopyPreview(previewArchive, extractedPreview) &&
    extractedPreview.ToArray().SequenceEqual(previewBytes),
    "A saved preview.png should be readable without loading the scene.");
previewArchive.Position = 0;
var loadedWithPreview = await BoardArchive.LoadAsync(previewArchive);
Assert(
    loadedWithPreview.Objects.Count == 1,
    "A board with a preview should still load its scene.");
await using var noPreviewArchive = new MemoryStream();
await BoardArchive.SaveAsync(sqlArchiveDocument, noPreviewArchive);
noPreviewArchive.Position = 0;
Assert(
    !BoardArchive.TryCopyPreview(noPreviewArchive, new MemoryStream()),
    "Boards saved without a preview should report none.");

AssertNear(
    20,
    PenStyleMetrics.MaximumThickness(new PenStyle(0xFF000000, 5, PenKind.Highlighter)),
    "Highlighter bounds should account for its broad nib.");
AssertNear(
    15,
    PenStyleMetrics.MaximumThickness(new PenStyle(0xFF000000, 5, PenKind.Calligraphy)),
    "Calligraphy bounds should account for the selected 3x nib width.");
var lightPressure = CalligraphyDynamics.AdjustPressure(0.2f, 0.5);
var heavyPressure = CalligraphyDynamics.AdjustPressure(0.9f, 0.5);
var fastStroke = CalligraphyDynamics.AdjustPressure(0.9f, 4);
Assert(
    heavyPressure > lightPressure,
    "Calligraphy width should increase with pressure.");
Assert(
    fastStroke < heavyPressure,
    "Calligraphy width should decrease as drawing speed increases.");
AssertNear(
    0.04 + (0.96 / 4.2),
    CalligraphyDynamics.AdjustPressure(1, 4),
    "Calligraphy speed should use the selected full-strength response.");

Assert(InkPalettes.Pen.Count == 6, "Pen palette should publish six teaching colors.");
Assert(InkPalettes.Highlighter.Count == 4, "Highlighter palette should publish four light colors.");
Assert(
    InkPalettes.Pen.Select(swatch => swatch.Argb).ToHashSet().SetEquals(
        new uint[] { 0xFF1F2937, 0xFFE64B3D, 0xFFE69F00, 0xFF56B4E9, 0xFF009E73, 0xFFCC79A7 }),
    "Pen palette should use the published color-blind-aware hex values.");
Assert(
    InkPalettes.Highlighter.Select(swatch => swatch.Argb).ToHashSet().SetEquals(
        new uint[] { 0xFFFACC15, 0xFFF472B6, 0xFF38BDF8, 0xFF2DD4BF }),
    "Highlighter palette should use the published light hex values.");
Assert(
    InkPalettes.DefaultPen is { Argb: 0xFFE64B3D, Thickness: 4, Kind: PenKind.Pen },
    "Pen should default to vermillion at size 4.");
Assert(
    InkPalettes.DefaultHighlighter is { Argb: 0xFFFACC15, Thickness: 6, Kind: PenKind.Highlighter },
    "Highlighter should default to yellow at size 6.");
Assert(
    InkPalettes.Normalize(new InkToolSettings { Argb = 0xFFDC2626, Thickness = 5 }, PenKind.Pen)
        is { Argb: 0xFFE64B3D, Thickness: 4 },
    "Unknown pen colors and sizes should snap to the pen default.");

var defaultSettings = AppSettingsSerializer.Parse(string.Empty);
Assert(
    defaultSettings.Import is { HorizontalSpacing: 32, VerticalSpacing: 32 } &&
    AppSettingsSerializer.Parse("{ \"version\": 17 }").Import is
        { HorizontalSpacing: 32, VerticalSpacing: 32 } &&
    AppSettingsSerializer.Parse("{ \"import\": null }").Import is
        { HorizontalSpacing: 32, VerticalSpacing: 32 },
    "New, older, and null import settings should preserve the existing 32 px gaps.");
Assert(
    AppSettingsSerializer.Parse("{ \"import\": { \"horizontalSpacing\": 0 } }").Import is
        { HorizontalSpacing: 0, VerticalSpacing: 32 },
    "A missing direction should default independently without replacing a saved zero gap.");
Assert(
    AppSettingsSerializer.Parse(
        "{ \"import\": { \"horizontalSpacing\": -10, \"verticalSpacing\": 10000 } }").Import is
        { HorizontalSpacing: ImportSettings.MinimumSpacing, VerticalSpacing: ImportSettings.MaximumSpacing },
    "Import spacing should clamp invalid saved values to the slider range.");
Assert(
    AppSettingsSerializer.Parse(AppSettingsSerializer.Format(new AppSettings
    {
        Import = new ImportSettings { HorizontalSpacing = double.NaN, VerticalSpacing = double.PositiveInfinity },
    })).Import is { HorizontalSpacing: 32, VerticalSpacing: 32 },
    "Non-finite spacing should fall back to the defaults before saving JSON.");
Assert(
    defaultSettings.ToolbarPlacement == ToolbarPlacement.TopRight,
    "Missing settings should default the toolbar to top-right.");
Assert(
    defaultSettings.CalligraphyAccess == CalligraphyAccess.DualPalette,
    "Missing settings should default the toolbar to the dual palette.");
Assert(
    defaultSettings.Pen.Argb == InkPalettes.DefaultPen.Argb &&
    defaultSettings.Highlighter.Thickness == 6,
    "Missing settings should default per-tool ink.");
Assert(
    defaultSettings.Laser.HoldSeconds == LaserSettings.DefaultHoldSeconds &&
    defaultSettings.Laser.FadeSeconds == LaserSettings.DefaultFadeSeconds &&
    defaultSettings.Laser.HoldMode == LaserHoldMode.Shared,
    "Missing settings should default laser hold to 2s shared, then fade.");
Assert(
    AppSettingsSerializer.Parse("{ \"laser\": { \"holdMode\": \"Sideways\" } }")
        .Laser.HoldMode == LaserHoldMode.Shared,
    "Unknown laser hold modes should fall back to shared.");
Assert(
    defaultSettings.Laser.TrailWeight == LaserTrailWeight.Light,
    "Missing settings should leave the laser trail following pen pressure.");
Assert(
    AppSettingsSerializer.Parse("{ \"laser\": { \"trailWeight\": \"Heavy\" } }")
        .Laser.TrailWeight == LaserTrailWeight.Light,
    "Unknown laser trail weights should fall back to light.");
Assert(
    AppSettingsSerializer.Parse("{ \"laser\": { \"trailWeight\": \"Bold\" } }")
        .Laser.TrailWeight == LaserTrailWeight.Bold,
    "Saved laser trail weight should still load.");
Assert(
    LaserSettings.MinimumTrailWidthFor(LaserTrailWeight.Light) <
        LaserSettings.MinimumTrailWidthFor(LaserTrailWeight.Medium) &&
    LaserSettings.MinimumTrailWidthFor(LaserTrailWeight.Medium) <
        LaserSettings.MinimumTrailWidthFor(LaserTrailWeight.Bold),
    "Heavier laser trail weights should raise the width floor.");
Assert(
    LaserSettings.MinimumTrailOpacityFor(LaserTrailWeight.Light) <
        LaserSettings.MinimumTrailOpacityFor(LaserTrailWeight.Bold) &&
    LaserSettings.MinimumTrailOpacityFor(LaserTrailWeight.Bold) <= 1,
    "Heavier laser trail weights should raise the opacity floor without exceeding it.");
Assert(
    AppSettingsSerializer.Parse("{ \"laser\": { \"holdMode\": \"PerStroke\" } }")
        .Laser.HoldMode == LaserHoldMode.PerStroke,
    "Saved per-stroke laser hold should still load.");
Assert(
    AppSettingsSerializer.Parse(
        "{ \"laser\": { \"holdSeconds\": -1, \"fadeSeconds\": 99 } }") is
    {
        Laser.HoldSeconds: LaserSettings.MinimumHoldSeconds,
        Laser.FadeSeconds: LaserSettings.MaximumFadeSeconds,
    },
    "Laser timing should clamp to the supported range.");
var formattedSettings = AppSettingsSerializer.Format(new AppSettings
{
    ToolbarPlacement = ToolbarPlacement.BottomCenter,
    CalligraphyAccess = CalligraphyAccess.SizeRow,
    Highlighter = new InkToolSettings { Argb = 0xFFF472B6, Thickness = 10 },
    Import = new ImportSettings { HorizontalSpacing = 80, VerticalSpacing = 120 },
});
Assert(
    formattedSettings.Contains("BottomCenter", StringComparison.Ordinal),
    "Settings JSON should persist the toolbar placement name.");
var roundTripped = AppSettingsSerializer.Parse(formattedSettings);
Assert(
    roundTripped.Import is { HorizontalSpacing: 80, VerticalSpacing: 120 },
    "Horizontal and vertical import spacing should persist independently.");
Assert(
    roundTripped.ToolbarPlacement == ToolbarPlacement.BottomCenter &&
    roundTripped.CalligraphyAccess == CalligraphyAccess.SizeRow &&
    roundTripped.Highlighter.Argb == 0xFFF472B6 &&
    roundTripped.Highlighter.Thickness == 10,
    "Settings JSON should round-trip toolbar placement, calligraphy access, and per-tool ink.");
Assert(
    AppSettingsSerializer.Parse("{ \"calligraphyAccess\": \"Sideways\" }").CalligraphyAccess ==
    CalligraphyAccess.DualPalette,
    "Unknown toolbar layouts should fall back to the dual palette.");
Assert(
    AppSettingsSerializer.Parse("{ \"calligraphyAccess\": \"Chevron\" }").CalligraphyAccess ==
    CalligraphyAccess.Chevron,
    "Previously saved chevron layout should still load.");
Assert(
    AppSettingsSerializer.Parse("{ }").ToolbarPlacement == ToolbarPlacement.TopRight,
    "Partial settings should keep the top-right default.");
Assert(
    AppSettingsSerializer.Parse("{ \"toolbarPlacement\": \"Sideways\" }").ToolbarPlacement ==
    ToolbarPlacement.TopRight,
    "Unknown toolbar placements should fall back to top-right.");
Assert(
    AppSettingsSerializer.Parse("{ \"toolbarPlacement\": 99 }").ToolbarPlacement ==
    ToolbarPlacement.TopRight,
    "Out-of-range toolbar placements should fall back to top-right.");
Assert(
    AppSettingsSerializer.Parse(
        "{ \"pen\": { \"argb\": 1, \"thickness\": 99 } }").Pen.Argb ==
    InkPalettes.DefaultPen.Argb,
    "Saved ink that is no longer in the palette should fall back to the tool default.");
Assert(
    defaultSettings.StartupMonitor == StartupMonitorKind.WacomIfPresent &&
    defaultSettings.StartupMonitorName is null &&
    !defaultSettings.StartFullScreen &&
    defaultSettings.FingerMode == FingerMode.WhenNoPen &&
    defaultSettings.CheckForUpdates,
    "Missing settings should default to Wacom-if-present, a windowed start, finger drawing when no pen is detected, and update checks on.");
Assert(
    UpdateVersion.ReadManifestVersion("{ \"version\": \"v0.9.3\" }") == "0.9.3",
    "stable.json should accept a leading v and keep three parts.");
Assert(
    UpdateVersion.ReadManifestVersion("{ \"version\": \"0.9.2-dev.3234\" }") == "0.9.2",
    "Pre-release suffixes on a manifest version should be ignored.");
Assert(
    UpdateVersion.ReadManifestVersion("{ \"version\": \"nope\" }") is null &&
    UpdateVersion.ReadManifestVersion("not json") is null,
    "Unknown or malformed manifests should be ignored.");
Assert(
    UpdateVersion.IsNewer("0.9.2", "0.9.3") &&
    !UpdateVersion.IsNewer("0.9.3", "0.9.3") &&
    !UpdateVersion.IsNewer("0.9.3", "0.9.2") &&
    !UpdateVersion.IsNewer("0.9.3+abc", "0.9.2"),
    "A newer three-part version should notify; equal or older should not.");
Assert(
    AppSettingsSerializer.Parse("{ \"checkForUpdates\": false }") is
    { CheckForUpdates: false },
    "A saved opt-out of update checks should still load.");
Assert(
    AppSettingsSerializer.Parse("{ \"latestKnownVersion\": \"v1.2.3-dev\", \"lastDismissedVersion\": \"nope\" }") is
    { LatestKnownVersion: "1.2.3", LastDismissedVersion: null },
    "Stored update versions should normalize or drop.");
Assert(
    AppSettingsSerializer.Parse("{ \"startupMonitor\": \"Sideways\" }").StartupMonitor ==
    StartupMonitorKind.WacomIfPresent,
    "Unknown startup monitors should fall back to Wacom-if-present.");
Assert(
    AppSettingsSerializer.Parse(
        "{ \"startupMonitor\": \"Named\" }").StartupMonitor ==
    StartupMonitorKind.WacomIfPresent,
    "A named startup monitor without a name should fall back to Wacom-if-present.");
Assert(
    AppSettingsSerializer.Parse(
        "{ \"startupMonitor\": \"Primary\", \"startupMonitorName\": \"Cintiq Pro 27\" }") is
    {
        StartupMonitor: StartupMonitorKind.Primary,
        StartupMonitorName: null,
    },
    "A primary startup monitor should drop a leftover display name.");
var namedStartup = AppSettingsSerializer.Parse(
    AppSettingsSerializer.Format(new AppSettings
    {
        StartupMonitor = StartupMonitorKind.Named,
        StartupMonitorName = "  Cintiq Pro 27  ",
        StartFullScreen = true,
    }));
Assert(
    namedStartup.StartupMonitor == StartupMonitorKind.Named &&
    namedStartup.StartupMonitorName == "Cintiq Pro 27" &&
    namedStartup.StartFullScreen,
    "Settings JSON should round-trip a named startup monitor and full-screen start.");
Assert(
    AppSettingsSerializer.Parse("{ \"fingerMode\": \"Sideways\" }").FingerMode == FingerMode.WhenNoPen,
    "Unknown finger-mode values should fall back to when-no-pen.");
Assert(
    AppSettingsSerializer.Parse("{ \"fingerMode\": \"Off\" }").FingerMode == FingerMode.Off,
    "Saved finger drawing Off should still load.");
Assert(
    AppSettingsSerializer.Parse("{ \"fingerMode\": \"On\" }").FingerMode == FingerMode.On,
    "Saved finger drawing On should still load.");
Assert(
    AppSettingsSerializer.Parse("{ \"fingerMode\": \"WhenNoPen\" }").FingerMode == FingerMode.WhenNoPen,
    "Saved when-no-pen finger drawing should still load.");
var fingerModeRoundTrip = AppSettingsSerializer.Parse(
    AppSettingsSerializer.Format(new AppSettings { FingerMode = FingerMode.WhenNoPen }));
Assert(
    fingerModeRoundTrip.FingerMode == FingerMode.WhenNoPen,
    "Settings JSON should round-trip when-no-pen finger drawing.");
Assert(
    defaultSettings.MouseMode == MouseMode.WhenNoDigitizer,
    "Missing settings should default mouse drawing to when there is no digitizer.");
Assert(
    AppSettingsSerializer.Parse("{ \"mouseMode\": \"Sideways\" }").MouseMode ==
    MouseMode.WhenNoDigitizer,
    "Unknown mouse-mode values should fall back to when-no-digitizer.");
Assert(
    AppSettingsSerializer.Parse("{ \"mouseMode\": \"Off\" }").MouseMode == MouseMode.Off &&
    AppSettingsSerializer.Parse("{ \"mouseMode\": \"On\" }").MouseMode == MouseMode.On,
    "Saved mouse drawing Off and On should still load.");
var mouseModeRoundTrip = AppSettingsSerializer.Parse(
    AppSettingsSerializer.Format(new AppSettings { MouseMode = MouseMode.On }));
Assert(
    mouseModeRoundTrip.MouseMode == MouseMode.On,
    "Settings JSON should round-trip mouse drawing.");
// Settings written before mouse drawing existed carry no mouseMode at all, and
// have to arrive as the new default rather than as Off - the whole point is the
// person who found nothing worked.
var settingsFromVersion12 = AppSettingsSerializer.Parse(
    "{ \"version\": 12, \"fingerMode\": \"Off\", \"toolbarPlacement\": \"BottomLeft\" }");
Assert(
    settingsFromVersion12.MouseMode == MouseMode.WhenNoDigitizer &&
    settingsFromVersion12.FingerMode == FingerMode.Off &&
    settingsFromVersion12.ToolbarPlacement == ToolbarPlacement.BottomLeft &&
    settingsFromVersion12.Version == AppSettingsSerializer.CurrentVersion,
    "Settings saved before mouse drawing existed should upgrade and keep their own choices.");
Assert(
    defaultSettings.SuggestMouseMode,
    "Missing settings should offer mouse drawing when the mouse picks a tool.");
Assert(
    settingsFromVersion12.SuggestMouseMode,
    "Settings saved before the offer existed should get it, not lose it.");
Assert(
    AppSettingsSerializer.Parse("{ \"suggestMouseMode\": false }") is
    { SuggestMouseMode: false },
    "An offer declined for good should still be declined after a restart.");
var offerRoundTrip = AppSettingsSerializer.Parse(
    AppSettingsSerializer.Format(new AppSettings { SuggestMouseMode = false }));
Assert(
    !offerRoundTrip.SuggestMouseMode,
    "Settings JSON should round-trip the mouse drawing offer.");
// The Eraser button costs the toolbar a row, so it stays off until asked for -
// including for settings written before it could be asked for.
Assert(
    !defaultSettings.ShowEraserButton,
    "Missing settings should leave the Eraser off the toolbar.");
Assert(
    !settingsFromVersion12.ShowEraserButton,
    "Settings saved before the Eraser button existed should not grow a toolbar row.");
var eraserButtonRoundTrip = AppSettingsSerializer.Parse(
    AppSettingsSerializer.Format(new AppSettings { ShowEraserButton = true }));
Assert(
    eraserButtonRoundTrip.ShowEraserButton,
    "Settings JSON should round-trip the always-show-the-Eraser choice.");
// The grid is an application preference, never part of a board, so what a person
// chose is what they get back at the next start.
Assert(
    defaultSettings.Grid == GridStyle.Off && defaultSettings.LastGridStyle == GridStyle.Lines,
    "A new setup draws no grid, and the first toggle from Off gives lines.");
Assert(
    settingsFromVersion12.Grid == GridStyle.Off,
    "Settings saved before the grid existed should not grow one.");
var gridRoundTrip = AppSettingsSerializer.Parse(
    AppSettingsSerializer.Format(new AppSettings
    {
        Grid = GridStyle.Dots,
        LastGridStyle = GridStyle.Dots,
    }));
Assert(
    gridRoundTrip is { Grid: GridStyle.Dots, LastGridStyle: GridStyle.Dots },
    "Settings JSON should round-trip the grid and the style the toggle brings back.");
Assert(
    AppSettingsSerializer.Parse("{ \"grid\": \"Squares\" }").Grid == GridStyle.Off,
    "A grid style nothing draws reads as no grid.");
Assert(
    AppSettingsSerializer.Parse("{ \"lastGridStyle\": \"Off\" }").LastGridStyle == GridStyle.Lines,
    "Off as the remembered style would leave the toggle nothing to turn on, so it reads as lines.");
Assert(
    defaultSettings.SnippetFormatOrder is ["dax", "sqlserver", "kql", "plain"],
    "A new setup tries every language before plain text, so pasted code is code without a setting.");
Assert(
    TextLanguageIds.NormalizeOrder(["sqlserver", "plain", "dax", "plain", "not-a-language"]) is
        ["sqlserver", "kql", "plain", "dax"],
    "Snippet format order should drop unknowns, keep first-seen order, and put a missing language in front of plain text.");
var snippetOrderRoundTrip = AppSettingsSerializer.Parse(
    AppSettingsSerializer.Format(new AppSettings
    {
        SnippetFormatOrder = ["dax", "sqlserver", "plain"],
    }));
Assert(
    snippetOrderRoundTrip.SnippetFormatOrder is ["dax", "sqlserver", "kql", "plain"],
    "A language added by an upgrade joins in front of plain text when plain text is not first.");
Assert(
    TextLanguageIds.NormalizeOrder(["plain", "dax"]) is ["plain", "dax", "sqlserver", "kql"],
    "An order that starts with plain text keeps pastes plain: added languages go last.");
Assert(
    AppSettingsSerializer.Parse("{ }").SnippetFormatOrder is
        ["dax", "sqlserver", "kql", "plain"],
    "Partial settings should fill the default snippet format order.");
Assert(
    AppSettingsSerializer.Parse("""{ "version": 15, "snippetFormatOrder": ["plain", "dax", "sqlserver"] }""").SnippetFormatOrder is
        ["dax", "sqlserver", "kql", "plain"] &&
    AppSettingsSerializer.Parse("""{ "version": 15, "snippetFormatOrder": ["plain", "dax", "sqlserver", "kql"] }""").SnippetFormatOrder is
        ["dax", "sqlserver", "kql", "plain"],
    "An older file still holding a shipped default was never customized and takes the new default.");
Assert(
    AppSettingsSerializer.Parse("""{ "version": 15, "snippetFormatOrder": ["sqlserver", "plain", "dax"] }""").SnippetFormatOrder is
        ["sqlserver", "kql", "plain", "dax"],
    "An older file with a chosen order keeps it, with the new language in front of plain text.");
Assert(
    AppSettingsSerializer.Parse("""{ "version": 16, "snippetFormatOrder": ["plain", "dax", "sqlserver", "kql"] }""").SnippetFormatOrder is
        ["plain", "dax", "sqlserver", "kql"],
    "A current file that puts plain text first chose to, and is left alone.");

// Choosing a language and recognizing one are two lists. Everything the selector offers
// is saved and restored; only the four that can read a snippet claim a paste, so a
// language chosen by hand is skipped in the snippet format order rather than read as
// plain text, which would move plain text up an order it was never part of.
Assert(
    TextLanguageIds.All.Count == 16 &&
    TextLanguageIds.All[0] == TextLanguageIds.Plain &&
    TextLanguageIds.All.Distinct(StringComparer.Ordinal).Count() == 16,
    "The selector offers sixteen distinct text types, plain text first.");
Assert(
    TextLanguageIds.DetectionOrder is ["dax", "sqlserver", "kql", "plain"] &&
    TextLanguageIds.All.Count(TextLanguageIds.CanDetect) == 4,
    "Only the four languages that read a snippet take part in detection.");
Assert(
    TextLanguageIds.Normalize("Python") == TextLanguageIds.Python &&
    TextLanguageIds.Normalize(" VBNET ") == TextLanguageIds.VbNet &&
    TextLanguageIds.Normalize("C++") == TextLanguageIds.Plain,
    "A language added for manual selection normalizes for persistence; a caption is not an identifier.");
Assert(
    TextLanguageIds.NormalizeOrder(["dax", "prompt", "python", "rust", "plain"]) is
        ["dax", "sqlserver", "kql", "plain"],
    "A language chosen by hand is ignored in the snippet format order, not read as plain text.");
Assert(
    TextLanguageIds.NormalizeOrder(["not-a-language", "dax"]) is
        ["plain", "dax", "sqlserver", "kql"],
    "A name that is no language still reads as plain text, so an order beginning with one keeps pastes plain.");
Assert(
    AppSettingsSerializer.Parse("""{ "version": 16, "snippetFormatOrder": ["python", "dax", "rust", "plain"] }""")
        .SnippetFormatOrder is ["dax", "sqlserver", "kql", "plain"],
    "A manual-only language written into settings is dropped from the saved order.");

// F6 answers a language it cannot format by asking for a vote, and the issue it opens
// is fixed per language rather than assembled from anything on the board.
Assert(
    new[]
    {
        (TextLanguageIds.Python, 108), (TextLanguageIds.C, 109), (TextLanguageIds.Cpp, 110),
        (TextLanguageIds.Java, 111), (TextLanguageIds.CSharp, 112), (TextLanguageIds.JavaScript, 113),
        (TextLanguageIds.TypeScript, 114), (TextLanguageIds.VbNet, 115), (TextLanguageIds.R, 116),
        (TextLanguageIds.Rust, 117), (TextLanguageIds.Php, 118),
    }.All(entry =>
        TextLanguageIds.FormattingRequestUrl(entry.Item1) ==
        $"https://github.com/sql-bi/SQLBI-Whiteboard/issues/{entry.Item2}"),
    "Each language chosen by hand resolves to its own voting issue.");
Assert(
    TextLanguageIds.All.Where(TextLanguageIds.CanDetect)
        .All(languageId => TextLanguageIds.FormattingRequestUrl(languageId) is null) &&
    TextLanguageIds.FormattingRequestUrl("not-a-language") is null,
    "Plain text and the three languages that format have nothing to vote for.");
const string longMeasure = "Sales Amount := SUMX ( Sales, Sales[Quantity] * Sales[Net Price] * ( 1 - Sales[Discount] ) )";
Assert(
    DaxLanguageEngine.TryFormat(longMeasure, 65, out string narrowDax) &&
    DaxLanguageEngine.TryFormat(longMeasure, 160, out string wideDax) &&
    narrowDax.Split('\n').Length > wideDax.Split('\n').Length &&
    narrowDax.Split('\n').All(line => line.TrimEnd().Length <= 65),
    "The DAX formatter wraps to the columns it is given, so a wider container gets longer lines.");
Assert(
    !DaxLanguageEngine.LooksLike("Sales") && !DaxLanguageEngine.LooksLike("42") && !DaxLanguageEngine.LooksLike("Why does December spike?") &&
    DaxLanguageEngine.LooksLike("Sales Amount := SUM ( Sales[Amount] )") && DaxLanguageEngine.LooksLike("[Amount] * 2"),
    "DAX claims a snippet only when it has a function, operator, keyword, or column reference.");
Assert(
    !SqlServerLanguageEngine.LooksLike("Sales") && !SqlServerLanguageEngine.LooksLike("42") &&
    SqlServerLanguageEngine.LooksLike("SELECT 1") && SqlServerLanguageEngine.LooksLike("SELECT COUNT(*) FROM dbo.Sales"),
    "SQL claims a snippet only when it has a keyword or function.");
Assert(
    !KqlLanguageEngine.LooksLike("Sales") && !KqlLanguageEngine.LooksLike("42") && !KqlLanguageEngine.LooksLike("Open questions") &&
    KqlLanguageEngine.LooksLike("Sales | count") && KqlLanguageEngine.LooksLike("print 1"),
    "KQL claims a snippet only when it has a pipe, operator, keyword, command, or function.");
Assert(
    defaultSettings.PenButtons.Barrel == PenButtonAction.Laser,
    "Missing settings should assign Laser to the pen barrel button.");
Assert(
    defaultSettings.WarnWhenNoDigitizer,
    "Missing settings should warn when Windows reports nothing to draw with.");
Assert(
    !AppSettingsSerializer.Parse(
        AppSettingsSerializer.Format(new AppSettings { WarnWhenNoDigitizer = false }))
        .WarnWhenNoDigitizer,
    "Dismissing the no-digitizer notice should survive a round trip.");
Assert(
    AppSettingsSerializer.Parse("{ \"penButtons\": { \"barrel\": \"Sideways\" } }")
        .PenButtons.Barrel == PenButtonAction.Laser,
    "An unknown pen button action should fall back to Laser.");
Assert(
    AppSettingsSerializer.Parse("{ \"penButtons\": { \"barrel\": \"StraightLine\" } }")
        .PenButtons.Barrel == PenButtonAction.StraightLine,
    "A saved pen button assignment should still load.");
Assert(
    AppSettingsSerializer.Parse("{ \"penButtons\": { \"lower\": \"Eraser\", \"upper\": \"Laser\" } }")
        .PenButtons.Barrel == PenButtonAction.Laser,
    "Settings written before the upper button was dropped should load, not reset the file.");
Assert(
    AppSettingsSerializer.Parse(
        AppSettingsSerializer.Format(new AppSettings
        {
            PenButtons = new PenButtonSettings { Barrel = PenButtonAction.StraightLine },
        })).PenButtons.Barrel == PenButtonAction.StraightLine,
    "Settings JSON should round-trip the pen button assignment.");
Assert(
    PenBarrelButton.IsWritingTipName("Tip") &&
    PenBarrelButton.IsWritingTipName("TipButton") &&
    !PenBarrelButton.IsWritingTipName("Eraser") &&
    !PenBarrelButton.IsWritingTipName("Secondary Tip"),
    "The writing tip is not the barrel button; an Eraser or Secondary name is not the tip.");
Assert(
    PenBarrelButton.IsReverseEndName("Eraser") &&
    PenBarrelButton.IsReverseEndName("Barrel Button 2") &&
    PenBarrelButton.IsReverseEndName("Upper") &&
    PenBarrelButton.IsReverseEndName("Secondary") &&
    !PenBarrelButton.IsReverseEndName("Barrel") &&
    !PenBarrelButton.IsReverseEndName("Tip"),
    "Windows names the reverse end Eraser, or the second/upper/secondary barrel.");

var daxSource = """
Tricky :=
-- a real comment
VAR Year = 2024
VAR Note = "-- not a comment"
RETURN Year & Note & Sales[Amount]
""";
Assert(
    DaxLanguageEngine.DefaultMaximumLineLength == 65,
    "DAX formatting should use the configured 65-character default line length.");
Assert(
    DaxLanguageEngine.TryFormat(
        daxSource,
        DaxLanguageEngine.DefaultMaximumLineLength,
        out string formattedDax),
    "Valid DAX should be formatted successfully.");
Assert(
    formattedDax.Contains("VAR Year =", StringComparison.Ordinal) &&
    formattedDax.Contains("RETURN", StringComparison.Ordinal),
    "DAX formatting should retain declarations and RETURN expressions.");
Assert(
    DaxLanguageEngine.Format(formattedDax) == formattedDax,
    "DAX formatting should be idempotent.");

IReadOnlyList<DaxClassifiedSpan> daxSpans = DaxLanguageEngine.Classify(formattedDax);
string DaxText(DaxClassifiedSpan span) =>
    formattedDax.Substring(span.Start, span.Length);
Assert(
    daxSpans.Count(span =>
        span.Classification == DaxTextClassification.Variable &&
        DaxText(span) == "Year") == 2,
    "A DAX variable should be classified at its declaration and use.");
Assert(
    daxSpans.Any(span =>
        span.Classification == DaxTextClassification.Keyword &&
        DaxText(span) == "VAR"),
    "DAX keywords should be classified for bold syntax highlighting.");
Assert(
    daxSpans.Any(span =>
        span.Classification == DaxTextClassification.StringLiteral &&
        DaxText(span) == "\"-- not a comment\""),
    "Comment markers inside DAX strings must remain string literals.");
Assert(
    daxSpans.Count(span => span.Classification == DaxTextClassification.Comment) == 1,
    "Only the real DAX comment should receive comment highlighting.");
Assert(
    daxSpans.Any(span =>
        span.Classification == DaxTextClassification.TableName &&
        DaxText(span) == "Sales") &&
    daxSpans.Any(span =>
        span.Classification == DaxTextClassification.ColumnReference &&
        DaxText(span) == "[Amount]"),
    "DAX table qualifiers and column references should receive distinct classifications.");
Assert(
    DaxLanguageEngine.DefinedObjectName(formattedDax) == "Tricky",
    "The DAX engine should identify a defined measure name.");
Assert(
    DaxLanguageEngine.DefinedObjectName(
        "[Sales Amount] = SUMX ( Sales, Sales[Quantity] * Sales[Net Price] )") ==
    "Sales Amount",
    "The DAX engine should extract a bracketed measure name for the container title.");
Assert(
    DaxLanguageEngine.DefinedObjectName(
        "Sales Amount = SUMX ( Sales, Sales[Quantity] * Sales[Net Price] )") ==
    "Sales Amount",
    "The DAX engine should extract a multi-word measure name for the container title.");
Assert(
    DaxLanguageEngine.IsQuery("EVALUATE Sales"),
    "The DAX engine should identify query text.");
Assert(
    !DaxLanguageEngine.TryFormat(
        string.Empty,
        DaxLanguageEngine.DefaultMaximumLineLength,
        out string emptyFormattedDax) &&
    emptyFormattedDax.Length == 0,
    "Formatting an empty DAX editor should be a safe no-op.");
Assert(
    TextLanguageIds.Normalize("unsupported-language") == TextLanguageIds.Plain,
    "Unknown saved text languages should fall back to plain text.");

var sqlSource = """
CREATE OR ALTER PROCEDURE [sales].[GetCustomers]
    @MinimumSales money
AS
BEGIN
    -- Keep only customers above the requested amount
    SELECT c.CustomerKey AS CustomerId,
           SUM(s.Amount) AS TotalSales
    FROM dbo.Customers AS c
    INNER JOIN dbo.Sales AS s ON s.CustomerKey = c.CustomerKey
    WHERE s.Amount >= @MinimumSales
      AND c.Note <> N'-- this is a string'
    GROUP BY c.CustomerKey;
END;
GO
""";
SqlServerTextAnalysis sqlAnalysis = SqlServerLanguageEngine.Analyze(sqlSource);
Assert(sqlAnalysis.Diagnostics.Count == 0, "Valid SQL Server code should parse without diagnostics.");
Assert(
    sqlAnalysis.DefinedObjectName == "sales.GetCustomers",
    "SQL Server analysis should identify the schema-qualified procedure name.");
string SqlText(SqlServerClassifiedSpan span) =>
    sqlSource.Substring(span.Start, span.Length);
Assert(
    sqlAnalysis.Spans.Any(span =>
        span.Classification == SqlServerTextClassification.Keyword &&
        SqlText(span).Equals("SELECT", StringComparison.OrdinalIgnoreCase)),
    "SQL Server keywords should be classified.");
Assert(
    sqlAnalysis.Spans.Any(span =>
        span.Classification == SqlServerTextClassification.Function &&
        SqlText(span).Equals("SUM", StringComparison.OrdinalIgnoreCase)),
    "SQL Server function calls should be classified.");
Assert(
    sqlAnalysis.Spans.Any(span =>
        span.Classification == SqlServerTextClassification.Comment &&
        SqlText(span).StartsWith("-- Keep only", StringComparison.Ordinal)),
    "SQL Server comments should be classified.");
Assert(
    sqlAnalysis.Spans.Any(span =>
        span.Classification == SqlServerTextClassification.StringLiteral &&
        SqlText(span) == "N'-- this is a string'"),
    "Comment markers inside SQL Server strings must remain string literals.");
Assert(
    sqlAnalysis.Spans.Any(span =>
        span.Classification == SqlServerTextClassification.Parameter &&
        SqlText(span) == "@MinimumSales"),
    "SQL Server procedure parameters should receive their own classification.");
Assert(
    sqlAnalysis.Spans.Any(span =>
        span.Classification == SqlServerTextClassification.TableName &&
        SqlText(span).Equals("Customers", StringComparison.OrdinalIgnoreCase)),
    "SQL Server table names should be classified from the syntax tree.");
Assert(
    sqlAnalysis.Spans.Any(span =>
        span.Classification == SqlServerTextClassification.ColumnName &&
        SqlText(span).Equals("CustomerKey", StringComparison.OrdinalIgnoreCase)),
    "SQL Server column names should be classified from the syntax tree.");
Assert(
    sqlAnalysis.Spans.Any(span =>
        span.Classification == SqlServerTextClassification.DefinitionName &&
        SqlText(span).Trim('[', ']').Equals("GetCustomers", StringComparison.OrdinalIgnoreCase)),
    "The SQL Server object being defined should receive definition-name highlighting.");
Assert(
    SqlServerLanguageEngine.TryFormat(sqlSource, out string formattedSql),
    "Valid SQL Server code should format successfully.");
Assert(
    formattedSql.Contains("SELECT", StringComparison.Ordinal) &&
    formattedSql.Contains("-- Keep only customers", StringComparison.Ordinal) &&
    formattedSql.Contains("N'-- this is a string'", StringComparison.Ordinal) &&
    formattedSql.EndsWith("GO", StringComparison.Ordinal),
    "SQL Server formatting should retain keywords, comments, strings, and GO separators.");
Assert(
    SqlServerLanguageEngine.TryFormat(formattedSql, out string formattedSqlAgain) &&
    formattedSqlAgain == formattedSql,
    "SQL Server formatting should be idempotent.");
Assert(
    SqlServerLanguageEngine.TryFormat(
        "select sum(amount) from dbo.sales;",
        out string formattedLowercaseSql) &&
    formattedLowercaseSql.Contains("SELECT", StringComparison.Ordinal) &&
    formattedLowercaseSql.Contains("sum", StringComparison.OrdinalIgnoreCase),
    "SQL Server formatting should normalize lowercase keywords while retaining function calls.");
const string invalidSql = "SELECT * FROM WHERE";
Assert(
    !SqlServerLanguageEngine.TryFormat(invalidSql, out string unchangedInvalidSql) &&
    unchangedInvalidSql == invalidSql,
    "Invalid SQL Server code should be left untouched by formatting.");
Assert(
    SqlServerLanguageEngine.Analyze(invalidSql).Diagnostics.Count > 0,
    "Invalid SQL Server code should expose parser diagnostics without interrupting highlighting.");
const string repeatedBatchSource = "SELECT 1;\nGO 2";
Assert(
    SqlServerLanguageEngine.TryFormat(repeatedBatchSource, out string repeatedBatchSql) &&
    repeatedBatchSql.EndsWith("GO 2", StringComparison.Ordinal),
    "SQL Server formatting should preserve GO repetition counts.");
Assert(
    SqlServerLanguageEngine.DefinedObjectName(
        "CREATE VIEW reporting.CustomerSales AS SELECT 1 AS Amount;") ==
    "reporting.CustomerSales",
    "SQL Server analysis should identify a view name for the container title.");
Assert(
    SqlServerLanguageEngine.DefinedObjectName("SELECT 1;") is null,
    "An ordinary SQL query should use the generic SQL Code title.");
Assert(
    TextLanguageIds.Normalize("SQLSERVER") == TextLanguageIds.SqlServer,
    "The SQL Server text language identifier should normalize for persistence.");

// KQL rides on Microsoft's own parser, so what is checked here is the adapter: that a
// snippet without a database still classifies and formats, that formatting leaves the
// code alone, and that the spacing an author chose around a join hint survives it.
var kqlSource = """
let Threshold = 10;
let ErrorSummary = (T:(UserId:string, EventType:string)) {
    T
    | where EventType == "Error"
    | summarize ErrorCount = count() by UserId
};
// Main query execution combining optimization hints
ErrorSummary(AppLogs)
| where ErrorCount > Threshold
| join kind=inner hint.strategy=broadcast UserMetadata on UserId
| project UserId, ErrorCount, Region
""";
KqlTextAnalysis kqlAnalysis = KqlLanguageEngine.Analyze(kqlSource);
Assert(
    kqlAnalysis.Diagnostics.Count == 0,
    "A KQL snippet naming tables it cannot resolve should still parse without complaint.");
string KqlText(KqlClassifiedSpan span) => kqlSource.Substring(span.Start, span.Length);
bool KqlHas(KqlTextClassification classification, string text) =>
    kqlAnalysis.Spans.Any(span =>
        span.Classification == classification &&
        KqlText(span).Equals(text, StringComparison.Ordinal));
Assert(
    KqlHas(KqlTextClassification.QueryOperator, "summarize") &&
    KqlHas(KqlTextClassification.Function, "count") &&
    KqlHas(KqlTextClassification.Variable, "Threshold") &&
    KqlHas(KqlTextClassification.Parameter, "T") &&
    KqlHas(KqlTextClassification.ColumnName, "UserId") &&
    KqlHas(KqlTextClassification.DataType, "string") &&
    KqlHas(KqlTextClassification.QueryParameter, "kind"),
    "KQL classification should tell operators, functions, and names apart.");
Assert(
    KqlHas(KqlTextClassification.StringLiteral, "\"Error\"") &&
    kqlAnalysis.Spans.Any(span =>
        span.Classification == KqlTextClassification.Comment &&
        KqlText(span).StartsWith("// Main query", StringComparison.Ordinal)),
    "KQL strings and comments should be classified.");
Assert(
    KqlLanguageEngine.TryFormat(kqlSource, out string formattedKql),
    "Valid KQL should format successfully.");
Assert(
    formattedKql.Contains(
        "| join kind=inner hint.strategy=broadcast UserMetadata on UserId",
        StringComparison.Ordinal),
    "Formatting should leave the spacing an author chose around a join hint alone.");
Assert(
    formattedKql.Contains(
        "// Main query execution combining optimization hints",
        StringComparison.Ordinal) &&
    formattedKql.Contains("\"Error\"", StringComparison.Ordinal),
    "KQL formatting should keep comments and string literals.");
Assert(
    KqlLanguageEngine.TryFormat(formattedKql, out string formattedKqlAgain) &&
    formattedKqlAgain == formattedKql,
    "KQL formatting should be idempotent.");
Assert(
    KqlLanguageEngine.TryFormat(
        kqlSource.Replace("\r\n", "\n").Replace("\n", "\r\n"),
        out string formattedCrlfKql) &&
    formattedCrlfKql == formattedKql,
    "Text stored with carriage returns should format to the same code as text without.");
const string invalidKql = "let Threshold =";
Assert(
    !KqlLanguageEngine.TryFormat(invalidKql, out string unchangedInvalidKql) &&
    unchangedInvalidKql == invalidKql,
    "Invalid KQL should be left untouched by formatting.");
Assert(
    KqlLanguageEngine.Analyze(invalidKql).Diagnostics.Count > 0,
    "Invalid KQL should expose parser diagnostics without interrupting highlighting.");
Assert(
    KqlLanguageEngine.DefinedObjectName(
        ".create-or-alter function with (docstring = 'Errors per user') " +
        "PerUserErrors() { AppLogs | count }") == "PerUserErrors",
    "The name a command defines should be the function's, not its first property's.");
Assert(
    KqlLanguageEngine.DefinedObjectName(kqlSource) is null,
    "An ordinary KQL query should use the generic KQL Code title.");
Assert(
    TextLanguageIds.Normalize("KQL") == TextLanguageIds.Kql,
    "The KQL text language identifier should normalize for persistence.");

// The SVG handed to the renderer is rewritten around its blind spots. An image's own
// clip-path is hoisted onto a group around it, with its transform, so the clip lands
// where the author put it (issue 98); letter-spacing comes off text that is anchored at
// its middle or end, which the renderer would otherwise pile up in half its width.
// Markup with nothing to rewrite is passed through untouched.
{
    byte[] clippedSvg = Encoding.UTF8.GetBytes(
        "<svg xmlns=\"http://www.w3.org/2000/svg\"><defs><clipPath id=\"c\"><rect width=\"1\" height=\"1\"/></clipPath></defs>" +
        "<image x=\"1\" clip-path=\"url(#c)\" transform=\"scale(2)\" href=\"data:image/png;base64,AA==\"/><rect width=\"2\" height=\"2\"/></svg>");
    string hoisted = Encoding.UTF8.GetString(SvgMarkup.Rewrite(clippedSvg));
    Assert(
        hoisted.Contains("<g clip-path=\"url(#c)\" transform=\"scale(2)\"><image x=\"1\" href=\"data:image/png;base64,AA==\" /></g>", StringComparison.Ordinal) &&
        hoisted.Contains("<rect width=\"2\" height=\"2\" />", StringComparison.Ordinal),
        "An image's clip-path and transform move to a group around it; the rest is untouched.");
    byte[] plainSvg = Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><image href=\"data:image/png;base64,AA==\"/></svg>");
    Assert(ReferenceEquals(SvgMarkup.Rewrite(plainSvg), plainSvg), "Markup with no clipped image is the same bytes.");
    byte[] brokenSvg = Encoding.UTF8.GetBytes("<svg><image clip-path='u'");
    Assert(ReferenceEquals(SvgMarkup.Rewrite(brokenSvg), brokenSvg), "Markup that does not parse is left for the renderer.");

    byte[] spacedSvg = Encoding.UTF8.GetBytes(
        "<svg xmlns=\"http://www.w3.org/2000/svg\">" +
        "<g text-anchor=\"middle\"><text x=\"1\" letter-spacing=\"0.8\">A</text><text letter-spacing=\"0\">B</text></g>" +
        "<text style=\"fill:red; text-anchor : end\"><tspan letter-spacing=\"1\">C</tspan></text>" +
        "<text text-anchor=\"middle\" letter-spacing=\"0.1em\">D</text>" +
        "<text letter-spacing=\"1.5\">E</text>" +
        "<g text-anchor=\"middle\"><text text-anchor=\"start\" letter-spacing=\"1.5\">F</text></g></svg>");
    string unspaced = Encoding.UTF8.GetString(SvgMarkup.Rewrite(spacedSvg));
    Assert(
        unspaced.Contains("<text x=\"1\">A</text>", StringComparison.Ordinal) &&
        unspaced.Contains("<tspan>C</tspan>", StringComparison.Ordinal),
        "Letter-spacing comes off text whose anchor, inherited or in a style, is middle or end.");
    Assert(
        unspaced.Contains("<text letter-spacing=\"0\">B</text>", StringComparison.Ordinal) &&
        unspaced.Contains("<text text-anchor=\"middle\" letter-spacing=\"0.1em\">D</text>", StringComparison.Ordinal) &&
        unspaced.Contains("<text letter-spacing=\"1.5\">E</text>", StringComparison.Ordinal) &&
        unspaced.Contains("<text text-anchor=\"start\" letter-spacing=\"1.5\">F</text>", StringComparison.Ordinal),
        "Spacing the renderer ignores, and spacing on start-anchored text, is kept.");
    byte[] startSpacedSvg = Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><text letter-spacing=\"2\">E</text></svg>");
    Assert(ReferenceEquals(SvgMarkup.Rewrite(startSpacedSvg), startSpacedSvg), "Start-anchored spaced text leaves the bytes as they were.");
}

// Export areas: the board is cut only where it is empty, a container keeps its
// linked ink, a bridging stroke glues its neighbours, and the two orders differ.
{
    var exportBoard = new BoardDocument();
    exportBoard.AddAsset(new BoardAsset("model", "Contoso model.png", "image/png", [1, 2, 3]));
    var exportImage = new ImageBoardObject(Guid.NewGuid(), 0, new RectD(0, 0, 400, 300), "model");
    exportBoard.AddObject(exportImage);
    exportBoard.AddObject(ExportStroke(50, 50, 100, 60, 1, exportImage.Id));
    exportBoard.AddObject(ExportStroke(2000, 0, 200, 100, 2));
    exportBoard.AddObject(ExportStroke(2050, 150, 200, 100, 3));

    var exportAreas = BoardPartitioner.Partition(exportBoard);
    Assert(exportAreas.Count == 2, "Two clusters farther apart than the gap threshold give two areas.");
    Assert(
        exportAreas[0].Objects.Count == 2 && exportAreas[0].Objects.Contains(exportImage),
        "A container and its linked stroke stay in one area.");
    Assert(exportAreas[0].Title == "Contoso model", "An area is named after its dominant container.");
    Assert(exportAreas[1].Title is null && exportAreas[1].Objects.Count == 2, "Free strokes near each other share an area with no title.");
    Assert(exportAreas.All(area => !area.IsScaledDown), "Areas that fit are not marked as scaled.");

    var bridge = ExportStroke(300, 100, 1800, 20, 4);
    exportBoard.AddObject(bridge);
    Assert(
        BoardPartitioner.Partition(exportBoard).Count == 1,
        "A stroke that spans two clusters keeps them on one area.");
    exportBoard.RemoveObject(bridge.Id);

    exportBoard.AddObject(ExportStroke(0, 650, 150, 300, -1));
    var drawingOrder = BoardPartitioner.Partition(exportBoard);
    Assert(drawingOrder.Count == 3, "A third cluster below the first is its own area.");
    Assert(
        drawingOrder[0].Bounds.Top > 600 && drawingOrder[0].Number == 1,
        "Drawing order puts the area that was started first, by z-index, first.");
    var readingOrder = BoardPartitioner.Partition(
        exportBoard,
        ExportLayoutOptions.Default with { Order = AreaOrder.Reading });
    Assert(
        readingOrder[0].Objects.Contains(exportImage) &&
        readingOrder[1].Bounds.Top > 600 &&
        readingOrder[2].Bounds.Left > 1500,
        "Reading order walks the cuts: the left column top to bottom, then the right.");

    var wideThreshold = BoardPartitioner.Partition(
        exportBoard,
        ExportLayoutOptions.Default with { GapThreshold = 400 });
    Assert(wideThreshold.Count == 2, "Raising the threshold merges clusters separated by less than it.");

    var closeBoard = new BoardDocument();
    closeBoard.AddObject(ExportStroke(0, 0, 50, 50, 0));
    closeBoard.AddObject(ExportStroke(300, 0, 50, 50, 1));
    Assert(
        BoardPartitioner.Partition(closeBoard).Count == 1,
        "A region that already fits a slide is not cut, however wide its gaps.");

    var denseBoard = new BoardDocument();
    denseBoard.AddObject(ExportStroke(0, 0, 3000, 2000, 0));
    var dense = BoardPartitioner.Partition(denseBoard);
    Assert(
        dense.Count == 1 && dense[0].IsScaledDown && dense[0].TextScalePercent == 40,
        "An area that cannot be cut is scaled down and says by how much.");

    Assert(
        Math.Abs(ExportLayoutOptions.MaximumAreaWidthFor(12) - 1440) < 0.000001 &&
        Math.Abs(ExportLayoutOptions.MaximumAreaWidthFor(9) - 1920) < 0.000001,
        "Smallest text decides how wide an area may be.");
    Assert(BoardPartitioner.Partition(new BoardDocument()).Count == 0, "An empty board has no areas.");

    // Frames win: whatever sits inside one belongs to it, frames come first,
    // and the rest of the board is still cut automatically.
    var frame = new FrameBoardObject(Guid.NewGuid(), 50, new RectD(1900, -100, 800, 500), "Second cluster");
    exportBoard.AddObject(frame);
    var framed = BoardPartitioner.Partition(exportBoard);
    Assert(framed.Count == 3, "A frame around a cluster keeps the area count.");
    Assert(
        framed[0].Title == "Second cluster" && framed[0].Bounds == frame.Bounds && framed[0].Objects.Count == 2 &&
        framed[0].Objects.All(item => item.Bounds.Left > 1500),
        "The frame is the first area, with its own bounds and title and the objects inside it.");
    Assert(
        framed.Skip(1).All(area => area.Objects.All(item => item is not FrameBoardObject && item.Bounds.Left < 1500)),
        "Objects outside the frame are partitioned as before, and the frame itself is not an object of any area.");
    Assert(
        frame.HitTest(new PointD(1900, 150), 1) && !frame.HitTest(new PointD(2300, 150), 1) && frame.HitTest(new PointD(1950, -90), 1),
        "A frame is hit on its edge and on its tab, not inside.");
    Assert(
        exportBoard.HitTestTopContainer(new PointD(2100, 50), 1) is null &&
        exportBoard.HitTestTopContainer(new PointD(1900, 150), 1) is FrameBoardObject,
        "Hit testing reaches a frame only by its edge.");
    exportBoard.RemoveObject(frame.Id);

    var exportSettings = AppSettingsSerializer.Parse("""{ "export": { "gapThreshold": 9999, "smallestTextPoints": 11, "order": "Drawing" } }""");
    Assert(
        exportSettings.Export.GapThreshold == ExportLayoutOptions.MaximumGapThreshold &&
        exportSettings.Export.SmallestTextPoints == ExportLayoutOptions.DefaultSmallestTextPoints &&
        exportSettings.Export.Order == AreaOrder.Drawing,
        "Export settings are clamped to what the dialog offers.");
    var freshExport = new AppSettings().Export;
    Assert(
        freshExport.Format == ExportFormat.PowerPoint &&
        freshExport.PageModel == ExportPageModel.OnePerArea &&
        freshExport.Order == AreaOrder.Reading &&
        freshExport.SlideAspect == ExportSlideAspect.Wide &&
        freshExport.SlideContent == ExportSlideContent.Editable,
        "A new setup exports an editable 16:9 deck, one slide per area, in reading order.");
}

// PowerPoint deck: one slide per page, a notes slide only where there are notes,
// and a package that opens as a ZIP with the parts where PowerPoint looks for them.
{
    byte[] onePixelPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82,
    ];
    ExportPage[] deckPages =
    [
        new("Overview", "1. Contoso model\n2. Area 2", onePixelPng, 1600, 900),
        new("Contoso model", null, onePixelPng, 1200, 900),
        new("Area 2 <&>", "Sales Amount :=\nSUM ( Sales[Amount] )", onePixelPng, 3840, 1000),
    ];
    using var deckStream = new MemoryStream();
    PptxDeckWriter.Write(deckStream, deckPages, new DeckOptions(SlideAspect.Wide));
    deckStream.Position = 0;
    using (var deck = new ZipArchive(deckStream, ZipArchiveMode.Read, leaveOpen: true))
    {
        Assert(
            deck.Entries.Count(entry =>
                entry.FullName.StartsWith("ppt/slides/slide", StringComparison.Ordinal) &&
                entry.FullName.EndsWith(".xml", StringComparison.Ordinal)) == 3,
            "The deck has one slide per page.");
        Assert(
            deck.Entries.Count(entry =>
                entry.FullName.StartsWith("ppt/notesSlides/notesSlide", StringComparison.Ordinal) &&
                entry.FullName.EndsWith(".xml", StringComparison.Ordinal)) == 2,
            "Only pages with notes get a notes slide.");
        Assert(
            deck.Entries.Count(entry => entry.FullName.StartsWith("ppt/media/", StringComparison.Ordinal)) == 3,
            "Every slide carries its picture.");
        Assert(
            deck.GetEntry("ppt/presentation.xml") is not null && deck.GetEntry("[Content_Types].xml") is not null,
            "The deck is a presentation package.");
    }

    using var standardStream = new MemoryStream();
    PptxDeckWriter.Write(standardStream, deckPages[..1], new DeckOptions(SlideAspect.Standard));
    Assert(standardStream.Length > 0, "A 4:3 deck is written too.");

    // An editable slide: the picture gives way to a picture per image, a text
    // box per text container, and the ink overlay on top.
    SlideElement[] elements =
    [
        new SlideImageElement(new SlideRect(100, 100, 400, 300), onePixelPng, "image/png"),
        new SlideTextElement(
            new SlideRect(600, 100, 500, 200),
            "DAX Code of Sales Amount",
            [
                new SlideTextRun("Sales Amount", 0xFF202020, true, false),
                new SlideTextRun(" :=\n", 0xFF5E6470, false, false),
                new SlideTextRun("SUM", 0xFF035ACA, true, false),
                new SlideTextRun(" ( Sales[Amount] )\n\n-- done", 0xFF333333, false, true),
            ],
            "Consolas",
            13,
            18,
            10,
            0xFFFCFCFC,
            0xFFD6D9DE,
            0xFF1F2937),
        new SlideImageElement(new SlideRect(0, 0, 1600, 900), onePixelPng, "image/png"),
    ];
    using var editableStream = new MemoryStream();
    PptxDeckWriter.Write(
        editableStream,
        [new ExportPage("Editable", null, onePixelPng, 1600, 900, elements)],
        new DeckOptions());
    editableStream.Position = 0;
    using (var editable = new ZipArchive(editableStream, ZipArchiveMode.Read, leaveOpen: true))
    {
        using var reader = new StreamReader(editable.GetEntry("ppt/slides/slide1.xml")!.Open());
        var slideXml = reader.ReadToEnd();
        Assert(
            slideXml.Split("<p:pic>").Length - 1 == 2 && slideXml.Split("<p:sp>").Length - 1 == 2,
            "An editable slide holds two pictures, the title, and one text box.");
        Assert(
            slideXml.Contains("Sales Amount", StringComparison.Ordinal) &&
            slideXml.Contains("035ACA", StringComparison.Ordinal) &&
            slideXml.Contains("Consolas", StringComparison.Ordinal),
            "Text runs keep their words, colors, and typeface.");
        Assert(
            editable.Entries.Count(entry => entry.FullName.StartsWith("ppt/media/", StringComparison.Ordinal)) == 2,
            "Each picture element has its own image part.");
    }

    // Shapes and labels go out as objects rather than as pixels. What is checked is
    // the mapping a reader meets: one preset geometry per kind, the two rounded
    // rectangles told apart by their corner adjust, a fill that keeps its
    // translucency, and a label turned about its own centre with its style on it.
    SlideElement[] designElements =
    [
        // The block arrow is turned, as a shape on the board can be: it goes out
        // as the same preset with a:xfrm rot, exactly as the label below does.
        .. Enum.GetValues<ShapeKind>().Select((kind, index) => new SlideShapeElement(
            new SlideRect(40 + (index * 190), 60, 160, 120),
            kind,
            0xFF035ACA,
            kind == ShapeKind.Ellipse ? ShapeSettings.Tint(0xFFE64B3D) : null,
            6,
            kind == ShapeKind.BlockArrow ? 90 : 0)),
        new SlideLabelElement(
            new SlideRect(200, 500, 420, 120),
            45,
            "Design objects\nas objects",
            "Georgia",
            32,
            0xFF1F2937,
            Bold: true,
            Italic: false,
            Underline: true),
        new SlideConnectorElement(
            new SlideRect(700, 500, 400, 120),
            ConnectorKind.Arrow,
            new SlidePosition(1100, 620),
            new SlidePosition(700, 500),
            null,
            null,
            0xFF035ACA,
            6),
        new SlideConnectorElement(
            new SlideRect(700, 700, 400, 200),
            ConnectorKind.CurvedArrow,
            new SlidePosition(700, 700),
            new SlidePosition(1100, 900),
            new SlidePosition(860, 700),
            new SlidePosition(940, 900),
            0xFF035ACA,
            6),

        // Two side midpoints facing each other: every point of the cubic is on one
        // line, which is the box with no height at all.
        new SlideConnectorElement(
            new SlideRect(200, 860, 400, 0),
            ConnectorKind.CurvedArrow,
            new SlidePosition(200, 860),
            new SlidePosition(600, 860),
            new SlidePosition(360, 860),
            new SlidePosition(440, 860),
            0xFF035ACA,
            6),
    ];
    ExportPage[] designPages = [new ExportPage("Design", null, onePixelPng, 1600, 900, designElements)];

    using var designStream = new MemoryStream();
    PptxDeckWriter.Write(designStream, designPages, new DeckOptions());
    designStream.Position = 0;
    using (var design = new ZipArchive(designStream, ZipArchiveMode.Read, leaveOpen: true))
    {
        using var reader = new StreamReader(design.GetEntry("ppt/slides/slide1.xml")!.Open());
        var slideXml = reader.ReadToEnd();
        foreach (var preset in new[] { "roundRect", "ellipse", "triangle", "pentagon", "rightArrow", "parallelogram", "diamond" })
        {
            Assert(
                slideXml.Contains($"prst=\"{preset}\"", StringComparison.Ordinal),
                $"A shape should go out as the {preset} preset.");
        }

        Assert(
            slideXml.Split("<p:sp>").Length - 1 == Enum.GetValues<ShapeKind>().Length + 4,
            "Every shape, the label, the two curved connectors, and the title are shapes on the slide.");
        Assert(
            slideXml.Contains("fmla=\"val 20000\"", StringComparison.Ordinal) &&
            slideXml.Contains("fmla=\"val 50000\"", StringComparison.Ordinal),
            "A rounded rectangle keeps its corner, and a stadium takes the whole of its side.");
        Assert(
            slideXml.Contains("<a:alpha val=\"25098\" />", StringComparison.Ordinal) &&
            slideXml.Contains("<a:noFill />", StringComparison.Ordinal),
            "A tinted fill is seen through, and None is no fill at all.");
        Assert(
            slideXml.Contains("rot=\"2700000\"", StringComparison.Ordinal) &&
            slideXml.Contains("Design objects", StringComparison.Ordinal) &&
            slideXml.Contains("as objects", StringComparison.Ordinal),
            "A label is turned about its centre and holds its lines as text.");
        Assert(
            slideXml.Contains("rot=\"5400000\"", StringComparison.Ordinal) &&
            slideXml.Split("rot=\"").Length - 1 == 2,
            "A turned shape carries its angle too, and an upright one says nothing about one.");
        Assert(
            slideXml.Contains("u=\"sng\"", StringComparison.Ordinal) &&
            slideXml.Contains("b=\"1\"", StringComparison.Ordinal) &&
            slideXml.Contains("Georgia", StringComparison.Ordinal),
            "The label's style and typeface travel with it.");
        Assert(
            slideXml.Split("<p:cxnSp>").Length - 1 == 1 &&
            slideXml.Contains("prst=\"straightConnector1\"", StringComparison.Ordinal) &&
            slideXml.Split("<a:cubicBezTo>").Length - 1 == 2,
            "A straight connector is a connector with the preset, and a curved one is a freeform with its own cubic.");
        Assert(
            slideXml.Contains("flipH=\"1\"", StringComparison.Ordinal) &&
            slideXml.Contains("flipV=\"1\"", StringComparison.Ordinal) &&
            slideXml.Split("<a:tailEnd").Length - 1 == 3,
            "A connector that runs right to left and bottom to top is flipped, and every arrow has a head.");
        Assert(
            !slideXml.Contains("<a:path w=\"0\"", StringComparison.Ordinal) &&
            !slideXml.Contains("h=\"0\">", StringComparison.Ordinal),
            "A flat curve still has a path to scale against.");
    }

    // The same elements as a vector page, and a label in each font a board offers:
    // the faces are read from Windows and embedded, so the words stay words. Only
    // Cascadia Mono is written in another face, because Windows ships it as a
    // variable font with no bold or italic file of its own.
    using var designPdfStream = new MemoryStream();
    PdfDocumentWriter.Write(designPdfStream, designPages, new PdfOptions(BoardName: "Contoso workshop"));
    designPdfStream.Position = 0;
    using (var designPdf = PdfSharp.Pdf.IO.PdfReader.Open(designPdfStream, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))
    {
        Assert(designPdf.PageCount == 1, "The design page is one page.");
    }

    // One document per font, so that the face a label was written in is the only
    // one the document could have got it from: a page holding all nine would name
    // Arial for its own sake and answer for every family that falls back to it.
    foreach (var font in LabelStyles.Fonts)
    {
        using var fontStream = new MemoryStream();
        PdfDocumentWriter.Write(
            fontStream,
            [
                new ExportPage(
                    font,
                    null,
                    onePixelPng,
                    1600,
                    900,
                    [
                        new SlideLabelElement(
                            new SlideRect(60, 40, 900, 70),
                            0,
                            $"The quick brown fox in {font}",
                            font,
                            36,
                            0xFF1F2937,
                            Bold: false,
                            Italic: false,
                            Underline: false),
                    ]),
            ],
            new PdfOptions(Footer: false));

        // A base font is named after the family, with #20 where a space is. A
        // Windows without the family embeds the stand-in the resolver names for it,
        // so either answer is the resolver working; what is ruled out is the label
        // quietly coming out in the page's own face instead.
        var face = (font == "Cascadia Mono" ? "Consolas" : font).Replace(" ", "#20", StringComparison.Ordinal);
        var standIn = font switch
        {
            "Georgia" or "Times New Roman" => "Times#20New#20Roman",
            "Consolas" or "Cascadia Mono" => "Courier#20New",
            _ => "Arial",
        };
        var fontBytes = System.Text.Encoding.Latin1.GetString(fontStream.ToArray());
        Assert(
            fontBytes.Contains($"+{face}", StringComparison.Ordinal) ||
            fontBytes.Contains($"+{standIn}", StringComparison.Ordinal),
            $"A label in {font} should be written in {face}, or in {standIn} where Windows has no such family.");
    }

    // The same pages as a PDF: one page each, a bookmark each, and the page
    // size following the picture when asked.
    using var pdfStream = new MemoryStream();
    PdfDocumentWriter.Write(pdfStream, deckPages, new PdfOptions(PdfPageSize.A4, Landscape: true, Footer: true, BoardName: "Contoso workshop"));
    pdfStream.Position = 0;
    using (var pdf = PdfSharp.Pdf.IO.PdfReader.Open(pdfStream, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))
    {
        Assert(pdf.PageCount == 3, "The PDF has one page per export page.");
        Assert(
            Math.Abs(pdf.Pages[0].Width.Point - 841.9) < 1 && Math.Abs(pdf.Pages[0].Height.Point - 595.3) < 1,
            "A4 landscape pages are 842 by 595 points.");
        Assert(pdf.Outlines.Count == 3, "Every page has a bookmark.");
    }

    // A vector page: the same elements as the editable slide, with the ink as
    // strokes rather than a picture, drawn as paths and text.
    SlidePoint[] sine = Enumerable.Range(0, 40)
        .Select(i => new SlidePoint(100 + (i * 20), 600 + (80 * Math.Sin(i / 4.0)), (float)(0.2 + (0.6 * Math.Abs(Math.Sin(i / 3.0))))))
        .ToArray();
    SlideElement[] vectorElements =
    [
        elements[0],
        elements[1],
        new SlideInkElement(
            new SlideRect(0, 0, 1600, 900),
            [
                new SlideStroke(sine, 0xFFE64B3D, 4, SlideStrokeKind.Pen),
                new SlideStroke([new SlidePoint(580, 150, 0.5f), new SlidePoint(1120, 160, 0.5f)], 0xFFFACC15, 6, SlideStrokeKind.Highlighter),
                new SlideStroke([new SlidePoint(200, 300, 0.3f), new SlidePoint(300, 260, 0.8f), new SlidePoint(420, 330, 0.5f)], 0xFF1F2937, 4, SlideStrokeKind.Calligraphy),
                new SlideStroke([new SlidePoint(50, 50, 0.5f)], 0xFF1F2937, 8, SlideStrokeKind.Pen),
            ]),
    ];
    using var vectorStream = new MemoryStream();
    PdfDocumentWriter.Write(
        vectorStream,
        [new ExportPage("Vector", null, onePixelPng, 1600, 900, vectorElements)],
        new PdfOptions(BoardName: "Contoso workshop"));
    var vectorBytes = vectorStream.ToArray();
    Assert(
        System.Text.Encoding.Latin1.GetString(vectorBytes).Contains("Consolas", StringComparison.Ordinal),
        "A vector page embeds the text's font, so the text is text.");
    vectorStream.Position = 0;
    using (var vector = PdfSharp.Pdf.IO.PdfReader.Open(vectorStream, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))
    {
        Assert(vector.PageCount == 1, "A vector page is one page.");
    }

    using var posterStream = new MemoryStream();
    PdfDocumentWriter.Write(posterStream, deckPages[..1], new PdfOptions(FitPageToPicture: true));
    posterStream.Position = 0;
    using (var poster = PdfSharp.Pdf.IO.PdfReader.Open(posterStream, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))
    {
        Assert(
            poster.PageCount == 1 && Math.Abs(poster.Pages[0].Width.Point - 800) < 1 && Math.Abs(poster.Pages[0].Height.Point - 450) < 1,
            "A whole-board page takes the picture's aspect at 144 dpi.");
    }
}

// The save point is what tells a board that matches its file from one that does not, and
// every one of these is a case somebody hits in an afternoon's drawing.
{
    var savedDocument = new BoardDocument();
    var savedHistory = new CommandHistory();
    Assert(savedHistory.IsAtSavePoint, "A board nobody has drawn on matches the empty file it is not.");

    savedHistory.Execute(new AddObjectCommand(ExportStroke(0, 0, 10, 10, 0)), savedDocument);
    Assert(!savedHistory.IsAtSavePoint, "A stroke drawn before any save must count as a change.");

    savedHistory.MarkSaved();
    Assert(savedHistory.IsAtSavePoint, "Saving must make the board match the file.");

    savedHistory.Undo(savedDocument);
    Assert(
        !savedHistory.IsAtSavePoint,
        "Undoing after a save rolls back what the file holds, so it must count as a change.");

    savedHistory.Redo(savedDocument);
    Assert(savedHistory.IsAtSavePoint, "Redoing back onto the save point must match the file again.");

    savedHistory.Execute(new AddObjectCommand(ExportStroke(20, 20, 10, 10, 1)), savedDocument);
    Assert(!savedHistory.IsAtSavePoint, "A stroke drawn after a save must count as a change.");

    savedHistory.Undo(savedDocument);
    Assert(
        savedHistory.IsAtSavePoint,
        "Undoing that stroke returns to what was saved, so it must stop counting as a change.");

    // Same depth, different history: the save point is the command, never the count.
    savedHistory.Execute(new AddObjectCommand(ExportStroke(40, 40, 10, 10, 2)), savedDocument);
    Assert(
        !savedHistory.IsAtSavePoint,
        "A different action at the depth that was saved is still a change.");

    savedHistory.Clear();
    Assert(savedHistory.IsAtSavePoint, "Clearing the history puts the save point at the empty board.");
}

// A snapshot is what autosave writes while the board carries on being drawn, so it has to
// hold what the document held and stop hearing about it afterwards.
{
    var snapshotDocument = new BoardDocument();
    snapshotDocument.AddAsset(new BoardAsset("asset-1", "picture.png", "image/png", [1, 2, 3]));
    snapshotDocument.AddObject(ExportStroke(0, 0, 10, 10, 3));
    snapshotDocument.AddObject(ExportStroke(30, 30, 10, 10, 1));

    var snapshot = snapshotDocument.Snapshot();
    Assert(snapshot.Objects.Count == 2, "A snapshot keeps every object.");
    Assert(snapshot.Assets.Count == 1, "A snapshot keeps every asset.");
    Assert(
        snapshot.Objects[0].ZIndex == 1 && snapshot.Objects[1].ZIndex == 3,
        "A snapshot keeps the document's z order.");

    snapshotDocument.AddObject(ExportStroke(60, 60, 10, 10, 4));
    Assert(
        snapshot.Objects.Count == 2,
        "A stroke drawn after the snapshot must not reach the copy being written.");
}

// The session sidecar is read while the application is starting, so nothing it can contain
// is allowed to be what stops it.
{
    var state = new SessionState
    {
        BoardPath = @"C:\decks\demo.wboard",
        Modified = true,
        ExitedCleanly = false,
        CameraCenterX = 120.5,
        CameraCenterY = -40.25,
        CameraZoom = 2.5,
    };

    var parsed = SessionState.Parse(SessionState.Format(state));
    Assert(parsed is not null, "A sidecar this version wrote must read back.");
    Assert(parsed!.BoardPath == state.BoardPath, "A sidecar keeps the board it was editing.");
    Assert(parsed.Modified && !parsed.ExitedCleanly, "A sidecar keeps how the session ended.");
    AssertNear(120.5, parsed.CameraCenterX, "A sidecar keeps where the camera was.");
    AssertNear(2.5, parsed.CameraZoom, "A sidecar keeps the zoom.");

    Assert(SessionState.Parse("{ not json") is null, "Damaged sidecars are skipped, not thrown over.");
    Assert(SessionState.Parse(string.Empty) is null, "An empty sidecar is skipped.");
    Assert(
        SessionState.Parse("{\"version\":9999}") is null,
        "A sidecar from a later version is skipped rather than half understood.");
    Assert(
        SessionState.Parse("{\"version\":1,\"boardPath\":\"  \"}")?.BoardPath is null,
        "A blank board path reads as no board path.");
}

// A restored camera comes from a file, so what it carries is checked rather than trusted.
{
    var restored = new Camera2D();
    restored.Resize(1000, 800);
    restored.Restore(new PointD(50, -30), 3);
    AssertNear(50, restored.Center.X, "A restored camera takes the center it was given.");
    AssertNear(3, restored.Zoom, "A restored camera takes the zoom it was given.");

    restored.Restore(new PointD(0, 0), 9999);
    AssertNear(Camera2D.MaximumZoom, restored.Zoom, "A restored zoom is clamped, not trusted.");

    restored.Restore(new PointD(0, 0), 0);
    AssertNear(Camera2D.MinimumZoom, restored.Zoom, "A zero zoom clamps rather than blanking the board.");

    restored.Restore(new PointD(10, 10), 2);
    restored.Restore(new PointD(double.NaN, 0), 2);
    AssertNear(10, restored.Center.X, "A sidecar carrying NaN leaves the camera where it was.");
}

// Area selection: what a rubber band and a lasso take, under both rules.
{
    var areaBoard = new BoardDocument();
    var picture = new ImageBoardObject(
        Guid.NewGuid(),
        areaBoard.NextZIndex,
        new RectD(100, 100, 100, 100),
        "area-picture");
    areaBoard.AddObject(picture);
    var crossingStroke = InkStrokeObject.Create(
        [
            new InkPoint(new PointD(150, 150), 0.5f, 0),
            new InkPoint(new PointD(400, 150), 0.5f, 1),
        ],
        PenStyle.Default,
        areaBoard.NextZIndex);
    areaBoard.AddObject(crossingStroke);
    var farStroke = InkStrokeObject.Create(
        [
            new InkPoint(new PointD(600, 600), 0.5f, 0),
            new InkPoint(new PointD(650, 650), 0.5f, 1),
        ],
        PenStyle.Default,
        areaBoard.NextZIndex);
    areaBoard.AddObject(farStroke);
    var slide = new FrameBoardObject(
        Guid.NewGuid(),
        areaBoard.NextZIndex,
        new RectD(0, 0, 900, 900),
        "Slide 1");
    areaBoard.AddObject(slide);

    var band = SelectionArea.Rectangle(new RectD(50, 50, 250, 250));
    HashSet<Guid> partly = areaBoard.ObjectsInArea(band, AreaSelection.PartlyInside)
        .Select(item => item.Id)
        .ToHashSet();
    Assert(
        partly.SetEquals(new[] { picture.Id, crossingStroke.Id }),
        "A band takes what it meets, and leaves what it does not.");
    Assert(
        !partly.Contains(slide.Id),
        "A frame is never taken by an area: the band means what is on the slide.");
    HashSet<Guid> fully = areaBoard.ObjectsInArea(band, AreaSelection.FullyInside)
        .Select(item => item.Id)
        .ToHashSet();
    Assert(
        fully.SetEquals(new[] { picture.Id }),
        "Fully inside drops the stroke that leaves the band, and keeps the picture inside it.");

    Assert(
        areaBoard.ObjectsInArea(
            SelectionArea.Rectangle(new RectD(500, 500, 400, 400)),
            AreaSelection.FullyInside)
            .Select(item => item.Id)
            .SequenceEqual([farStroke.Id]),
        "A band around a whole stroke takes it under either rule.");

    // The same two questions asked of a lasso, drawn as a diamond around the
    // same corner of the board.
    var lasso = SelectionArea.Lasso(
    [
        new PointD(175, 20),
        new PointD(330, 175),
        new PointD(175, 330),
        new PointD(20, 175),
    ]);
    Assert(
        areaBoard.ObjectsInArea(lasso, AreaSelection.PartlyInside)
            .Select(item => item.Id)
            .ToHashSet()
            .SetEquals(new[] { picture.Id, crossingStroke.Id }),
        "A lasso takes what its outline encloses or crosses.");
    Assert(
        areaBoard.ObjectsInArea(lasso, AreaSelection.FullyInside)
            .Select(item => item.Id)
            .SequenceEqual([picture.Id]),
        "Fully inside a lasso is every corner inside the outline.");
    Assert(
        !Polygon.Contains(
            [new PointD(175, 20), new PointD(330, 175), new PointD(175, 330), new PointD(20, 175)],
            new PointD(340, 340)),
        "A point outside the outline is outside by the even-odd rule.");
    Assert(
        Polygon.RectangleIntersectsSegment(
            new RectD(0, 0, 10, 10),
            new PointD(-5, 5),
            new PointD(15, 5)) &&
        !Polygon.RectangleIntersectsSegment(
            new RectD(0, 0, 10, 10),
            new PointD(-5, 50),
            new PointD(15, 50)),
        "A segment through a box meets it, and one well past it does not.");
}

// Extend to touching, including the ring that would otherwise circle forever.
{
    var chain = new BoardDocument();
    var first = new ImageBoardObject(Guid.NewGuid(), chain.NextZIndex, new RectD(0, 0, 100, 100), "chain-1");
    var second = new ImageBoardObject(Guid.NewGuid(), chain.NextZIndex, new RectD(90, 0, 100, 100), "chain-2");
    var third = new ImageBoardObject(Guid.NewGuid(), chain.NextZIndex, new RectD(180, 0, 100, 100), "chain-3");
    var apart = new ImageBoardObject(Guid.NewGuid(), chain.NextZIndex, new RectD(600, 0, 100, 100), "chain-apart");
    chain.AddObject(first);
    chain.AddObject(second);
    chain.AddObject(third);
    chain.AddObject(apart);

    Assert(
        chain.GrowSelection([first.Id], ExtendSelection.Ignore).SequenceEqual([first.Id]),
        "Ignore takes the area at its word.");
    Assert(
        chain.GrowSelection([first.Id], ExtendSelection.Single)
            .ToHashSet()
            .SetEquals(new[] { first.Id, second.Id }),
        "Single adds one round of what touches the selection.");
    HashSet<Guid> recursive = chain.GrowSelection([first.Id], ExtendSelection.Recursive).ToHashSet();
    Assert(
        recursive.SetEquals(new[] { first.Id, second.Id, third.Id }),
        "Recursive walks the chain and stops where it ends.");
    Assert(
        !recursive.Contains(apart.Id),
        "Nothing the chain does not reach joins it.");

    // A ring: every one of these touches the next, and the last touches the
    // first. The visited set is what makes this terminate at all.
    var ring = new BoardDocument();
    var ringIds = new List<Guid>();
    for (var index = 0; index < 4; index++)
    {
        var corner = new ImageBoardObject(
            Guid.NewGuid(),
            ring.NextZIndex,
            new RectD(index is 1 or 2 ? 90 : 0, index is 2 or 3 ? 90 : 0, 100, 100),
            $"ring-{index}");
        ring.AddObject(corner);
        ringIds.Add(corner.Id);
    }

    Assert(
        ring.GrowSelection([ringIds[0]], ExtendSelection.Recursive).ToHashSet().SetEquals(ringIds),
        "A ring of touching objects is taken whole, and examined once each.");
}

// A group gesture: the selection moves and scales as one, and the strokes
// linked to a selected container come along without being selected themselves.
{
    var groupBoard = new BoardDocument();
    var groupPicture = new ImageBoardObject(
        Guid.NewGuid(),
        groupBoard.NextZIndex,
        new RectD(0, 0, 100, 100),
        "group-picture");
    groupBoard.AddObject(groupPicture);
    var groupLinked = InkStrokeObject.Create(
        [
            new InkPoint(new PointD(20, 20), 0.5f, 0),
            new InkPoint(new PointD(80, 80), 0.5f, 1),
        ],
        PenStyle.Default,
        groupBoard.NextZIndex,
        containerId: groupPicture.Id);
    groupBoard.AddObject(groupLinked);
    var groupStroke = InkStrokeObject.Create(
        [
            new InkPoint(new PointD(200, 200), 0.5f, 0),
            new InkPoint(new PointD(300, 300), 0.5f, 1),
        ],
        PenStyle.Default,
        groupBoard.NextZIndex);
    groupBoard.AddObject(groupStroke);

    var groupBounds = new RectD(0, 0, 300, 300);
    var moved = groupBounds.Translate(new PointD(500, 50));
    BoardObject[] movedBefore = [groupPicture, groupStroke, groupLinked];
    BoardObject[] movedAfter =
    [
        groupPicture.WithBounds(new RectD(500, 50, 100, 100)),
        groupStroke.TransformWithContainer(groupBounds, moved),
        groupLinked.TransformWithContainer(groupBounds, moved),
    ];
    var groupHistory = new CommandHistory();
    groupHistory.Execute(new ReplaceObjectsCommand(movedBefore, movedAfter), groupBoard);
    AssertNear(
        500,
        groupBoard.Objects.OfType<ImageBoardObject>().Single().Bounds.Left,
        "Moving the selection moves every member of it.");
    AssertNear(
        520,
        groupBoard.Objects.OfType<InkStrokeObject>()
            .Single(item => item.ContainerId == groupPicture.Id)
            .Points[0]
            .Position
            .X,
        "A stroke linked to a selected container is carried by the same move.");

    groupHistory.Undo(groupBoard);
    AssertNear(
        0,
        groupBoard.Objects.OfType<ImageBoardObject>().Single().Bounds.Left,
        "Undo puts the whole group back.");
    AssertNear(
        20,
        groupBoard.Objects.OfType<InkStrokeObject>()
            .Single(item => item.ContainerId == groupPicture.Id)
            .Points[0]
            .Position
            .X,
        "Undo puts the linked strokes back too.");

    // Scaled about the top-left corner, aspect preserved.
    var scaled = new RectD(0, 0, 600, 600);
    BoardObject[] scaledAfter =
    [
        groupPicture.WithBounds(new RectD(0, 0, 200, 200)),
        groupStroke.TransformWithContainer(groupBounds, scaled),
        groupLinked.TransformWithContainer(groupBounds, scaled),
    ];
    groupHistory.Execute(new ReplaceObjectsCommand(movedBefore, scaledAfter), groupBoard);
    AssertNear(
        200,
        groupBoard.Objects.OfType<ImageBoardObject>().Single().Bounds.Width,
        "Scaling the selection scales every member about the same corner.");
    AssertNear(
        400,
        groupBoard.Objects.OfType<InkStrokeObject>()
            .Single(item => item.ContainerId is null)
            .Points[0]
            .Position
            .X,
        "A selected stroke is scaled by the group rather than by its own box.");

    // Deleting the set takes the linked strokes with it, whether or not they
    // were part of the set.
    Assert(
        groupBoard.GetDeletionGroup([groupPicture.Id, groupStroke.Id])
            .Select(item => item.Id)
            .ToHashSet()
            .SetEquals(new[] { groupPicture.Id, groupStroke.Id, groupLinked.Id }),
        "Deleting a set takes its members and the strokes linked to them.");

    // A block sent to the front keeps its own order.
    var ordered = new BoardDocument();
    var bottom = new ImageBoardObject(Guid.NewGuid(), ordered.NextZIndex, new RectD(0, 0, 10, 10), "z-1");
    ordered.AddObject(bottom);
    var middle = new ImageBoardObject(Guid.NewGuid(), ordered.NextZIndex, new RectD(20, 0, 10, 10), "z-2");
    ordered.AddObject(middle);
    var top = new ImageBoardObject(Guid.NewGuid(), ordered.NextZIndex, new RectD(40, 0, 10, 10), "z-3");
    ordered.AddObject(top);
    BoardObject[] block = ordered.GetDeletionGroup([bottom.Id, middle.Id])
        .OrderBy(item => item.ZIndex)
        .ToArray();
    var start = top.ZIndex + 1;
    ordered.ReplaceObjects(block.Select((item, index) => item.WithZIndex(start + index)).ToArray());
    Assert(
        ordered.Objects[0].Id == top.Id &&
        ordered.Objects[1].Id == bottom.Id &&
        ordered.Objects[2].Id == middle.Id,
        "A block brought to the front goes above everything and keeps its internal order.");
}

// The three Selection settings: what they round trip as, and what an invalid
// one normalizes to.
{
    var selectionSettings = AppSettingsSerializer.Parse(AppSettingsSerializer.Format(new AppSettings
    {
        AreaSelection = AreaSelection.FullyInside,
        ExtendSelection = ExtendSelection.Recursive,
        AreaSelectionTool = AreaSelectionTool.Lasso,
    }));
    Assert(
        selectionSettings.AreaSelection == AreaSelection.FullyInside &&
        selectionSettings.ExtendSelection == ExtendSelection.Recursive &&
        selectionSettings.AreaSelectionTool == AreaSelectionTool.Lasso,
        "The Selection settings survive a round trip.");
    Assert(
        AppSettingsSerializer.Parse("{ }") is
        {
            AreaSelection: AreaSelection.PartlyInside,
            ExtendSelection: ExtendSelection.Ignore,
            AreaSelectionTool: AreaSelectionTool.Rectangle,
        },
        "A file that says nothing about selection takes the defaults.");
    Assert(
        AppSettingsSerializer.Parse("{ \"areaSelection\": 99, \"extendSelection\": 99 }") is
        {
            AreaSelection: AreaSelection.PartlyInside,
            ExtendSelection: ExtendSelection.Ignore,
        },
        "A selection value that is not one of the choices normalizes to the default.");
    Assert(
        AppSettingsSerializer.Parse("{ }").Version == 19,
        "Settings are written as version 19.");
}

// Shapes: the outline every kind is made of, what a tap on one reaches, how a
// board carrying one is written, and what the next one is drawn with.
{
    var shapeBox = new RectD(0, 0, 100, 60);

    IReadOnlyList<PointD> rounded = ShapeGeometry.Outline(ShapeKind.RoundedRectangle, shapeBox);
    Assert(
        HasShapePoint(rounded, 12, 0) && HasShapePoint(rounded, 88, 0) && HasShapePoint(rounded, 100, 12),
        "A rounded rectangle's straight runs stop a corner radius short of each corner.");
    Assert(
        !HasShapePoint(rounded, 0, 0),
        "The corner itself is not on a rounded rectangle: that is what makes it rounded.");

    IReadOnlyList<PointD> ellipse = ShapeGeometry.Outline(ShapeKind.Ellipse, shapeBox);
    Assert(
        HasShapePoint(ellipse, 0, 30) && HasShapePoint(ellipse, 100, 30) &&
        HasShapePoint(ellipse, 50, 0) && HasShapePoint(ellipse, 50, 60),
        "An ellipse touches its box at the middle of each side.");
    Assert(
        ellipse.All(point => shapeBox.Inflate(0.000001).Contains(point)),
        "The ellipse is inscribed in the box rather than escaping it between the samples.");

    Assert(
        ShapeGeometry.Outline(ShapeKind.Triangle, shapeBox)
            .SequenceEqual([new PointD(50, 0), new PointD(100, 60), new PointD(0, 60)]),
        "A triangle is its apex and the two bottom corners of the box.");
    Assert(
        ShapeGeometry.Outline(ShapeKind.Diamond, shapeBox)
            .SequenceEqual([new PointD(50, 0), new PointD(100, 30), new PointD(50, 60), new PointD(0, 30)]),
        "A diamond is the middle of each side.");
    Assert(
        ShapeGeometry.Outline(ShapeKind.Parallelogram, shapeBox)
            .SequenceEqual([new PointD(25, 0), new PointD(100, 0), new PointD(75, 60), new PointD(0, 60)]),
        "A parallelogram slants by a quarter of its width.");
    Assert(
        ShapeGeometry.Outline(ShapeKind.BlockArrow, shapeBox)
            .SequenceEqual(
            [
                new PointD(0, 15), new PointD(60, 15), new PointD(60, 0), new PointD(100, 30),
                new PointD(60, 60), new PointD(60, 45), new PointD(0, 45),
            ]),
        "A block arrow's head begins at three fifths of the box and its shaft is half the height.");

    IReadOnlyList<PointD> pentagon = ShapeGeometry.Outline(ShapeKind.Pentagon, shapeBox);
    Assert(
        pentagon.Count == 5 && HasShapePoint(pentagon, 50, 0),
        "A pentagon has five points and one of them is the top of the box.");
    Assert(
        pentagon.Min(point => point.X) == 0 && pentagon.Max(point => point.X) == 100 &&
        pentagon.Max(point => point.Y) == 60,
        "The pentagon fills the box it was dragged out of rather than a circle inside it.");

    IReadOnlyList<PointD> stadium = ShapeGeometry.Outline(ShapeKind.Stadium, shapeBox);
    Assert(
        HasShapePoint(stadium, 30, 0) && HasShapePoint(stadium, 70, 0) && HasShapePoint(stadium, 100, 30),
        "A stadium wider than it is tall has straight sides and semicircular ends.");

    // The band along the outline, and nothing else: what is drawn inside a shape
    // has to stay reachable.
    var banded = ShapeBoardObject.Create(
        Guid.NewGuid(),
        0,
        shapeBox,
        ShapeKind.Ellipse,
        0xFF1F2937,
        null,
        4);
    Assert(banded.HitTest(new PointD(0, 30), 1), "A tap on the outline takes hold of the shape.");
    Assert(
        banded.HitTest(new PointD(3, 30), 1) && !banded.HitTest(new PointD(20, 30), 1),
        "The band reaches eight screen pixels in from the outline and no further.");
    Assert(!banded.HitTest(new PointD(50, 30), 1), "A tap in the middle of a shape is not the shape.");
    Assert(!banded.HitTest(new PointD(130, 30), 1), "A tap well outside the box is not the shape.");
    Assert(
        banded.HitTest(new PointD(-3, 30), 1) && !banded.HitTest(new PointD(-3, 30), 8),
        "The band is screen pixels, so zooming in narrows what it covers on the board.");

    // The area asks the outline too, so an empty corner of a shape's box is not
    // the shape.
    Assert(
        !banded.IsTakenBy(SelectionArea.Rectangle(new RectD(-5, -5, 15, 15)), AreaSelection.PartlyInside),
        "A band over the empty corner of an ellipse's box does not take the ellipse.");
    Assert(
        banded.IsTakenBy(SelectionArea.Rectangle(new RectD(-5, 25, 20, 10)), AreaSelection.PartlyInside),
        "A band that crosses the outline takes the shape.");
    Assert(
        banded.IsTakenBy(SelectionArea.Rectangle(new RectD(-10, -10, 120, 80)), AreaSelection.FullyInside),
        "An area around the whole shape takes it under the stricter rule as well.");
    Assert(
        !banded.IsTakenBy(SelectionArea.Rectangle(new RectD(-5, 25, 20, 10)), AreaSelection.FullyInside),
        "Under Only objects fully inside, every point of the outline has to be inside.");

    // A shape is a container, so ink that touches only it links to it.
    var shapeBoard = new BoardDocument();
    var linkedShape = ShapeBoardObject.Create(
        Guid.NewGuid(),
        shapeBoard.NextZIndex,
        new RectD(0, 0, 200, 120),
        ShapeKind.RoundedRectangle,
        0xFF1F2937,
        null,
        4);
    shapeBoard.AddObject(linkedShape);
    var shapeStroke = InkStrokeObject.Create(
        [new InkPoint(new PointD(40, 40), 0.5f, 0), new InkPoint(new PointD(160, 80), 0.5f, 1)],
        PenStyle.Default,
        shapeBoard.NextZIndex);
    Assert(
        shapeBoard.FindSingleTouchedContainer(shapeStroke)?.Id == linkedShape.Id,
        "A stroke that touches only a shape links to it, as it does to any other container.");

    // Version 7 is asked for by the board that needs it, and by nothing else.
    var shapeArchiveBoard = new BoardDocument();
    Assert(
        BoardArchive.VersionFor(shapeArchiveBoard) == BoardArchive.VersionBeforeFrames,
        "A board with neither a frame nor a design object is still written as version 5.");
    shapeArchiveBoard.AddObject(new FrameBoardObject(
        Guid.NewGuid(),
        shapeArchiveBoard.NextZIndex,
        new RectD(0, 0, 400, 300),
        "Slide"));
    Assert(
        BoardArchive.VersionFor(shapeArchiveBoard) == BoardArchive.VersionWithFrames,
        "A board with only frames stays on the version frames arrived in.");
    var filledShape = ShapeBoardObject.Create(
        Guid.NewGuid(),
        shapeArchiveBoard.NextZIndex,
        new RectD(10, 20, 300, 140),
        ShapeKind.Stadium,
        0xFF009E73,
        0x40CC79A7,
        8);
    shapeArchiveBoard.AddObject(filledShape);
    var hollowShape = ShapeBoardObject.Create(
        Guid.NewGuid(),
        shapeArchiveBoard.NextZIndex,
        new RectD(400, 20, 120, 120),
        ShapeKind.Pentagon,
        0xFFE64B3D,
        null,
        2);
    shapeArchiveBoard.AddObject(hollowShape);
    Assert(
        BoardArchive.VersionFor(shapeArchiveBoard) == BoardArchive.CurrentVersion,
        "A board that holds a shape asks for version 7.");

    await using var shapeArchive = new MemoryStream();
    await BoardArchive.SaveAsync(shapeArchiveBoard, shapeArchive);
    shapeArchive.Position = 0;
    var loadedShapes = await BoardArchive.LoadAsync(shapeArchive);
    Assert(
        loadedShapes.Objects.OfType<ShapeBoardObject>().Single(item => item.Id == filledShape.Id) == filledShape,
        "A filled shape round-trips with its kind, outline, fill, and thickness.");
    Assert(
        loadedShapes.Objects.OfType<ShapeBoardObject>().Single(item => item.Id == hollowShape.Id) == hollowShape,
        "A shape with no fill round-trips as one, rather than picking one up on the way.");

    // A kind a later release invented becomes a rounded rectangle, and the
    // fields a hand-written board leaves out take their defaults.
    var strangeId = Guid.NewGuid();
    await using var strangeArchive = new MemoryStream();
    using (var writer = new ZipArchive(strangeArchive, ZipArchiveMode.Create, leaveOpen: true))
    {
        var entry = writer.CreateEntry("scene.json");
        await using var entryStream = entry.Open();
        await using var text = new StreamWriter(entryStream, Encoding.UTF8);
        await text.WriteAsync(
            "{\"version\":7,\"objects\":[{\"type\":\"shape\",\"id\":\"" + strangeId +
            "\",\"zIndex\":0,\"bounds\":{\"x\":0,\"y\":0,\"width\":100,\"height\":60}," +
            "\"shapeKind\":\"Hexagon\"}],\"assets\":[]}");
    }

    strangeArchive.Position = 0;
    var loadedStrange = await BoardArchive.LoadAsync(strangeArchive);
    Assert(
        loadedStrange.Objects.OfType<ShapeBoardObject>().Single() is
        {
            Kind: ShapeKind.RoundedRectangle,
            FillArgb: null,
            Thickness: 4,
        },
        "An unknown shape kind reads as a rounded rectangle, and the missing fields take their defaults.");

    // The two Toolbar settings and the shape defaults.
    var insertSettings = AppSettingsSerializer.Parse(AppSettingsSerializer.Format(new AppSettings
    {
        InsertOnToolbar = true,
        AfterInsert = AfterInsert.ReturnToSelect,
        Shape = new ShapeSettings
        {
            OutlineArgb = 0xFF009E73,
            FillArgb = ShapeSettings.Tint(0xFFCC79A7),
            Thickness = 8,
        },
    }));
    Assert(
        insertSettings is { InsertOnToolbar: true, AfterInsert: AfterInsert.ReturnToSelect } &&
        insertSettings.Shape is { OutlineArgb: 0xFF009E73, Thickness: 8 } &&
        insertSettings.Shape.FillArgb == ShapeSettings.Tint(0xFFCC79A7),
        "The Toolbar settings and the shape defaults survive a round trip.");
    Assert(
        AppSettingsSerializer.Parse("{ }") is
        {
            InsertOnToolbar: false,
            AfterInsert: AfterInsert.KeepTool,
        },
        "A file that says nothing about the Insert toolbar takes the defaults.");
    Assert(
        AppSettingsSerializer.Parse(
            "{ \"afterInsert\": 99, \"shape\": { \"outlineArgb\": 123, \"fillArgb\": 456, \"thickness\": 99 } }") is
        {
            AfterInsert: AfterInsert.KeepTool,
            Shape: { FillArgb: null, Thickness: 4 },
        },
        "A shape default that is not on the palette normalizes back to one that is.");
}


// A shape turned about its own centre. What is stored is the box it was drawn
// in and the angle; the box the document indexes follows from them, and so do
// the outline, the hit band, the eight binding points, and what a corner handle
// does.
{
    var turnedBox = new RectD(0, 0, 100, 60);
    var flat = ShapeBoardObject.Create(
        Guid.NewGuid(),
        0,
        turnedBox,
        ShapeKind.RoundedRectangle,
        0xFF1F2937,
        null,
        4);
    Assert(
        flat is { AngleDegrees: 0, LayoutWidth: 100, LayoutHeight: 60 } && flat.Bounds == turnedBox,
        "A shape is drawn upright, in the box it was dragged out of.");

    var quarter = flat.WithAngle(90);
    AssertNear(60, quarter.Bounds.Width, "A quarter turn swaps the width for the height.");
    AssertNear(100, quarter.Bounds.Height, "A quarter turn swaps the height for the width.");
    AssertNear(50, quarter.Bounds.Center.X, "A turn is about the centre, which does not move.");
    AssertNear(30, quarter.Bounds.Center.Y, "A turn is about the centre on both axes.");
    AssertNear(100, quarter.LayoutWidth, "The box the shape was drawn in is kept through the turn.");
    AssertNear(
        0,
        quarter.WithAngle(quarter.AngleDegrees - 90).AngleDegrees,
        "Stepping back the other way comes home.");

    var askew = flat.WithAngle(45);
    AssertNear(
        160 / Math.Sqrt(2),
        askew.Bounds.Width,
        "At 45 degrees the box spans the two sides of the rectangle together.");
    AssertNear(askew.Bounds.Width, askew.Bounds.Height, "And that box is square.");

    // The outline is turned with the shape, so the band is where the edge is
    // drawn rather than where it was described.
    Assert(
        quarter.HitTest(new PointD(80, 30), 1),
        "A tap on the outline of a turned shape takes hold of it.");
    Assert(
        !flat.HitTest(new PointD(80, 30), 1),
        "The same point is inside the shape upright, and a shape is never taken by its inside.");
    Assert(
        !askew.HitTest(new PointD(askew.Bounds.Left + 2, askew.Bounds.Top + 2), 1),
        "The empty corner of the box around a turned shape is not the shape.");
    Assert(
        askew.Outline().All(point => askew.HitTest(point, 1)),
        "Every point the outline is drawn through is on the shape.");
    Assert(
        !askew.IsTakenBy(
            SelectionArea.Rectangle(new RectD(askew.Bounds.Left, askew.Bounds.Top, 10, 10)),
            AreaSelection.PartlyInside),
        "A band over the corner a turned shape leaves empty does not take it.");

    // The eight binding points are where the corners really are, so an arrow
    // dropped on one lands where the dot was drawn.
    IReadOnlyList<PointD> turnedDots = ConnectorGeometry.BindingPoints(quarter.AnchorFrame);
    Assert(
        HasShapePoint(turnedDots, 80, -20) && HasShapePoint(turnedDots, 20, 80) &&
        HasShapePoint(turnedDots, 80, 30) && HasShapePoint(turnedDots, 50, -20),
        "The binding points of a turned shape are its corners and side midpoints where they now are.");

    // And an arrow bound to one of them turns with the shape.
    var tied = ConnectorBoardObject.Create(
        Guid.NewGuid(),
        1,
        ConnectorKind.Arrow,
        new PointD(100, 30),
        new PointD(400, 300),
        0xFF1F2937,
        4,
        new ConnectorAnchor(flat.Id, 1, 0.5));
    Assert(tied.Start == new PointD(100, 30), "The arrow starts on the right-hand side of the upright shape.");
    ConnectorBoardObject followed = tied.Follow(quarter);
    AssertNear(50, followed.Start.X, "A turn carries the bound endpoint round with the shape.");
    AssertNear(80, followed.Start.Y, "The right-hand side of a shape turned a quarter is its bottom.");
    Assert(followed.End == tied.End, "The free end stays where it was.");

    // Ctrl binds anywhere on the border, and what is stored is still a fraction
    // of the rectangle before the turn, so it holds that place through a turn.
    ConnectorAnchor onBorder = ConnectorGeometry.NearestBorderPoint(
        quarter.Id,
        quarter.AnchorFrame,
        quarter.Outline(),
        new PointD(80, 0));
    AssertNear(
        0.2,
        onBorder.U,
        "A drop on the drawn border becomes the fraction of the box before the turn that it landed on.");
    AssertNear(0, onBorder.V, "Which side it landed on is read in that box too.");

    // The corner handle reads the box along the shape's own axes: a shape turned
    // a quarter grows sideways when the handle is pulled sideways.
    var stretched = (ShapeBoardObject)quarter.WithBounds(
        new RectD(quarter.Bounds.X, quarter.Bounds.Y, quarter.Bounds.Width * 2, quarter.Bounds.Height));
    AssertNear(100, stretched.LayoutWidth, "Pulling the box sideways leaves the turned shape's own width alone.");
    AssertNear(120, stretched.LayoutHeight, "It is the other axis that takes the change.");
    AssertNear(120, stretched.Bounds.Width, "Which is the box the handle asked for.");
    var grown = (ShapeBoardObject)flat.WithBounds(new RectD(0, 0, 200, 120));
    Assert(
        grown is { LayoutWidth: 200, LayoutHeight: 120 } && grown.Bounds == new RectD(0, 0, 200, 120),
        "An upright shape still takes the box it is given, width and height alike.");

    // The rotation handle leaves a shape at any angle, and everything that
    // follows from the angle follows from a free one as it does from a quarter.
    var free = flat.WithAngle(30);
    AssertNear(30, free.AngleDegrees, "A shape takes the angle it is given.");
    AssertNear(100, free.LayoutWidth, "The box the shape was drawn in is kept through a free turn too.");
    AssertNear(
        (100 * Math.Cos(Math.PI / 6)) + (60 * Math.Sin(Math.PI / 6)),
        free.Bounds.Width,
        "The box of a shape turned by 30 degrees is the box of the turned rectangle.");
    AssertNear(
        (100 * Math.Sin(Math.PI / 6)) + (60 * Math.Cos(Math.PI / 6)),
        free.Bounds.Height,
        "On the other axis too.");
    AssertNear(50, free.Bounds.Center.X, "A free turn is about the centre, which does not move.");
    IReadOnlyList<PointD> freeCorners = free.Corners();
    AssertNear(
        50 - (50 * Math.Cos(Math.PI / 6)) + (30 * Math.Sin(Math.PI / 6)),
        freeCorners[0].X,
        "The top-left corner of the rectangle is where a free turn puts it.");
    AssertNear(
        30 - (50 * Math.Sin(Math.PI / 6)) - (30 * Math.Cos(Math.PI / 6)),
        freeCorners[0].Y,
        "On the other axis too.");

    // A bound arrow follows a free turn the way it follows a quarter: the
    // anchor is a fraction of the rectangle before the turn either way.
    ConnectorBoardObject freeFollowed = tied.Follow(free);
    AssertNear(
        50 + (50 * Math.Cos(Math.PI / 6)),
        freeFollowed.Start.X,
        "A free turn carries the bound endpoint to the middle of the side where it now is.");
    AssertNear(
        30 + (50 * Math.Sin(Math.PI / 6)),
        freeFollowed.Start.Y,
        "On the other axis too.");

    // The rotation handle stands clear of the middle of the shape's own top
    // edge, which is the edge that is up for the shape rather than up on screen.
    AssertNear(50, flat.AnchorFrame.RotationHandle(1).X, "The handle of an upright shape is over the middle of its top.");
    AssertNear(-24, flat.AnchorFrame.RotationHandle(1).Y, "And 24 pixels clear of it.");
    AssertNear(
        50 + 30 + 24,
        quarter.AnchorFrame.RotationHandle(1).X,
        "A quarter turn takes the handle round to the side.");
    AssertNear(30, quarter.AnchorFrame.RotationHandle(1).Y, "Level with the middle of the edge it stands over.");
}

// Ink linked to a shape or a label is turned with it, about the same centre.
{
    var horizontal = InkStrokeObject.Create(
        [
            new InkPoint(new PointD(40, 100), 0.5f, 0),
            new InkPoint(new PointD(60, 100), 0.5f, 1),
        ],
        PenStyle.Default,
        0);
    var turnedInk = horizontal.Rotate(new PointD(50, 100), 90);
    AssertNear(50, turnedInk.Points[1].Position.X, "A quarter turn takes a point ten to the right ten below.");
    AssertNear(110, turnedInk.Points[1].Position.Y, "Which is what turning clockwise about the centre means.");
    AssertNear(50, turnedInk.Points[0].Position.X, "The other end goes the other way.");
    AssertNear(90, turnedInk.Points[0].Position.Y, "By the same amount.");
    AssertNear(
        horizontal.Bounds.Height,
        turnedInk.Bounds.Width,
        "The box is worked out again from the points the turn left, so a stroke lying flat now stands up.");
    AssertNear(horizontal.Bounds.Width, turnedInk.Bounds.Height, "And what was its length is now its height.");
    AssertNear(
        horizontal.Style.Thickness,
        turnedInk.Style.Thickness,
        "A turn says nothing about the pen, so the thickness is left as it is.");
    Assert(
        turnedInk.Id == horizontal.Id && turnedInk.ContainerId == horizontal.ContainerId,
        "The stroke is the same stroke, so it stays linked to what it was drawn on.");
    Assert(ReferenceEquals(horizontal, horizontal.Rotate(new PointD(50, 100), 0)), "No turn is no change.");
}

// A turned shape in a board, saved and read back, and a shape from a board
// written before a shape could be turned.
{
    var turnedDocument = new BoardDocument();
    var savedShape = ShapeBoardObject.Create(
        Guid.NewGuid(),
        turnedDocument.NextZIndex,
        new RectD(40, 60, 240, 100),
        ShapeKind.BlockArrow,
        0xFF035ACA,
        0x40CC79A7,
        6,
        30);
    turnedDocument.AddObject(savedShape);

    await using var turnedArchive = new MemoryStream();
    await BoardArchive.SaveAsync(turnedDocument, turnedArchive);
    turnedArchive.Position = 0;
    ShapeBoardObject restoredShape = (await BoardArchive.LoadAsync(turnedArchive))
        .Objects.OfType<ShapeBoardObject>().Single();
    Assert(
        restoredShape is { AngleDegrees: 30, LayoutWidth: 240, LayoutHeight: 100, Kind: ShapeKind.BlockArrow },
        "A shape turned to a free angle round-trips with that angle and the box it was drawn in.");
    AssertNear(savedShape.Bounds.Left, restoredShape.Bounds.Left, "The box comes back where it was.");
    AssertNear(savedShape.Bounds.Width, restoredShape.Bounds.Width, "The box comes back the size it was.");

    var uprightScene = "{\"version\":7,\"objects\":[{\"type\":\"shape\"," +
        "\"id\":\"3f2504e0-4f89-11d3-9a0c-0305e82c3302\",\"zIndex\":0," +
        "\"bounds\":{\"x\":10,\"y\":20,\"width\":300,\"height\":140}," +
        "\"shapeKind\":\"Stadium\",\"thickness\":8}],\"assets\":[]}";
    await using var uprightArchive = new MemoryStream();
    using (var writer = new ZipArchive(uprightArchive, ZipArchiveMode.Create, leaveOpen: true))
    {
        var entry = writer.CreateEntry("scene.json");
        await using var entryStream = entry.Open();
        await entryStream.WriteAsync(Encoding.UTF8.GetBytes(uprightScene));
    }

    uprightArchive.Position = 0;
    ShapeBoardObject fromOldBoard = (await BoardArchive.LoadAsync(uprightArchive))
        .Objects.OfType<ShapeBoardObject>().Single();
    Assert(
        fromOldBoard is { AngleDegrees: 0, LayoutWidth: 300, LayoutHeight: 140 } &&
        fromOldBoard.Bounds == new RectD(10, 20, 300, 140),
        "A board written before a shape could be turned reads as the upright shape it was.");

    var askewScene = "{\"version\":7,\"objects\":[{\"type\":\"shape\"," +
        "\"id\":\"3f2504e0-4f89-11d3-9a0c-0305e82c3303\",\"zIndex\":0," +
        "\"bounds\":{\"x\":0,\"y\":0,\"width\":120,\"height\":120}," +
        "\"shapeKind\":\"Diamond\",\"angleDegrees\":50," +
        "\"layoutWidth\":100,\"layoutHeight\":60}],\"assets\":[]}";
    await using var askewArchive = new MemoryStream();
    using (var writer = new ZipArchive(askewArchive, ZipArchiveMode.Create, leaveOpen: true))
    {
        var entry = writer.CreateEntry("scene.json");
        await using var entryStream = entry.Open();
        await entryStream.WriteAsync(Encoding.UTF8.GetBytes(askewScene));
    }

    askewArchive.Position = 0;
    ShapeBoardObject askewShape = (await BoardArchive.LoadAsync(askewArchive))
        .Objects.OfType<ShapeBoardObject>().Single();
    AssertNear(50, askewShape.AngleDegrees, "A saved angle off the grid is the angle the shape comes back at.");
    AssertNear(60, askewShape.Bounds.Center.X, "The shape stays centred where the file put it.");
    AssertNear(
        (100 * Math.Cos(50 * Math.PI / 180)) + (60 * Math.Sin(50 * Math.PI / 180)),
        askewShape.Bounds.Width,
        "The box is worked out again from the angle and the rectangle rather than trusted.");
}

// A label is a rectangle of text turned about its own centre. What is stored is
// the layout size and the angle; the box the document indexes follows from them,
// and so do the hit test, the area test, and what the corner handle does.
{
    var labelCenter = new PointD(100, 100);
    var upright = FreeTextBoardObject.Create(
        Guid.NewGuid(),
        0,
        labelCenter,
        "Design objects",
        "Segoe UI",
        24,
        0xFF1F2937,
        false,
        false,
        false,
        0,
        200,
        40);
    AssertNear(200, upright.Bounds.Width, "An upright label is as wide as its layout.");
    AssertNear(40, upright.Bounds.Height, "An upright label is as tall as its layout.");
    AssertNear(100, upright.Bounds.Center.X, "The layout is centred on the box.");

    var quarter = upright.WithAngle(90);
    AssertNear(40, quarter.Bounds.Width, "A quarter turn swaps the width for the height.");
    AssertNear(200, quarter.Bounds.Height, "A quarter turn swaps the height for the width.");
    AssertNear(100, quarter.Bounds.Center.X, "A turn is about the centre, which does not move.");
    AssertNear(100, quarter.Bounds.Center.Y, "A turn is about the centre on both axes.");

    var square = FreeTextBoardObject.Create(
        Guid.NewGuid(), 0, labelCenter, "X", "Segoe UI", 24, 0xFF1F2937,
        false, false, false, 45, 100, 100);
    AssertNear(
        100 * Math.Sqrt(2),
        square.Bounds.Width,
        "A square turned by 45 degrees stands on a corner, and its box grows by the square root of two.");
    AssertNear(square.Bounds.Width, square.Bounds.Height, "That box is square as well.");

    // Turned, the corners of the box are outside the label: a tap there reaches
    // whatever is behind it rather than the label.
    Assert(square.HitTest(labelCenter, 1), "A label is hit at its centre.");
    Assert(
        square.HitTest(new PointD(labelCenter.X + 45, labelCenter.Y), 1),
        "A label is hit anywhere on the rectangle it is drawn in.");
    Assert(
        !square.HitTest(new PointD(square.Bounds.Left + 2, square.Bounds.Top + 2), 1),
        "The empty corner of the box around a turned label is not the label.");

    // The corner handle scales the text rather than reflowing it.
    var scaled = (FreeTextBoardObject)upright.WithBounds(
        new RectD(upright.Bounds.Left, upright.Bounds.Top, 400, 80));
    AssertNear(48, scaled.FontSize, "Scaling the box to twice the size doubles the font size.");
    AssertNear(400, scaled.LayoutWidth, "The layout is scaled with the font.");
    AssertNear(400, scaled.Bounds.Width, "The box follows the layout.");

    var moved = (FreeTextBoardObject)upright.WithBounds(upright.Bounds.Translate(new PointD(30, -20)));
    AssertNear(24, moved.FontSize, "Moving a label leaves its size alone.");
    AssertNear(upright.Bounds.Left + 30, moved.Bounds.Left, "Moving a label moves its box.");

    // An angle is any angle: the rotation handle turns an object freely, and
    // what is put right is only an angle outside one turn or no angle at all.
    AssertNear(50, RotatedRectangle.NormalizeAngle(50), "An angle off the grid is kept as it is.");
    AssertNear(10, RotatedRectangle.NormalizeAngle(370), "An angle past a whole turn comes back inside one.");
    AssertNear(315, RotatedRectangle.NormalizeAngle(-45), "A negative angle comes back inside one turn.");
    AssertNear(0, RotatedRectangle.NormalizeAngle(360), "A whole turn is no turn.");
    AssertNear(0, RotatedRectangle.NormalizeAngle(double.NaN), "An angle that is not a number is no turn.");
    AssertNear(0, RotatedRectangle.NormalizeAngle(double.PositiveInfinity), "Nor is an angle without end.");

    // The property bar's two buttons step by 45 and land on the grid, so a free
    // angle is back on it after one press rather than keeping its stray degrees.
    AssertNear(90, RotatedRectangle.StepAngle(50, 45), "A step of 45 from 50 lands on the nearest multiple, 90.");
    AssertNear(0, RotatedRectangle.StepAngle(50, -45), "A step back from 50 lands on 0.");
    AssertNear(45, RotatedRectangle.StepAngle(0, 45), "A step from the grid is a plain step.");
    AssertNear(315, RotatedRectangle.StepAngle(0, -45), "A step back from nothing comes round the other way.");

    // A free angle, which is what the rotation handle leaves behind.
    var askew = upright.WithAngle(30);
    AssertNear(30, askew.AngleDegrees, "A label takes the angle it is given.");
    AssertNear(
        (200 * Math.Cos(Math.PI / 6)) + (40 * Math.Sin(Math.PI / 6)),
        askew.Bounds.Width,
        "The box of a label turned by 30 degrees is the box of the turned rectangle.");
    AssertNear(
        (200 * Math.Sin(Math.PI / 6)) + (40 * Math.Cos(Math.PI / 6)),
        askew.Bounds.Height,
        "On the other axis too.");
    AssertNear(100, askew.Bounds.Center.X, "A free turn is about the centre, which does not move.");
    IReadOnlyList<PointD> askewCorners = askew.Corners();
    AssertNear(
        100 - (100 * Math.Cos(Math.PI / 6)) + (20 * Math.Sin(Math.PI / 6)),
        askewCorners[0].X,
        "The top-left corner of the layout is where the turn puts it.");
    AssertNear(
        100 - (100 * Math.Sin(Math.PI / 6)) - (20 * Math.Cos(Math.PI / 6)),
        askewCorners[0].Y,
        "On the other axis too.");

    // The handle stands clear of the middle of the object's own top edge, so it
    // is above the label when the label is upright and beside it when it is not.
    AnchorFrame uprightFrame = upright.AnchorFrame;
    AssertNear(100, uprightFrame.RotationHandle(1).X, "The handle of an upright object is over the middle of its top.");
    AssertNear(80 - 24, uprightFrame.RotationHandle(1).Y, "And 24 pixels clear of it.");
    AssertNear(
        80 - 12,
        uprightFrame.RotationHandle(2).Y,
        "The 24 pixels are the screen's, so the handle keeps its distance as the board is zoomed.");
    AnchorFrame quarterFrame = quarter.AnchorFrame;
    AssertNear(
        100 + 20 + 24,
        quarterFrame.RotationHandle(1).X,
        "A quarter turn takes the handle round to the side, clear of the edge that is now up for the object.");
    AssertNear(100, quarterFrame.RotationHandle(1).Y, "Level with the centre, which is where that edge's middle now is.");

    // An area takes a turned label by its corners rather than by its box.
    var areaOverCorner = SelectionArea.Rectangle(
        new RectD(square.Bounds.Left - 10, square.Bounds.Top - 10, 14, 14));
    Assert(
        !square.IsTakenBy(areaOverCorner, AreaSelection.PartlyInside),
        "A band over the empty corner of the box does not take the label.");
    Assert(
        square.IsTakenBy(
            SelectionArea.Rectangle(new RectD(90, 90, 20, 20)),
            AreaSelection.PartlyInside),
        "A band inside the label takes it.");
    Assert(
        square.IsTakenBy(
            SelectionArea.Rectangle(new RectD(0, 0, 200, 200)),
            AreaSelection.FullyInside),
        "A band around the whole label takes it under either rule.");
    Assert(
        !square.IsTakenBy(
            SelectionArea.Rectangle(square.Bounds.Inflate(-8)),
            AreaSelection.FullyInside),
        "Fully inside means every corner of the turned rectangle, not of the box.");

    // Ink that touches only a label links to it, as it does to any container.
    var labelBoard = new BoardDocument();
    labelBoard.AddObject(upright);
    var overLabel = InkStrokeObject.Create(
        [
            new InkPoint(new PointD(60, 95), 0.5f, 0),
            new InkPoint(new PointD(140, 105), 0.5f, 1),
        ],
        PenStyle.Default,
        labelBoard.NextZIndex);
    Assert(
        labelBoard.FindSingleTouchedContainer(overLabel)?.Id == upright.Id,
        "A stroke drawn across a label alone links to the label.");
    labelBoard.AddObject(overLabel with { ContainerId = upright.Id });
    Assert(
        labelBoard.GetDeletionGroup([upright.Id]).Count == 2,
        "Deleting a label takes the ink linked to it.");

    Assert(
        BoardPartitioner.DefaultTitle(labelBoard, upright) == "Design objects",
        "A label names an export area with its first line.");
}

// A label in a board, saved and read back: every property survives, the board
// asks for version 7, and what a file could have got wrong is put right.
{
    var labelDocument = new BoardDocument();
    var saved = FreeTextBoardObject.Create(
        Guid.NewGuid(),
        labelDocument.NextZIndex,
        new PointD(240, 160),
        "Two\nlines",
        "Georgia",
        32,
        0xFFE64B3D,
        true,
        true,
        true,
        90,
        180,
        96);
    labelDocument.AddObject(saved);
    Assert(
        BoardArchive.VersionFor(labelDocument) == BoardArchive.CurrentVersion,
        "A board with a label asks for version 7.");

    await using var labelArchive = new MemoryStream();
    await BoardArchive.SaveAsync(labelDocument, labelArchive);
    labelArchive.Position = 0;
    BoardDocument reloaded = await BoardArchive.LoadAsync(labelArchive);
    FreeTextBoardObject restored = reloaded.Objects.OfType<FreeTextBoardObject>().Single();
    Assert(
        restored is
        {
            Text: "Two\nlines",
            FontFamily: "Georgia",
            FontSize: 32,
            Argb: 0xFFE64B3D,
            Bold: true,
            Italic: true,
            Underline: true,
            AngleDegrees: 90,
            LayoutWidth: 180,
            LayoutHeight: 96,
        },
        "A label round-trips with everything it is written with.");
    AssertNear(saved.Bounds.Left, restored.Bounds.Left, "The box comes back where it was.");
    AssertNear(saved.Bounds.Width, restored.Bounds.Width, "The box comes back the size it was.");

    var strangeScene = "{\"version\":7,\"objects\":[{\"type\":\"label\"," +
        "\"id\":\"3f2504e0-4f89-11d3-9a0c-0305e82c3301\",\"zIndex\":0," +
        "\"bounds\":{\"x\":0,\"y\":0,\"width\":120,\"height\":40}," +
        "\"textContent\":\"Askew\",\"fontFamily\":\"Papyrus\",\"fontSize\":26," +
        "\"angleDegrees\":50,\"layoutWidth\":120,\"layoutHeight\":40}],\"assets\":[]}";
    await using var strangeArchive = new MemoryStream();
    using (var writer = new ZipArchive(strangeArchive, ZipArchiveMode.Create, leaveOpen: true))
    {
        var entry = writer.CreateEntry("scene.json");
        await using var entryStream = entry.Open();
        await entryStream.WriteAsync(Encoding.UTF8.GetBytes(strangeScene));
    }

    strangeArchive.Position = 0;
    FreeTextBoardObject normalized = (await BoardArchive.LoadAsync(strangeArchive))
        .Objects.OfType<FreeTextBoardObject>().Single();
    Assert(normalized.FontFamily == "Segoe UI", "A font nobody has falls back to the default.");
    AssertNear(50, normalized.AngleDegrees, "A saved angle off the grid is the angle the label comes back at.");
    AssertNear(26, normalized.FontSize, "A size inside the range is left alone.");
    Assert(
        normalized.Underline == false && normalized.Bold == false,
        "A label that says nothing about its style is written plainly.");
}

// The label defaults: what the last label was written with, remembered for the
// next one and normalized like every other setting.
{
    var labelSettings = AppSettingsSerializer.Parse(AppSettingsSerializer.Format(new AppSettings
    {
        Label = new LabelSettings
        {
            FontFamily = "Consolas",
            FontSize = 40,
            Argb = 0xFF009E73,
            Bold = true,
            Underline = true,
        },
    }));
    Assert(
        labelSettings.Label is
        {
            FontFamily: "Consolas",
            FontSize: 40,
            Argb: 0xFF009E73,
            Bold: true,
            Italic: false,
            Underline: true,
        },
        "The label defaults survive a round trip.");
    Assert(
        AppSettingsSerializer.Parse("{ }").Label is
        {
            FontFamily: "Segoe UI",
            FontSize: 24,
            Argb: 0xFF1F2937,
            Bold: false,
        },
        "A file that says nothing about labels takes Segoe UI 24 in near-black.");
    Assert(
        AppSettingsSerializer.Parse("{ \"label\": { \"fontFamily\": \"Papyrus\", \"fontSize\": 37 } }")
            .Label is { FontFamily: "Segoe UI", FontSize: 24 },
        "A font or a size that is not one of the choices falls back to the default.");
}

// Connectors: where the line runs, what it points at, how it follows what it is
// bound to, and what happens to it when that is deleted.
{
    var straight = ConnectorBoardObject.Create(
        Guid.NewGuid(),
        0,
        ConnectorKind.Arrow,
        new PointD(0, 0),
        new PointD(200, 0),
        0xFF1F2937,
        4);
    Assert(
        straight.Polyline().SequenceEqual([new PointD(0, 0), new PointD(200, 0)]),
        "A straight connector is its two ends and nothing in between.");
    Assert(
        straight.Bounds.Left <= 0 && straight.Bounds.Right >= 200,
        "The box covers both ends of the line.");
    Assert(
        straight.Bounds.Top < 0 && straight.Bounds.Bottom > 0,
        "The box is inflated by half the line's width, so a horizontal line is not a box of no height.");

    // A curve leaves each end along the outward normal of the side its anchor
    // sits on, so an arrow between two boxes bows out rather than cutting across.
    var curveTarget = Guid.NewGuid();
    var curved = ConnectorBoardObject.Create(
        Guid.NewGuid(),
        0,
        ConnectorKind.CurvedArrow,
        new PointD(0, 0),
        new PointD(200, 200),
        0xFF1F2937,
        4,
        new ConnectorAnchor(curveTarget, 0, 0.5),
        null);
    IReadOnlyList<PointD> curvePath = curved.Polyline();
    Assert(
        curvePath.Count == ConnectorGeometry.CurveSegments + 1,
        "A curve is flattened into the segments everything else reads it as.");
    Assert(
        curvePath[0] == new PointD(0, 0) && curvePath[^1] == new PointD(200, 200),
        "The flattened curve starts and ends where the connector does.");
    Assert(
        curvePath[1].X < 0,
        "An end bound to the left side of a box leaves it leftwards, away from the box.");
    Assert(
        curved.Bounds.Left < 0,
        "The box of a curve covers where the curve actually goes, not just its two ends.");
    Assert(
        ConnectorGeometry.Polyline(ConnectorKind.CurvedArrow, new PointD(0, 0), new PointD(200, 0))[1].X > 0,
        "A free end leaves horizontally, towards the other end.");

    // The head follows the last segment, and the line stops at the base of it.
    IReadOnlyList<PointD> head = straight.Arrowhead()!;
    Assert(head.Count == 3 && head[0] == new PointD(200, 0), "The arrowhead is a triangle with its tip at the end.");
    AssertNear(16, 200 - head[1].X, "The head is four times the thickness long.");
    AssertNear(10, head[1].Y - head[2].Y, "The head is two and a half times the thickness wide.");
    Assert(
        ConnectorGeometry.Arrowhead(ConnectorKind.Line, straight.Polyline(), 4) is null,
        "A plain line has no head.");
    var upward = ConnectorBoardObject.Create(
        Guid.NewGuid(), 0, ConnectorKind.Arrow, new PointD(0, 200), new PointD(0, 0), 0xFF1F2937, 4);
    IReadOnlyList<PointD> upwardHead = upward.Arrowhead()!;
    Assert(
        upwardHead[1].Y > 0 && upwardHead[2].Y > 0,
        "An arrow drawn upwards has its head pointing up: the triangle sits below its tip.");
    IReadOnlyList<PointD> shortened = ConnectorGeometry.LinePath(ConnectorKind.Arrow, straight.Polyline(), 4);
    AssertNear(184, shortened[^1].X, "The line stops at the base of the head rather than poking through the tip.");

    Assert(straight.HitTest(new PointD(100, 3), 1), "A tap within the band of the line takes the connector.");
    Assert(!straight.HitTest(new PointD(100, 30), 1), "A tap well off the line is not the connector.");
    Assert(
        !straight.HitTest(new PointD(100, 6), 8),
        "The band is screen pixels, so zooming in narrows what it covers on the board.");
    Assert(curved.HitTest(curvePath[16], 1), "A tap on the curve itself takes a curved connector.");
    Assert(
        !curved.HitTest(new PointD(190, 10), 1),
        "The empty corner of a curve's box is not the curve.");

    // The eight binding points, and what Ctrl asks for instead.
    var box = new RectD(100, 100, 200, 100);
    IReadOnlyList<PointD> dots = ConnectorGeometry.BindingPoints(box);
    Assert(dots.Count == 8, "A box offers eight binding points.");
    Assert(
        dots.Contains(new PointD(100, 100)) && dots.Contains(new PointD(300, 200)) &&
        dots.Contains(new PointD(200, 100)) && dots.Contains(new PointD(100, 150)),
        "They are the four corners and the four side midpoints.");
    var boxId = Guid.NewGuid();
    Assert(
        ConnectorGeometry.NearestBindingPoint(boxId, box, new PointD(205, 108)) ==
        new ConnectorAnchor(boxId, 0.5, 0),
        "A drop near the top of a box binds to the midpoint of that side.");
    Assert(
        ConnectorGeometry.NearestBindingPoint(boxId, box, new PointD(296, 104)) ==
        new ConnectorAnchor(boxId, 1, 0),
        "A drop near a corner binds to the corner.");
    ConnectorAnchor border = ConnectorGeometry.NearestBorderPoint(boxId, box, null, new PointD(240, 90));
    Assert(
        border.V == 0 && Math.Abs(border.U - 0.7) < 0.000001,
        "With Ctrl the endpoint takes the nearest point anywhere on the border, as a fraction of the box.");
    ConnectorAnchor outlineBorder = ConnectorGeometry.NearestBorderPoint(
        boxId,
        box,
        ShapeGeometry.Outline(ShapeKind.Diamond, box),
        new PointD(100, 100));
    Assert(
        outlineBorder.U > 0 && outlineBorder.U < 0.5 && outlineBorder.V > 0 && outlineBorder.V < 0.5,
        "A shape's own outline answers for it, so a corner of a diamond's box binds to the slope between its points.");

    // Following: what is bound moves and the endpoint goes with it, whether it
    // sits on a corner or on the middle of a side.
    var followed = ShapeBoardObject.Create(
        Guid.NewGuid(), 0, box, ShapeKind.RoundedRectangle, 0xFF1F2937, null, 4);
    var bound = ConnectorBoardObject.Create(
        Guid.NewGuid(),
        1,
        ConnectorKind.Arrow,
        new PointD(300, 150),
        new PointD(600, 400),
        0xFF1F2937,
        4,
        new ConnectorAnchor(followed.Id, 1, 0.5),
        null);
    var movedShape = (ShapeBoardObject)followed.WithBounds(box.Translate(new PointD(50, 20)));
    ConnectorBoardObject afterMove = bound.Follow(movedShape);
    Assert(afterMove.Start == new PointD(350, 170), "A move carries the bound endpoint with the object.");
    Assert(afterMove.End == bound.End, "The free end stays where it was.");
    var resizedShape = (ShapeBoardObject)followed.WithBounds(new RectD(100, 100, 400, 200));
    Assert(
        bound.Follow(resizedShape).Start == new PointD(500, 200),
        "A resize keeps the endpoint on the middle of the same side.");
    var corner = bound.WithEndpoints(
        bound.Start,
        bound.End,
        new ConnectorAnchor(followed.Id, 1, 1),
        null);
    Assert(
        corner.Follow(resizedShape).Start == new PointD(500, 300),
        "A corner anchor stays on that corner through a resize.");
    Assert(
        corner.Follow(ShapeBoardObject.Create(
            Guid.NewGuid(), 0, box, ShapeKind.Ellipse, 0xFF1F2937, null, 4)) == corner,
        "An object the connector is not bound to moves without touching it.");

    // A group gesture carries the connector by its box, anchors and all.
    ConnectorBoardObject dragged = (ConnectorBoardObject)bound.WithBounds(
        bound.Bounds.Translate(new PointD(10, 10)));
    Assert(
        dragged.Start == new PointD(310, 160) && dragged.EndAnchor is null &&
        dragged.StartAnchor == bound.StartAnchor,
        "Moving a connector's box moves both ends and keeps what they are bound to.");
    Assert(
        bound.Detach(followed.Id).StartAnchor is null && bound.Detach(followed.Id).Start == bound.Start,
        "Detaching clears the anchor and leaves the endpoint where it was.");

    // An area takes a connector by its path, as it takes a stroke by its points.
    Assert(
        straight.IsTakenBy(SelectionArea.Rectangle(new RectD(90, -5, 20, 10)), AreaSelection.PartlyInside),
        "A band across the line takes the connector.");
    Assert(
        !curved.IsTakenBy(SelectionArea.Rectangle(new RectD(170, 0, 30, 30)), AreaSelection.PartlyInside),
        "A band over the empty corner of a curve's box does not take it.");
    Assert(
        straight.IsTakenBy(SelectionArea.Rectangle(new RectD(-20, -20, 260, 40)), AreaSelection.FullyInside),
        "An area around the whole line takes it under the stricter rule as well.");
    Assert(
        !straight.IsTakenBy(SelectionArea.Rectangle(new RectD(-20, -20, 150, 40)), AreaSelection.FullyInside),
        "Under Only objects fully inside, both ends have to be inside.");

    // Deleting what a connector points at detaches it, in the same step.
    var connectorBoard = new BoardDocument();
    var deleted = ShapeBoardObject.Create(
        Guid.NewGuid(),
        connectorBoard.NextZIndex,
        new RectD(0, 0, 100, 100),
        ShapeKind.Ellipse,
        0xFF1F2937,
        null,
        4);
    connectorBoard.AddObject(deleted);
    var pointing = ConnectorBoardObject.Create(
        Guid.NewGuid(),
        connectorBoard.NextZIndex,
        ConnectorKind.Arrow,
        new PointD(100, 50),
        new PointD(400, 50),
        0xFF1F2937,
        4,
        new ConnectorAnchor(deleted.Id, 1, 0.5),
        null);
    connectorBoard.AddObject(pointing);
    Assert(
        connectorBoard.ConnectorsAttachedTo(deleted.Id).Single().Id == pointing.Id,
        "The board finds the connectors bound to an object.");
    var connectorHistory = new CommandHistory();
    connectorHistory.Execute(
        new CompositeCommand(
        [
            new RemoveObjectsCommand(connectorBoard.GetDeletionGroup([deleted.Id])),
            new ReplaceObjectsCommand([pointing], [pointing.Detach(deleted.Id)]),
        ]),
        connectorBoard);
    Assert(
        connectorBoard.Objects.OfType<ShapeBoardObject>().Any() == false &&
        connectorBoard.Objects.OfType<ConnectorBoardObject>().Single() is { StartAnchor: null } left &&
        left.Start == new PointD(100, 50),
        "Deleting a shape leaves the arrow where it was, freed from it.");
    connectorHistory.Undo(connectorBoard);
    Assert(
        connectorBoard.Objects.Count == 2 &&
        connectorBoard.Objects.OfType<ConnectorBoardObject>().Single().StartAnchor?.ObjectId == deleted.Id,
        "One undo puts the shape back and binds the arrow to it again.");

    // An export area is never cut between a shape and its arrow.
    var partitionBoard = new BoardDocument();
    var leftShape = ShapeBoardObject.Create(
        Guid.NewGuid(), partitionBoard.NextZIndex, new RectD(0, 0, 200, 100),
        ShapeKind.RoundedRectangle, 0xFF1F2937, null, 4);
    partitionBoard.AddObject(leftShape);
    var rightShape = ShapeBoardObject.Create(
        Guid.NewGuid(), partitionBoard.NextZIndex, new RectD(1600, 0, 200, 100),
        ShapeKind.RoundedRectangle, 0xFF1F2937, null, 4);
    partitionBoard.AddObject(rightShape);
    Assert(
        BoardPartitioner.Partition(partitionBoard).Count == 2,
        "Two shapes a board apart are two export areas.");
    var joining = ConnectorBoardObject.Create(
        Guid.NewGuid(),
        partitionBoard.NextZIndex,
        ConnectorKind.Arrow,
        new PointD(200, 50),
        new PointD(1600, 50),
        0xFF1F2937,
        4,
        new ConnectorAnchor(leftShape.Id, 1, 0.5),
        new ConnectorAnchor(rightShape.Id, 0, 0.5));
    partitionBoard.AddObject(joining);
    IReadOnlyList<ExportArea> joined = BoardPartitioner.Partition(partitionBoard);
    Assert(
        joined.Count == 1 && joined[0].Objects.Count == 3,
        "A connector bound to both brings the two units together, so the arrow is not cut off from what it joins.");
    var lonely = new BoardDocument();
    lonely.AddObject(ConnectorBoardObject.Create(
        Guid.NewGuid(), 0, ConnectorKind.Line, new PointD(0, 0), new PointD(100, 100), 0xFF1F2937, 4));
    Assert(
        BoardPartitioner.Partition(lonely).Count == 1,
        "A connector bound to nothing is an area of its own.");

    // A board that carries a connector, written and read back.
    var savedBoard = new BoardDocument();
    var firstShape = ShapeBoardObject.Create(
        Guid.NewGuid(), savedBoard.NextZIndex, new RectD(0, 0, 120, 80),
        ShapeKind.Diamond, 0xFF1F2937, null, 4);
    savedBoard.AddObject(firstShape);
    var secondShape = ShapeBoardObject.Create(
        Guid.NewGuid(), savedBoard.NextZIndex, new RectD(300, 200, 120, 80),
        ShapeKind.Ellipse, 0xFF1F2937, null, 4);
    savedBoard.AddObject(secondShape);
    var savedConnector = ConnectorBoardObject.Create(
        Guid.NewGuid(),
        savedBoard.NextZIndex,
        ConnectorKind.CurvedArrow,
        new PointD(120, 40),
        new PointD(300, 240),
        0xFF009E73,
        8,
        new ConnectorAnchor(firstShape.Id, 1, 0.5),
        new ConnectorAnchor(secondShape.Id, 0, 0.5));
    savedBoard.AddObject(savedConnector);
    Assert(
        BoardArchive.VersionFor(savedBoard) == BoardArchive.CurrentVersion,
        "A board that holds a connector asks for version 7.");

    await using var connectorArchive = new MemoryStream();
    await BoardArchive.SaveAsync(savedBoard, connectorArchive);
    connectorArchive.Position = 0;
    ConnectorBoardObject restoredConnector = (await BoardArchive.LoadAsync(connectorArchive))
        .Objects.OfType<ConnectorBoardObject>().Single();
    Assert(
        restoredConnector == savedConnector,
        "A connector round-trips with its kind, colour, thickness, ends, and both anchors.");

    // What a file could have got wrong: a kind from a later release, an anchor
    // naming an object that is not there, and fractions outside the box.
    var danglingScene = "{\"version\":7,\"objects\":[{\"type\":\"connector\"," +
        "\"id\":\"3f2504e0-4f89-11d3-9a0c-0305e82c3302\",\"zIndex\":0," +
        "\"bounds\":{\"x\":0,\"y\":0,\"width\":10,\"height\":10}," +
        "\"connectorKind\":\"Elbow\",\"thickness\":0,\"startX\":0,\"startY\":0," +
        "\"endX\":200,\"endY\":0,\"startAnchor\":{\"objectId\":" +
        "\"3f2504e0-4f89-11d3-9a0c-0305e82c3303\",\"u\":4,\"v\":-1}}],\"assets\":[]}";
    await using var danglingArchive = new MemoryStream();
    using (var writer = new ZipArchive(danglingArchive, ZipArchiveMode.Create, leaveOpen: true))
    {
        var entry = writer.CreateEntry("scene.json");
        await using var entryStream = entry.Open();
        await entryStream.WriteAsync(Encoding.UTF8.GetBytes(danglingScene));
    }

    danglingArchive.Position = 0;
    ConnectorBoardObject dangling = (await BoardArchive.LoadAsync(danglingArchive))
        .Objects.OfType<ConnectorBoardObject>().Single();
    Assert(
        dangling is { Kind: ConnectorKind.Arrow, Thickness: 4, StartAnchor: null },
        "An unknown kind reads as an arrow, a thickness of nothing takes the default, and an anchor naming " +
        "an object the file does not have is dropped.");
    Assert(
        dangling.Bounds.Right >= 200,
        "The box is worked out again from the ends rather than trusted to the file.");
    Assert(
        ConnectorAnchor.Normalize(Guid.Empty, 4, -1) == new ConnectorAnchor(Guid.Empty, 1, 0),
        "A fraction outside the box is brought back onto it.");

    // The connector defaults: what the last one was drawn with.
    var connectorSettings = AppSettingsSerializer.Parse(AppSettingsSerializer.Format(new AppSettings
    {
        Connector = new ConnectorSettings
        {
            Argb = 0xFF009E73,
            Thickness = 8,
            Kind = ConnectorKind.CurvedArrow,
        },
    }));
    Assert(
        connectorSettings.Connector is
        {
            Argb: 0xFF009E73,
            Thickness: 8,
            Kind: ConnectorKind.CurvedArrow,
        },
        "The connector defaults survive a round trip.");
    Assert(
        AppSettingsSerializer.Parse("{ }").Connector is
        {
            Argb: 0xFFE64B3D,
            Thickness: 4,
            Kind: ConnectorKind.Arrow,
        },
        "A file that says nothing about connectors draws the next one as an arrow in the pen's colour.");
    Assert(
        AppSettingsSerializer.Parse("{ \"connector\": { \"argb\": 123, \"thickness\": 99, \"kind\": 42 } }")
            .Connector is { Argb: 0xFFE64B3D, Thickness: 4, Kind: ConnectorKind.Arrow },
        "A connector default that is not one of the choices normalizes back to one that is.");
}

// A duplicate has to hold together on its own: the copies of the strokes follow
// the copy of the shape, the copy of the arrow points at the copy rather than at
// the original, and the end that pointed at something left behind lets go.
{
    var shapeId = Guid.NewGuid();
    var outsideId = Guid.NewGuid();
    var shape = ShapeBoardObject.Create(
        shapeId,
        0,
        new RectD(0, 0, 100, 100),
        ShapeKind.RoundedRectangle,
        0xFF1F2937,
        null,
        4);
    var outside = ShapeBoardObject.Create(
        outsideId,
        3,
        new RectD(300, 0, 100, 100),
        ShapeKind.Ellipse,
        0xFF1F2937,
        null,
        4);
    var firstStroke = InkStrokeObject.Create(
        [new InkPoint(new PointD(10, 10), 0.5f, 0), new InkPoint(new PointD(40, 40), 0.5f, 1)],
        PenStyle.Default,
        1,
        containerId: shapeId);
    var secondStroke = InkStrokeObject.Create(
        [new InkPoint(new PointD(50, 50), 0.5f, 0), new InkPoint(new PointD(80, 80), 0.5f, 1)],
        PenStyle.Default,
        2,
        containerId: shapeId);
    var connector = ConnectorBoardObject.Create(
        Guid.NewGuid(),
        4,
        ConnectorKind.Arrow,
        new PointD(100, 50),
        new PointD(300, 50),
        0xFFE64B3D,
        4,
        new ConnectorAnchor(shapeId, 1, 0.5),
        new ConnectorAnchor(outsideId, 0, 0.5));

    IReadOnlyList<BoardObject> copies = SelectionDuplicator.Duplicate(
        [shape, connector],
        [firstStroke, secondStroke],
        [connector],
        new PointD(24, 24),
        5);
    Assert(copies.Count == 4, "A connector given twice is copied once.");

    var copiedShape = (ShapeBoardObject)copies[0];
    var copiedConnector = (ConnectorBoardObject)copies[1];
    InkStrokeObject[] copiedStrokes = copies.OfType<InkStrokeObject>().ToArray();
    Assert(
        copies.Select(item => item.Id).Distinct().Count() == 4,
        "Every copy has an id of its own.");
    Assert(
        copies.All(item => item.Id != shapeId && item.Id != connector.Id &&
                           item.Id != firstStroke.Id && item.Id != secondStroke.Id),
        "No copy keeps the id of what it was copied from.");
    Assert(
        copiedStrokes.Length == 2 && copiedStrokes.All(stroke => stroke.ContainerId == copiedShape.Id),
        "A stroke linked to a duplicated container follows the copy.");
    Assert(
        copiedConnector.StartAnchor?.ObjectId == copiedShape.Id,
        "An end bound to a duplicated object is bound to the copy.");
    Assert(
        copiedConnector.EndAnchor is null,
        "An end bound to something left behind lets go rather than tying the copy to it.");
    Assert(
        copiedConnector.Start == new PointD(124, 74) && copiedConnector.End == new PointD(324, 74),
        "Both ends of the copy move by the offset, including the one that let go.");
    Assert(
        copiedShape.Bounds == new RectD(24, 24, 100, 100),
        "The copy is offset from the original by what was asked for.");
    Assert(
        copiedStrokes
            .OrderBy(stroke => stroke.ZIndex)
            .Zip([firstStroke, secondStroke])
            .All(pair => pair.First.Points
                .Zip(pair.Second.Points)
                .All(point =>
                    Math.Abs(point.First.Position.X - point.Second.Position.X - 24) < 0.000001 &&
                    Math.Abs(point.First.Position.Y - point.Second.Position.Y - 24) < 0.000001)),
        "A duplicated stroke is the same stroke, moved.");

    // The copies sit above the board, keeping the order they had on it: the
    // shape under its ink, and the arrow over both.
    Assert(
        copiedShape.ZIndex == 5 &&
        copiedStrokes.Select(stroke => stroke.ZIndex).OrderBy(depth => depth).SequenceEqual([6, 7]) &&
        copiedConnector.ZIndex == 8,
        "The copies take consecutive depths from the one given, in the order the originals were in.");

    // One object on its own, and a stroke on its own, take the same path.
    Assert(
        SelectionDuplicator.Duplicate([firstStroke], [], [], new PointD(24, 24), 9) is
            [InkStrokeObject { ContainerId: null, ZIndex: 9 }],
        "A stroke duplicated without its container is a stroke of its own rather than one tied to what was not copied.");
    Assert(
        SelectionDuplicator.Duplicate([outside], [], [], new PointD(24, 24), 9) is
            [ShapeBoardObject { ZIndex: 9 } only] &&
        only.Bounds == new RectD(324, 24, 100, 100),
        "One object duplicates as one object.");

    // A turned shape is copied as it stands: the angle and the box it was drawn
    // in come with it, so the copy is the same shape rather than an upright one
    // in the box the turn happens to take up.
    var turned = ShapeBoardObject.Create(
        Guid.NewGuid(),
        3,
        new RectD(300, 0, 100, 40),
        ShapeKind.BlockArrow,
        0xFF1F2937,
        null,
        4,
        45);
    Assert(
        SelectionDuplicator.Duplicate([turned], [], [], new PointD(24, 24), 9) is
            [ShapeBoardObject copiedTurn] &&
        copiedTurn.AngleDegrees == turned.AngleDegrees &&
        copiedTurn.LayoutWidth == turned.LayoutWidth &&
        copiedTurn.LayoutHeight == turned.LayoutHeight &&
        copiedTurn.Bounds == turned.Bounds.Translate(new PointD(24, 24)),
        "A turned shape keeps its angle and the box it was drawn in.");
}

// Bring forward and Send backward move the block past exactly one object, and
// offer themselves only while there is one to pass.
{
    var zDocument = new BoardDocument();
    ShapeBoardObject[] stack = Enumerable.Range(0, 4)
        .Select(index => ShapeBoardObject.Create(
            Guid.NewGuid(),
            index,
            new RectD(index * 10, 0, 40, 40),
            ShapeKind.RoundedRectangle,
            0xFF1F2937,
            null,
            4))
        .ToArray();
    foreach (ShapeBoardObject item in stack)
    {
        zDocument.AddObject(item);
    }

    Guid[] block = [stack[1].Id, stack[2].Id];
    Assert(
        !ZOrder.IsAtFront(zDocument.Objects, block) && !ZOrder.IsAtBack(zDocument.Objects, block),
        "A block with something above and below it can move either way.");
    Assert(
        ZOrder.IsAtFront(zDocument.Objects, [stack[2].Id, stack[3].Id]),
        "A block holding the topmost object is at the front.");
    Assert(
        ZOrder.IsAtBack(zDocument.Objects, [stack[0].Id, stack[1].Id]),
        "A block holding the bottom object is at the back.");
    Assert(
        ZOrder.Step(zDocument.Objects, [stack[2].Id, stack[3].Id], forward: true).Before.Count == 0,
        "A block at the front has nothing left to pass.");
    Assert(
        ZOrder.Step(zDocument.Objects, [stack[0].Id, stack[1].Id], forward: false).Before.Count == 0,
        "A block at the back has nothing left to pass.");
    Assert(
        ZOrder.IsAtFront(zDocument.Objects, []) && ZOrder.IsAtBack(zDocument.Objects, []),
        "Nothing selected is at both ends at once, which is what disables all four commands.");

    (IReadOnlyList<BoardObject> zBefore, IReadOnlyList<BoardObject> zAfter) =
        ZOrder.Step(zDocument.Objects, block, forward: true);
    var stepForward = new ReplaceObjectsCommand(zBefore, zAfter);
    stepForward.Execute(zDocument);
    Assert(
        zDocument.Objects.Select(item => item.Id).SequenceEqual(
            [stack[0].Id, stack[3].Id, stack[1].Id, stack[2].Id]),
        "Bring forward takes the block past the one object above it, keeping its own order.");
    Assert(
        zDocument.Objects.Select(item => item.ZIndex).SequenceEqual([0, 1, 2, 3]),
        "The depths of the span are dealt out again rather than new ones invented.");

    stepForward.Undo(zDocument);
    Assert(
        zDocument.Objects.Select(item => item.Id).SequenceEqual(stack.Select(item => item.Id)) &&
        zDocument.Objects.Select(item => item.ZIndex).SequenceEqual([0, 1, 2, 3]),
        "Undoing the step puts every depth back where it was.");

    (IReadOnlyList<BoardObject> backBefore, IReadOnlyList<BoardObject> backAfter) =
        ZOrder.Step(zDocument.Objects, block, forward: false);
    new ReplaceObjectsCommand(backBefore, backAfter).Execute(zDocument);
    Assert(
        zDocument.Objects.Select(item => item.Id).SequenceEqual(
            [stack[1].Id, stack[2].Id, stack[0].Id, stack[3].Id]),
        "Send backward takes the block past the one object below it.");
}

Console.WriteLine("SQLBI.Whiteboard.Core smoke tests passed.");

static InkStrokeObject ExportStroke(double x, double y, double width, double height, int zIndex, Guid? containerId = null) =>
    InkStrokeObject.Create(
        [
            new InkPoint(new PointD(x, y), 0.5f, 0),
            new InkPoint(new PointD(x + width, y + height), 0.5f, 1),
        ],
        PenStyle.Default,
        zIndex,
        containerId: containerId);

static bool HasShapePoint(IReadOnlyList<PointD> outline, double x, double y) =>
    outline.Any(point => Math.Abs(point.X - x) <= 0.000001 && Math.Abs(point.Y - y) <= 0.000001);

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertNear(double expected, double actual, string message)
{
    if (Math.Abs(expected - actual) > 0.000001)
    {
        throw new InvalidOperationException(message);
    }
}
