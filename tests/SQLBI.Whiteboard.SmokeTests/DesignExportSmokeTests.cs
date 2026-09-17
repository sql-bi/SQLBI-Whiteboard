using SQLBI.Whiteboard.Core.Export;
using SQLBI.Whiteboard.Core.Geometry;
using SQLBI.Whiteboard.Core.Model;
using SQLBI.Whiteboard.Core.Settings;
using SQLBI.Whiteboard.Export;

namespace SQLBI.Whiteboard.SmokeTests;

/// <summary>
/// What an editable slide carries for the objects that have a native form: one
/// element per shape, label, and connector, with the colors, the angle, the style,
/// and the curve the screen shows, and a picture over the page only for what is
/// left, which is the ink and only where it cannot go as strokes. The writers
/// are checked in the Core harness, which can open what they produce; what only
/// this side can answer is what the board hands them.
/// </summary>
internal static class DesignExportSmokeTests
{
    private const int PixelWidth = 1600;
    private const int PixelHeight = 900;
    private const uint Outline = 0xFF035ACA;
    private const uint LabelArgb = 0xFF1F2937;

    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                CheckTheElements();
                CheckWhatIsLeftForThePicture();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new InvalidOperationException("Design export smoke tests failed.", failure);
        }
    }

    private static void CheckTheElements()
    {
        var document = new BoardDocument();
        ShapeKind[] kinds = Enum.GetValues<ShapeKind>();
        foreach (ShapeKind kind in kinds)
        {
            document.AddObject(Shape(document, kind, (int)kind));
        }

        FreeTextBoardObject label = Label(document, angleDegrees: 45);
        document.AddObject(label);
        document.AddObject(Label(document, angleDegrees: 0, new PointD(900, 700)));

        // A curve bound to the right-hand side of the first shape, and a straight
        // line attached to nothing.
        ShapeBoardObject bound = document.Objects.OfType<ShapeBoardObject>().First();
        document.AddObject(ConnectorBoardObject.Create(
            Guid.NewGuid(),
            document.NextZIndex,
            ConnectorKind.CurvedArrow,
            new PointD(bound.Bounds.Right, bound.Bounds.Center.Y),
            new PointD(900, 600),
            Outline,
            ConnectorBoardObject.DefaultThickness,
            new ConnectorAnchor(bound.Id, 1, 0.5)));
        document.AddObject(ConnectorBoardObject.Create(
            Guid.NewGuid(),
            document.NextZIndex,
            ConnectorKind.Line,
            new PointD(400, 700),
            new PointD(100, 500),
            Outline,
            ConnectorBoardObject.DefaultThickness));

        IReadOnlyList<SlideElement> elements = Build(document);
        SlideShapeElement[] shapes = elements.OfType<SlideShapeElement>().ToArray();
        Assert(shapes.Length == kinds.Length, "Every shape leaves as a shape element.");
        Assert(
            shapes.Select(shape => shape.Kind).SequenceEqual(kinds),
            "The shapes keep their kinds, in the order they were drawn.");
        Assert(
            shapes.All(shape => shape.OutlineArgb == Outline) &&
            shapes.Count(shape => shape.FillArgb is not null) == 1 &&
            shapes.Single(shape => shape.FillArgb is not null).FillArgb == ShapeSettings.Tint(0xFFE64B3D),
            "An outline and a translucent fill travel as they are; None stays None.");
        Assert(
            shapes.All(shape => shape.Thickness > 0 && shape.Bounds.Width > 1 && shape.Bounds.Height > 1),
            "A shape lands on the slide with a size and an outline width in slide pixels.");

        SlideLabelElement text = elements.OfType<SlideLabelElement>().First();
        Assert(
            text.Text == label.Text && text.FontFamily == "Georgia" && text.Argb == LabelArgb,
            "A label keeps its words, its font, and its color.");
        Assert(
            text is { AngleDegrees: 45, Bold: true, Italic: false, Underline: true },
            "The angle and the style of a label reach the slide.");

        // The element carries the layout rectangle before the turn, which is wider
        // than the box the document indexes is tall at 45 degrees.
        Assert(
            text.Bounds.Width > text.Bounds.Height &&
            Math.Abs(text.Bounds.Width / text.Bounds.Height - (label.LayoutWidth / label.LayoutHeight)) < 0.01,
            "A label's rectangle is the layout it was measured at, not its turned box.");

        SlideConnectorElement[] connectors = elements.OfType<SlideConnectorElement>().ToArray();
        Assert(connectors.Length == 2, "Both connectors leave as connector elements.");
        SlideConnectorElement curve = connectors[0];
        SlideConnectorElement straight = connectors[1];
        Assert(
            curve is { Kind: ConnectorKind.CurvedArrow, FirstControl: not null, SecondControl: not null } &&
            straight is { Kind: ConnectorKind.Line, FirstControl: null, SecondControl: null },
            "A curve hands over its control points; a straight line has none to hand over.");
        Assert(
            curve.Argb == Outline && curve.Thickness > 0 && straight.Thickness > 0,
            "A connector keeps its color and takes a width in slide pixels.");

        // The curve leaves the side it is bound to, so its first control point is to
        // the right of where it starts.
        Assert(
            curve.FirstControl!.Value.X > curve.Start.X,
            "A curve bound to a right-hand side leaves it to the right.");

        // The box holds the whole cubic, which is what the writers place it in.
        Assert(
            curve.Bounds.X <= Math.Min(curve.Start.X, curve.End.X) + 0.01 &&
            curve.Bounds.X + curve.Bounds.Width >= curve.FirstControl.Value.X - 0.01,
            "A connector's box holds its ends and its control points.");
        Assert(
            straight.End.X < straight.Start.X && straight.End.Y < straight.Start.Y,
            "A line that runs right to left and bottom to top keeps that direction, for the flips.");

        // The turn is the angle and nothing else: the same words upright measure the
        // same rectangle, and the writers are the ones that turn it.
        SlideLabelElement upright = elements.OfType<SlideLabelElement>().Last();
        Assert(
            upright.AngleDegrees == 0 &&
            Math.Abs(upright.Bounds.Width - text.Bounds.Width) < 0.01 &&
            Math.Abs(upright.Bounds.Height - text.Bounds.Height) < 0.01,
            "The same label upright is the same rectangle, without the angle.");
    }

    /// <summary>
    /// The picture over the page is for what no element carries. A deck takes the
    /// ink that way; a vector page draws the ink itself and needs no picture at all.
    /// </summary>
    private static void CheckWhatIsLeftForThePicture()
    {
        var document = new BoardDocument();
        document.AddObject(Shape(document, ShapeKind.Ellipse, 0));
        document.AddObject(Label(document, angleDegrees: 0));
        document.AddObject(ConnectorBoardObject.Create(
            Guid.NewGuid(),
            document.NextZIndex,
            ConnectorKind.Arrow,
            new PointD(0, 0),
            new PointD(200, 160),
            Outline,
            ConnectorBoardObject.DefaultThickness));

        Assert(
            Build(document).OfType<SlideImageElement>().Any() == false,
            "Shapes, labels, and connectors alone leave no picture over the slide.");
        Assert(
            Build(document, inkAsStrokes: true).OfType<SlideImageElement>().Any() == false,
            "And none over a vector page either.");

        document.AddObject(InkStrokeObject.Create(
            [
                new InkPoint(new PointD(0, 0), 0.5f, 1),
                new InkPoint(new PointD(120, 80), 0.5f, 2),
            ],
            new PenStyle(0xFF2563EB, 6),
            document.NextZIndex));

        Assert(
            Build(document).OfType<SlideImageElement>().Count() == 1,
            "The ink still goes over a deck's slide as one picture.");
        Assert(
            Build(document, inkAsStrokes: true) is var vector &&
            !vector.OfType<SlideImageElement>().Any() &&
            vector.OfType<SlideInkElement>().Single().Strokes.Count == 1,
            "A vector page draws the ink as strokes instead.");
    }

    private static IReadOnlyList<SlideElement> Build(BoardDocument document, bool inkAsStrokes = false)
    {
        IReadOnlyList<ExportArea> areas = BoardExporter.Areas(
            document,
            new ExportSettings { PageModel = ExportPageModel.WholeBoard },
            null);
        Assert(areas.Count == 1, "A whole-board export is one area.");
        return EditableSlide.Build(document, areas[0], PixelWidth, PixelHeight, null, inkAsStrokes);
    }

    private static ShapeBoardObject Shape(BoardDocument document, ShapeKind kind, int column) => new(
        Guid.NewGuid(),
        document.NextZIndex,
        new RectD(column * 200, 0, 160, 120),
        kind,
        Outline,
        kind == ShapeKind.Ellipse ? ShapeSettings.Tint(0xFFE64B3D) : null,
        ShapeBoardObject.DefaultThickness);

    private static FreeTextBoardObject Label(BoardDocument document, double angleDegrees, PointD? center = null)
    {
        const string Text = "Design objects";
        System.Windows.Size layout = LabelVisual.Measure(Text, "Georgia", 32, bold: true, italic: false, 1);
        return FreeTextBoardObject.Create(
            Guid.NewGuid(),
            document.NextZIndex,
            center ?? new PointD(200, 400),
            Text,
            "Georgia",
            32,
            LabelArgb,
            bold: true,
            italic: false,
            underline: true,
            angleDegrees,
            layout.Width,
            layout.Height);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
