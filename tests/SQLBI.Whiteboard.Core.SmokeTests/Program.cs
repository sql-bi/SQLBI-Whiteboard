using SQLBI.Whiteboard.Core.Commands;
using SQLBI.Whiteboard.Core.Geometry;
using SQLBI.Whiteboard.Core.Model;
using SQLBI.Whiteboard.Core.Persistence;
using SQLBI.Whiteboard.Core.Viewport;

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

await using var archive = new MemoryStream();
await BoardArchive.SaveAsync(document, archive);
archive.Position = 0;
var loaded = await BoardArchive.LoadAsync(archive);
Assert(loaded.Objects.Count == 3, "Archive should round-trip scene objects.");
Assert(loaded.Assets[asset.Id].Data.SequenceEqual(asset.Data), "Archive should round-trip asset bytes.");
Assert(
    loaded.Objects.OfType<InkStrokeObject>()
        .Single(item => item.Id == archivedLinkedStroke.Id) is
        { ContainerId: var loadedContainerId, Style.Kind: PenKind.Calligraphy } &&
    loadedContainerId == archivedContainer.Id,
    "Archive should preserve stroke-container links.");

AssertNear(
    20,
    PenStyleMetrics.MaximumThickness(new PenStyle(0xFF000000, 5, PenKind.Highlighter)),
    "Highlighter bounds should account for its broad nib.");
var lightPressure = CalligraphyDynamics.AdjustPressure(0.2f, 0.5);
var heavyPressure = CalligraphyDynamics.AdjustPressure(0.9f, 0.5);
var fastStroke = CalligraphyDynamics.AdjustPressure(0.9f, 4);
Assert(
    heavyPressure > lightPressure,
    "Calligraphy width should increase with pressure.");
Assert(
    fastStroke < heavyPressure,
    "Calligraphy width should decrease as drawing speed increases.");

Console.WriteLine("SQLBI.Whiteboard.Core smoke tests passed.");

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
