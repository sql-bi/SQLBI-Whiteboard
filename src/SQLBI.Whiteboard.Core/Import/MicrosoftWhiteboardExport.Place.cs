using System.Text;
using SQLBI.Whiteboard.Core.Geometry;
using SQLBI.Whiteboard.Core.Model;
using SQLBI.Whiteboard.Core.Settings;

namespace SQLBI.Whiteboard.Core.Import;

/// <summary>
/// What an export becomes on the board: the objects in drawing order, the pictures
/// they refer to, and the rectangle they cover.
/// </summary>
public sealed record MicrosoftWhiteboardPlacement(
    IReadOnlyList<BoardObject> Objects,
    IReadOnlyList<BoardAsset> Assets,
    RectD Bounds);

public sealed partial class MicrosoftWhiteboardExport
{
    /// <summary>
    /// How near a connector's end has to be to a shape's box to be bound to it.
    /// </summary>
    private const double BindingTolerance = 6;

    private const double CommentMargin = 96;

    /// <summary>
    /// The objects that go on the board. With nothing on the board the export keeps
    /// its own layout with its top-left corner at the origin. Otherwise it goes below
    /// the existing content, aligned with its left edge, as a PowerPoint deck does.
    /// A stroke that touches exactly one imported container travels with it, which is
    /// the rule for a stroke drawn by hand, and a connector that ends on a shape is
    /// bound to it.
    /// </summary>
    public MicrosoftWhiteboardPlacement Place(RectD? existingContent, int firstZIndex, IBoardTextMeasure measure)
    {
        ArgumentNullException.ThrowIfNull(measure);
        var built = new BoardObject?[Items.Count];
        var assets = new List<BoardAsset>();
        for (var index = 0; index < Items.Count; index++)
        {
            var zIndex = firstZIndex + index;
            switch (Items[index])
            {
                case MicrosoftWhiteboardImage image:
                    var assetId = Guid.NewGuid().ToString("N");
                    assets.Add(new BoardAsset(assetId, $"image{assets.Count + 1}{image.Extension}", image.ContentType, image.Bytes));
                    built[index] = new ImageBoardObject(Guid.NewGuid(), zIndex, image.Bounds, assetId);
                    break;
                case MicrosoftWhiteboardStroke stroke:
                    built[index] = ToInk(stroke, default, zIndex);
                    break;
                case MicrosoftWhiteboardShape shape:
                    built[index] = ToShape(shape, zIndex);
                    break;
                case MicrosoftWhiteboardLabel label:
                    built[index] = ToLabel(label, zIndex, measure);
                    break;
                case MicrosoftWhiteboardTextContainer container:
                    var height = measure.TextContainerHeight(container.Markdown, TextLanguageIds.Markdown, container.Width);
                    built[index] = new TextBoardObject(
                        Guid.NewGuid(),
                        zIndex,
                        new RectD(container.TopLeft.X, container.TopLeft.Y, container.Width, Math.Max(1, height)),
                        container.Title,
                        container.Markdown,
                        LanguageId: TextLanguageIds.Markdown);
                    break;
            }
        }

        // Connectors are bound once every shape exists, because one may end on a
        // shape that comes after it in drawing order.
        var shapes = built.OfType<ShapeBoardObject>().ToArray();
        for (var index = 0; index < Items.Count; index++)
        {
            if (Items[index] is MicrosoftWhiteboardConnector connector)
            {
                built[index] = ToConnector(connector, firstZIndex + index, shapes);
            }
        }

        PlaceComments(built);
        var objects = built.OfType<BoardObject>().ToList();

        var containers = objects.Where(item => item is IBoardContainer).ToArray();
        for (var index = 0; index < objects.Count; index++)
        {
            if (objects[index] is InkStrokeObject ink && SingleTouched(ink, containers) is { } carrier)
            {
                objects[index] = ink with { ContainerId = carrier.Id };
            }
        }

        if (objects.Count == 0)
        {
            return new MicrosoftWhiteboardPlacement(objects, assets, RectD.Empty);
        }

        var left = objects.Min(item => item.Bounds.Left);
        var top = objects.Min(item => item.Bounds.Top);
        var sourceBounds = new RectD(
            left,
            top,
            objects.Max(item => item.Bounds.Right) - left,
            objects.Max(item => item.Bounds.Bottom) - top);
        var target = existingContent is { } content
            ? new PointD(content.Left, content.Bottom + Gap)
            : new PointD(0, 0);
        var offset = target - new PointD(left, top);
        return new MicrosoftWhiteboardPlacement(
            objects.Select(item => Translate(item, offset)).ToArray(),
            assets,
            sourceBounds.Translate(offset));
    }

    /// <summary>
    /// A comment in Microsoft Whiteboard is a small pin, and a container in its place
    /// would cover the object it is about. Comments go in a column to the right of
    /// everything else instead, each at its pin's height, pushed down where the one
    /// above would overlap it.
    /// </summary>
    private void PlaceComments(BoardObject?[] built)
    {
        var comments = Enumerable.Range(0, Items.Count)
            .Where(index => Items[index] is MicrosoftWhiteboardTextContainer { Title: CommentTitle })
            .ToArray();
        var others = built.Where((item, index) => item is not null && !comments.Contains(index)).ToArray();
        if (comments.Length == 0 || others.Length == 0)
        {
            return;
        }

        var left = others.Max(item => item!.Bounds.Right) + CommentMargin;
        var next = double.MinValue;
        foreach (var index in comments.OrderBy(index => built[index]!.Bounds.Top))
        {
            var comment = built[index]!;
            var top = Math.Max(comment.Bounds.Top, next);
            built[index] = comment with { Bounds = new RectD(left, top, comment.Bounds.Width, comment.Bounds.Height) };
            next = top + comment.Bounds.Height + (CommentMargin / 4);
        }
    }

    /// <summary>
    /// The width of the ink as a pen style and a pressure per point. WPF draws a point
    /// at the style's thickness times 0.25 + 1.5 × pressure, so pressure 0.5 is the
    /// thickness itself. The thickness is the stroke's typical width, raised when the
    /// widest point would need a pressure above 1.
    /// </summary>
    public static InkStrokeObject ToInk(MicrosoftWhiteboardStroke stroke, PointD offset, int zIndex)
    {
        ArgumentNullException.ThrowIfNull(stroke);
        if (stroke.Kind == PenKind.Highlighter)
        {
            // A highlighter is drawn with a tip twice as wide as it is tall and at a
            // fixed opacity, so the tip's height carries the width and the color is
            // made opaque.
            var width = Median(stroke.Widths);
            var style = new PenStyle(stroke.Argb | 0xFF000000, Math.Max(0.5, width / 2), PenKind.Highlighter);
            return InkStrokeObject.Create(
                stroke.Points.Select((point, index) => new InkPoint(point + offset, 0.5f, index)),
                style,
                zIndex);
        }

        var typical = Median(stroke.Widths);
        var widest = stroke.Widths.Count == 0 ? typical : stroke.Widths.Max();
        var thickness = Math.Max(0.1, Math.Max(typical, widest / 1.75));
        return InkStrokeObject.Create(
            stroke.Points.Select((point, index) => new InkPoint(
                point + offset,
                (float)Math.Clamp(((stroke.Widths[index] / thickness) - 0.25) / 1.5, 0, 1),
                index)),
            new PenStyle(stroke.Argb, thickness, PenKind.Pen),
            zIndex);
    }

    private static ShapeBoardObject ToShape(MicrosoftWhiteboardShape shape, int zIndex)
    {
        var bounds = new RectD(
            shape.Center.X - (shape.Width / 2),
            shape.Center.Y - (shape.Height / 2),
            shape.Width,
            shape.Height);
        var created = ShapeBoardObject.Create(
            Guid.NewGuid(),
            zIndex,
            bounds,
            shape.Kind,
            shape.OutlineArgb,
            shape.FillArgb,
            shape.Thickness,
            shape.AngleDegrees);
        return shape.Text.Length == 0
            ? created
            : created with
            {
                Text = shape.Text,
                FontFamily = shape.Font.Family,
                FontSize = LabelStyles.ClampFontSize(shape.Font.Size),
                TextArgb = shape.Font.Argb,
                Bold = shape.Font.Bold,
                Italic = shape.Font.Italic,
                Underline = shape.Font.Underline,
            };
    }

    /// <summary>
    /// A label wraps where the text box wrapped, because a label keeps the lines it is
    /// given. Its top-left corner is the box's corner plus the box's padding, turned
    /// with the box, and a box that centers its text moves it in by half the room left.
    /// </summary>
    private static FreeTextBoardObject ToLabel(MicrosoftWhiteboardLabel label, int zIndex, IBoardTextMeasure measure)
    {
        var font = label.Font;
        var fontSize = LabelStyles.ClampFontSize(font.Size * label.Scale);
        var scale = fontSize / Math.Max(0.000001, font.Size);
        var text = Wrap(label.Text, label.WrapWidth * scale, font, fontSize, measure);
        var (width, height) = measure.Label(text, font.Family, fontSize, font.Bold, font.Italic);
        var insetX = label.CenteredWidth is { } box
            ? Math.Max(label.Inset.X, (box - (width / scale)) / 2)
            : label.Inset.X;
        var topLeft = label.Origin + RotatedRectangle.Rotate(new PointD(insetX * scale, label.Inset.Y * scale), label.AngleDegrees);
        return FreeTextBoardObject.Create(
            Guid.NewGuid(),
            zIndex,
            RotatedRectangle.CenterFromTopLeft(topLeft, width, height, label.AngleDegrees),
            text,
            font.Family,
            fontSize,
            font.Argb,
            font.Bold,
            font.Italic,
            font.Underline,
            label.AngleDegrees,
            width,
            height);
    }

    /// <summary>
    /// Breaks each line at spaces so that no line is wider than the box allowed. A
    /// word wider than that on its own stays whole on its line.
    /// </summary>
    internal static string Wrap(string text, double maximumWidth, MicrosoftWhiteboardFont font, double fontSize, IBoardTextMeasure measure)
    {
        var result = new StringBuilder(text.Length + 16);
        var lines = text.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (index > 0)
            {
                result.Append('\n');
            }

            var line = new StringBuilder();
            foreach (var word in lines[index].Split(' '))
            {
                var candidate = line.Length == 0 ? word : $"{line} {word}";
                if (line.Length > 0 &&
                    measure.Label(candidate, font.PageFamily ?? font.Family, fontSize, font.Bold, font.Italic).Width > maximumWidth)
                {
                    result.Append(line).Append('\n');
                    line.Clear().Append(word);
                }
                else
                {
                    line.Clear().Append(candidate);
                }
            }

            result.Append(line);
        }

        return result.ToString();
    }

    /// <summary>
    /// An end on the edge of an upright shape is bound to it at that point, as a
    /// fraction of the shape's box, so the connector follows the shape. A turned shape
    /// is left alone, because its fractions are measured in its own turned box.
    /// </summary>
    private static ConnectorBoardObject ToConnector(MicrosoftWhiteboardConnector connector, int zIndex, IReadOnlyList<ShapeBoardObject> shapes)
    {
        ConnectorAnchor? Bind(PointD point)
        {
            var shape = shapes
                .Where(candidate => candidate.AngleDegrees == 0 &&
                                    candidate.Bounds.Inflate(BindingTolerance + connector.Thickness).Contains(point) &&
                                    NearEdge(candidate.Bounds, point, BindingTolerance + connector.Thickness))
                .MinBy(candidate => candidate.Bounds.Width * candidate.Bounds.Height);
            return shape is null
                ? null
                : ConnectorAnchor.Normalize(
                    shape.Id,
                    (point.X - shape.Bounds.Left) / Math.Max(0.000001, shape.Bounds.Width),
                    (point.Y - shape.Bounds.Top) / Math.Max(0.000001, shape.Bounds.Height));
        }

        return ConnectorBoardObject.Create(
            Guid.NewGuid(),
            zIndex,
            connector.Arrow ? ConnectorKind.Arrow : ConnectorKind.Line,
            connector.Start,
            connector.End,
            connector.Argb,
            connector.Thickness,
            Bind(connector.Start),
            Bind(connector.End));
    }

    private static bool NearEdge(RectD box, PointD point, double tolerance) =>
        Math.Abs(point.X - box.Left) <= tolerance ||
        Math.Abs(point.X - box.Right) <= tolerance ||
        Math.Abs(point.Y - box.Top) <= tolerance ||
        Math.Abs(point.Y - box.Bottom) <= tolerance;

    private static BoardObject Translate(BoardObject item, PointD offset) => item switch
    {
        InkStrokeObject ink => ink with
        {
            Bounds = ink.Bounds.Translate(offset),
            Points = ink.Points.Select(point => point with { Position = point.Position + offset }).ToArray(),
        },
        ConnectorBoardObject connector => connector with
        {
            Bounds = connector.Bounds.Translate(offset),
            Start = connector.Start + offset,
            End = connector.End + offset,
        },
        _ => item with { Bounds = item.Bounds.Translate(offset) },
    };

    private static BoardObject? SingleTouched(InkStrokeObject stroke, IReadOnlyList<BoardObject> containers)
    {
        BoardObject? match = null;
        foreach (var container in containers)
        {
            if (!stroke.Touches(container.Bounds))
            {
                continue;
            }

            if (match is not null)
            {
                return null;
            }

            match = container;
        }

        return match;
    }
}
