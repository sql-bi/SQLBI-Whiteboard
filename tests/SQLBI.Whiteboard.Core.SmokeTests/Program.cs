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
Assert(stroke.HitTest(new PointD(10, 9), 4), "Stroke hit testing should find a nearby point.");
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
    BoardArchive.VersionFor(document) == BoardArchive.CurrentVersion &&
    loadedWithFrame.Objects.OfType<FrameBoardObject>().Single() is { Title: "Slide 1", Bounds.Width: 1000 },
    "A frame round-trips with its title, and asks for the current version.");
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
});
Assert(
    formattedSettings.Contains("BottomCenter", StringComparison.Ordinal),
    "Settings JSON should persist the toolbar placement name.");
var roundTripped = AppSettingsSerializer.Parse(formattedSettings);
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
Assert(
    defaultSettings.SnippetFormatOrder is ["plain", "dax", "sqlserver"],
    "Missing settings should keep Plain text first so paste stays plain text.");
Assert(
    TextLanguageIds.NormalizeOrder(["sqlserver", "plain", "dax", "plain", "python"]) is
        ["sqlserver", "plain", "dax"],
    "Snippet format order should drop unknowns, keep first-seen order, and fill missing languages.");
var snippetOrderRoundTrip = AppSettingsSerializer.Parse(
    AppSettingsSerializer.Format(new AppSettings
    {
        SnippetFormatOrder = ["dax", "sqlserver", "plain"],
    }));
Assert(
    snippetOrderRoundTrip.SnippetFormatOrder is ["dax", "sqlserver", "plain"],
    "Settings JSON should round-trip snippet format order.");
Assert(
    AppSettingsSerializer.Parse("{ }").SnippetFormatOrder is ["plain", "dax", "sqlserver"],
    "Partial settings should fill the default snippet format order.");
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

    var exportSettings = AppSettingsSerializer.Parse("""{ "export": { "gapThreshold": 9999, "smallestTextPoints": 11, "order": "Reading" } }""");
    Assert(
        exportSettings.Export.GapThreshold == ExportLayoutOptions.MaximumGapThreshold &&
        exportSettings.Export.SmallestTextPoints == ExportLayoutOptions.DefaultSmallestTextPoints &&
        exportSettings.Export.Order == AreaOrder.Reading,
        "Export settings are clamped to what the dialog offers.");
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
