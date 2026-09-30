using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using SQLBI.Whiteboard.Core.Geometry;
using SQLBI.Whiteboard.Core.Model;
using SQLBI.Whiteboard.Core.Settings;

namespace SQLBI.Whiteboard.Core.Import;

/// <summary>
/// Reads the ZIP that Microsoft Whiteboard writes when a board is exported: one HTML
/// page that draws the board, and a JSON file of comment threads. The page is the
/// only description of the board it contains, so the importer reads the elements the
/// page draws with, one object per element that carries a whiteboard type. An object
/// with no counterpart is counted in <see cref="Skipped"/>. The format is not
/// documented; docs/microsoft-whiteboard-import.md records what was found in it.
/// </summary>
public sealed partial class MicrosoftWhiteboardExport
{
    public const string ArchiveExtension = ".zip";

    /// <summary>
    /// Between an imported board and what was on the board before it.
    /// </summary>
    public const double Gap = 192;

    private const string CommentsSuffix = "-comments.json";
    private const string CommentTitle = "Comment";
    private const string ListTitle = "List";

    /// <summary>
    /// A table cell's border, which the web client draws over the grid line.
    /// </summary>
    private const double TableBorder = 4;

    /// <summary>
    /// The largest size a sticker's caption is drawn at before it shrinks to fit.
    /// </summary>
    private const double StickerCaptionFontSize = 30;

    /// <summary>
    /// A vertical list's columns in the web client: the icon, the text, the text
    /// beside an Assigned to column, that column, and the likes.
    /// </summary>
    private const double ListIconWidth = 44;
    private const double ListTextWidth = 284;
    private const double ListAssignedTextWidth = 170 + 194;
    private const double ListLikesWidth = 76;
    private const string CanvasMarker = "id=\"canvasContent\"";

    /// <summary>
    /// A text box keeps its text this far from its edges, and wraps at its maximum
    /// width less twice this.
    /// </summary>
    private const double TextBoxPadding = 16;

    /// <summary>
    /// A sticky note has a bar this tall above its text, for the author's name.
    /// </summary>
    private const double NoteTitleBar = 40;

    /// <summary>
    /// A note grid lays its notes out on this pitch, starting this far in and down
    /// from its corner, below a title row. Measured on an export rendered by Edge.
    /// </summary>
    private const double GridPitch = 320;
    private const double GridLeft = 17;
    private const double GridTop = 81;
    private const double GridNoteWidth = 304;
    private const double GridNoteHeight = 305;

    private const uint Black = 0xFF000000;
    private const uint Transparent = 0x00000000;

    private static readonly Dictionary<string, uint> NoteColors = new(StringComparer.Ordinal)
    {
        ["paleYellowGradient"] = 0xFFFEE15A,
        ["softOrangeGradient"] = 0xFFFCCD7A,
        ["paleOrangeGradient"] = 0xFFFFAB7C,
        ["softRedGradient"] = 0xFFF18992,
        ["lightPinkGradient"] = 0xFFEA99C7,
        ["paleGreenGradient"] = 0xFFCBE59C,
        ["softCyanGradient"] = 0xFFB4E8CA,
        ["softBlueGradient"] = 0xFF99C9EF,
        ["paleBlueGradient"] = 0xFFB7C3FC,
        ["paleVioletGradient"] = 0xFFDC9BFF,
        ["lightGrayGradient"] = 0xFFE6E6E6,
        ["grayGradient"] = 0xFFC6C6C6,
    };

    private MicrosoftWhiteboardExport(
        string name,
        IReadOnlyList<MicrosoftWhiteboardItem> items,
        IReadOnlyDictionary<string, int> skipped)
    {
        Name = name;
        Items = items;
        Skipped = skipped;
    }

    /// <summary>
    /// The board's name, taken from the page inside the archive.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// What the board holds, in drawing order, bottom first.
    /// </summary>
    public IReadOnlyList<MicrosoftWhiteboardItem> Items { get; }

    public IEnumerable<MicrosoftWhiteboardStroke> Strokes => Items.OfType<MicrosoftWhiteboardStroke>();

    public IEnumerable<MicrosoftWhiteboardImage> Images => Items.OfType<MicrosoftWhiteboardImage>();

    /// <summary>
    /// What was left out, by the name the person would recognize.
    /// </summary>
    public IReadOnlyDictionary<string, int> Skipped { get; }

    /// <summary>
    /// True for a ZIP holding one HTML page and its comments file, which is the shape
    /// of every Microsoft Whiteboard export. Only the archive's directory is read.
    /// </summary>
    public static bool IsExport(string? path)
    {
        if (!string.Equals(Path.GetExtension(path), ArchiveExtension, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(path))
        {
            return false;
        }

        try
        {
            using var archive = ZipFile.OpenRead(path);
            return FindPage(archive) is not null;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static MicrosoftWhiteboardExport Read(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var (page, comments) = FindPage(archive) ??
            throw new InvalidDataException($"{Path.GetFileName(path)} is not a Microsoft Whiteboard export.");
        using var pageReader = new StreamReader(page.Open(), Encoding.UTF8);
        var html = pageReader.ReadToEnd();
        using var commentsReader = new StreamReader(comments.Open(), Encoding.UTF8);
        return Parse(html, Path.GetFileNameWithoutExtension(page.Name), commentsReader.ReadToEnd());
    }

    public static MicrosoftWhiteboardExport Parse(string html, string name, string? commentsJson = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        var start = html.IndexOf(CanvasMarker, StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidDataException("The page does not contain a Microsoft Whiteboard canvas.");
        }

        var threads = ReadCommentThreads(commentsJson);
        var items = new List<MicrosoftWhiteboardItem>();
        var skipped = new SortedDictionary<string, int>(StringComparer.Ordinal);
        void Skip(string what, int count = 1)
        {
            if (count > 0)
            {
                skipped[what] = skipped.GetValueOrDefault(what) + count;
            }
        }

        foreach (var (anchor, tags) in SplitAnchors(HtmlTags.StartTags(html, start)))
        {
            switch (anchor.Type)
            {
                case "InkGroup":
                    items.AddRange(ReadInkGroup(anchor, tags));
                    break;
                case "FluidImage":
                case "AzureImage":
                case "AzureGif":
                case "DocumentPage":
                case "ReactionStickers":
                    if (ReadImage(anchor, tags) is { } image)
                    {
                        items.Add(image);
                    }

                    break;
                case "Shape":
                    if (ReadShape(anchor, tags) is { } shape)
                    {
                        items.Add(shape);
                    }

                    break;
                case "LegacyEllipse":
                case "LegacyPolygon":
                    if (ReadLegacyShape(anchor, tags) is { } legacy)
                    {
                        items.Add(legacy);
                    }

                    break;
                case "Note":
                    items.Add(ReadNote(anchor, tags, 0, tags.Count, default));
                    break;
                case "GridList":
                    items.AddRange(ReadGrid(anchor, tags));
                    break;
                case "PlainText":
                    if (ReadLabel(anchor, tags) is { } label)
                    {
                        items.Add(label);
                    }

                    break;
                case "Connector":
                    if (ReadConnector(anchor, tags) is { } connector)
                    {
                        items.Add(connector);
                    }

                    break;
                case "VerticalList":
                case "VerticalBulletList":
                case "VerticalCheckboxList":
                case "UnknownList":
                    if (ReadList(anchor, tags) is { } list)
                    {
                        items.Add(list);
                    }

                    break;
                case "Hyperlink":
                    if (ReadLink(anchor, tags) is { } link)
                    {
                        items.Add(link);
                    }

                    break;
                case "LegacySticker":
                    items.AddRange(ReadSticker(anchor, tags));
                    break;
                case "Unknown":
                case "Table":
                    if (tags.Any(tag => tag.Name == "div" && tag.HasClass("inkTableContainer")))
                    {
                        items.AddRange(ReadTable(anchor, tags));
                    }
                    else
                    {
                        Skip(anchor.Type);
                    }

                    break;
                case "CommentThread":
                    if (ReadComment(anchor, tags, threads) is { } comment)
                    {
                        items.Add(comment);
                    }

                    break;
                default:
                    Skip(anchor.Type);
                    break;
            }

            Skip("Reactions on notes", tags.Count(tag => tag.Name == "button" && tag.HasClass("ReactionPill")));
        }

        return new MicrosoftWhiteboardExport(name, items, skipped);
    }

    private static (ZipArchiveEntry Page, ZipArchiveEntry Comments)? FindPage(ZipArchive archive)
    {
        var pages = archive.Entries
            .Where(entry => entry.FullName.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (pages.Length != 1)
        {
            return null;
        }

        var name = Path.GetFileNameWithoutExtension(pages[0].Name) + CommentsSuffix;
        var comments = archive.Entries.FirstOrDefault(entry =>
            string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));
        return comments is null ? null : (pages[0], comments);
    }

    /// <summary>
    /// The page's objects with the tags that belong to each. An object starts at a
    /// div with the anchor class and a whiteboard type, and runs to the next such div.
    /// The page ends with elements that carry the anchor class but no object, such as
    /// the pool that draws other people's ink as it arrives, and those end the last one.
    /// </summary>
    private static IEnumerable<(Anchor Anchor, List<HtmlTag> Tags)> SplitAnchors(IEnumerable<HtmlTag> tags)
    {
        Anchor? anchor = null;
        var body = new List<HtmlTag>();
        foreach (var tag in tags)
        {
            if (tag.Name == "div" && tag.HasClass("anchor"))
            {
                if (anchor is not null)
                {
                    yield return (anchor, body);
                }

                anchor = tag.Attribute("data-whiteboard-type") is { } type ? Anchor.From(tag, type) : null;
                body = [];
                continue;
            }

            if (anchor is not null)
            {
                body.Add(tag);
            }
        }

        if (anchor is not null)
        {
            yield return (anchor, body);
        }
    }

    private static MicrosoftWhiteboardImage? ReadImage(Anchor anchor, List<HtmlTag> tags)
    {
        var box = tags.FirstOrDefault(tag => tag.Name == "div" && tag.HasClass("imageComponent"));
        var img = tags.FirstOrDefault(tag => tag.Name == "img");
        var width = StyleLength(box?.Attribute("style"), "width") ?? 0;
        var height = StyleLength(box?.Attribute("style"), "height") ?? 0;
        if (img is null || width <= 0 || height <= 0)
        {
            return null;
        }

        if (DecodeDataUri(img.Attribute("src")) is not { } bytes)
        {
            return null;
        }

        // The page labels most pictures text/plain, so the bytes say what it is.
        var (contentType, extension) = SniffImage(bytes);
        if (contentType is null)
        {
            return null;
        }

        // A picture is centered on its anchor and scaled around that point.
        var center = anchor.ToCanvas(default);
        var scaledWidth = width * anchor.Scale;
        var scaledHeight = height * anchor.Scale;
        var angle = anchor.AngleDegrees;
        if (Math.Abs(Math.IEEERemainder(angle, 360)) < 0.5)
        {
            return new MicrosoftWhiteboardImage(
                new RectD(center.X - (scaledWidth / 2), center.Y - (scaledHeight / 2), scaledWidth, scaledHeight),
                bytes,
                contentType,
                extension);
        }

        // An image container cannot turn, so a turned picture is wrapped in an SVG
        // that draws it turned, sized to the box the turned picture covers.
        var radians = angle * Math.PI / 180;
        var cos = Math.Abs(Math.Cos(radians));
        var sin = Math.Abs(Math.Sin(radians));
        var boxWidth = (scaledWidth * cos) + (scaledHeight * sin);
        var boxHeight = (scaledWidth * sin) + (scaledHeight * cos);
        var svg = TurnedPicture(bytes, contentType, scaledWidth, scaledHeight, boxWidth, boxHeight, angle);
        return new MicrosoftWhiteboardImage(
            new RectD(center.X - (boxWidth / 2), center.Y - (boxHeight / 2), boxWidth, boxHeight),
            svg,
            DroppedFileImport.SvgContentType,
            DroppedFileImport.SvgExtension);
    }

    internal static byte[] TurnedPicture(
        byte[] bytes,
        string contentType,
        double width,
        double height,
        double boxWidth,
        double boxHeight,
        double angleDegrees)
    {
        static string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        var markup =
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" " +
            $"width=\"{N(boxWidth)}\" height=\"{N(boxHeight)}\" viewBox=\"0 0 {N(boxWidth)} {N(boxHeight)}\">" +
            $"<image x=\"{N((boxWidth - width) / 2)}\" y=\"{N((boxHeight - height) / 2)}\" " +
            $"width=\"{N(width)}\" height=\"{N(height)}\" preserveAspectRatio=\"none\" " +
            $"transform=\"rotate({N(angleDegrees)} {N(boxWidth / 2)} {N(boxHeight / 2)})\" " +
            $"xlink:href=\"data:{contentType};base64,{Convert.ToBase64String(bytes)}\"/></svg>";
        return Encoding.UTF8.GetBytes(markup);
    }

    /// <summary>
    /// A shape is centered on its anchor. Its SVG draws the outline around the middle
    /// of a box, and a shape turned by a quarter is sometimes stored as the box turned
    /// that way with the text turned back, so the text's turn is part of the angle.
    /// </summary>
    private static MicrosoftWhiteboardShape? ReadShape(Anchor anchor, List<HtmlTag> tags)
    {
        var svg = tags.FirstOrDefault(tag => tag.Name == "svg" && tag.HasClass("shape"));
        var group = tags.FirstOrDefault(tag => tag.Name == "g" && tag.Attribute("fill") is not null);
        var path = tags.FirstOrDefault(tag => tag.Name == "path");
        if (svg is null || group is null || path?.Attribute("d") is not { } data)
        {
            return null;
        }

        var width = Numbers(svg.Attribute("width")).FirstOrDefault();
        var height = Numbers(svg.Attribute("height")).FirstOrDefault();
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var turn = Numbers(StyleValue(
            tags.FirstOrDefault(tag => tag.HasClass("textBoxContainer"))?.Attribute("style"),
            "transform")).FirstOrDefault();
        var quarter = Math.Abs(Math.IEEERemainder(turn, 180)) > 45;
        var shapeText = tags.FirstOrDefault(tag => tag.HasClass("shapeText"));
        var fontSize = StyleLength(shapeText?.Attribute("style"), "font-size") ?? 20;
        var core = tags.FirstOrDefault(tag => tag.HasClass("textBoxCore"));

        return new MicrosoftWhiteboardShape(
            anchor.ToCanvas(default),
            (quarter ? height : width) * anchor.Scale,
            (quarter ? width : height) * anchor.Scale,
            RotatedRectangle.NormalizeAngle(anchor.AngleDegrees + turn),
            ClassifyShape(data, width, height),
            PaintOrNull(group.Attribute("stroke")) ?? Transparent,
            PaintOrNull(group.Attribute("fill")),
            CssLength(group.Attribute("stroke-width"), 2) * anchor.Scale,
            ReadText(tags, 0, tags.Count),
            ReadFont(core?.Attribute("style"), fontSize * anchor.Scale, defaultBold: true));
    }

    /// <summary>
    /// A shape from an older version of the app, drawn by a plain SVG ellipse or polygon
    /// with its outline in the element's style. An ellipse is centered on its anchor and
    /// its radii can disagree with the SVG's box, so the radii are what the page draws.
    /// A polygon's corners start at its anchor.
    /// </summary>
    private static MicrosoftWhiteboardShape? ReadLegacyShape(Anchor anchor, List<HtmlTag> tags)
    {
        var svg = tags.FirstOrDefault(tag => tag.Name == "svg" && tag.HasClass("shape"));
        var element = tags.FirstOrDefault(tag => tag.Name is "ellipse" or "polygon");
        if (svg is null || element is null)
        {
            return null;
        }

        PointD center;
        double width;
        double height;
        ShapeKind kind;
        if (element.Name == "ellipse")
        {
            var boxWidth = Numbers(svg.Attribute("width")).FirstOrDefault();
            var boxHeight = Numbers(svg.Attribute("height")).FirstOrDefault();
            var cx = Numbers(element.Attribute("cx")).FirstOrDefault();
            var cy = Numbers(element.Attribute("cy")).FirstOrDefault();
            center = new PointD(cx - (boxWidth / 2), cy - (boxHeight / 2));
            width = 2 * Numbers(element.Attribute("rx")).FirstOrDefault();
            height = 2 * Numbers(element.Attribute("ry")).FirstOrDefault();
            kind = ShapeKind.Ellipse;
        }
        else
        {
            var numbers = Numbers(element.Attribute("points"));
            var corners = new List<PointD>();
            for (var index = 0; index + 1 < numbers.Count; index += 2)
            {
                corners.Add(new PointD(numbers[index], numbers[index + 1]));
            }

            if (corners.Count < 3)
            {
                return null;
            }

            var left = corners.Min(point => point.X);
            var top = corners.Min(point => point.Y);
            width = corners.Max(point => point.X) - left;
            height = corners.Max(point => point.Y) - top;
            center = new PointD(left + (width / 2), top + (height / 2));
            var outline = "M" + string.Join(
                " L",
                corners.Select(point => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{point.X - center.X},{point.Y - center.Y}")));
            kind = ClassifyShape(outline, width, height);
        }

        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var style = element.Attribute("style");
        return new MicrosoftWhiteboardShape(
            anchor.ToCanvas(center),
            width * anchor.Scale,
            height * anchor.Scale,
            anchor.AngleDegrees,
            kind,
            PaintOrNull(StyleValue(style, "stroke") ?? element.Attribute("stroke")) ?? Transparent,
            PaintOrNull(StyleValue(style, "fill") ?? element.Attribute("fill")),
            CssLength(StyleValue(style, "stroke-width") ?? element.Attribute("stroke-width"), 2) * anchor.Scale,
            string.Empty,
            new MicrosoftWhiteboardFont(LabelStyles.DefaultFontFamily, 20, Black, false, false, false));
    }

    /// <summary>
    /// The kind of a shape from the outline it draws, because the name on it is
    /// written in the language of whoever made the board.
    /// </summary>
    internal static ShapeKind ClassifyShape(string data, double width, double height)
    {
        if (data.IndexOfAny(['Q', 'q', 'C', 'c', 'A', 'a']) >= 0)
        {
            return data.IndexOfAny(['L', 'l']) >= 0 ? ShapeKind.Stadium : ShapeKind.Ellipse;
        }

        var numbers = Numbers(data);
        var corners = new List<PointD>();
        for (var index = 0; index + 1 < numbers.Count; index += 2)
        {
            corners.Add(new PointD(numbers[index], numbers[index + 1]));
        }

        if (corners.Count > 1 && corners[0] == corners[^1])
        {
            corners.RemoveAt(corners.Count - 1);
        }

        var halfWidth = width / 2;
        var halfHeight = height / 2;
        return corners.Count switch
        {
            3 => ShapeKind.Triangle,
            5 => ShapeKind.Pentagon,
            7 => ShapeKind.BlockArrow,
            4 when corners.Count(point => Math.Abs(point.Y) < halfHeight * 0.05) >= 2 => ShapeKind.Diamond,
            4 when corners.All(point => Math.Abs(Math.Abs(point.X) - halfWidth) < halfWidth * 0.02) => ShapeKind.Rectangle,
            4 => ShapeKind.Parallelogram,
            _ => ShapeKind.RoundedRectangle,
        };
    }

    /// <summary>
    /// A sticky note becomes a shape in the note's color. Its text sits below the
    /// author bar, so the shape covers both. Offset places a note inside a grid.
    /// </summary>
    private static MicrosoftWhiteboardShape ReadNote(Anchor anchor, List<HtmlTag> tags, int from, int to, PointD offset)
    {
        uint color = NoteColors["paleYellowGradient"];
        double width = GridNoteWidth;
        double height = GridNoteHeight - NoteTitleBar;
        double fontSize = 32;
        string? coreStyle = null;
        for (var index = from; index < to; index++)
        {
            var tag = tags[index];
            if (tag.Name != "div")
            {
                continue;
            }

            if (tag.HasClass("textBoxBackground"))
            {
                var classes = tag.Attribute("class")!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (classes.FirstOrDefault(NoteColors.ContainsKey) is { } key)
                {
                    color = NoteColors[key];
                }
            }
            else if (tag.HasClass("stickyNote"))
            {
                var style = tag.Attribute("style");
                width = StyleLength(style, "width") ?? width;
                height = StyleLength(style, "height") ?? height;
                fontSize = StyleLength(style, "font-size") ?? fontSize;
            }
            else if (tag.HasClass("textBoxCore") && coreStyle is null)
            {
                coreStyle = tag.Attribute("style");
            }
        }

        var fullHeight = height + NoteTitleBar;
        return new MicrosoftWhiteboardShape(
            anchor.ToCanvas(offset + new PointD(width / 2, fullHeight / 2)),
            width * anchor.Scale,
            fullHeight * anchor.Scale,
            anchor.AngleDegrees,
            ShapeKind.Rectangle,
            color,
            color,
            1,
            ReadText(tags, from, to),
            ReadFont(coreStyle, fontSize * anchor.Scale, defaultBold: false));
    }

    /// <summary>
    /// A note grid is a white panel with a title, holding its notes in rows. It
    /// becomes a shape for the panel, a label for the title, and one shape per note.
    /// </summary>
    private static IEnumerable<MicrosoftWhiteboardItem> ReadGrid(Anchor anchor, List<HtmlTag> tags)
    {
        var children = new List<int>();
        for (var index = 0; index < tags.Count; index++)
        {
            if (tags[index].Name == "div" && tags[index].HasClass("listChild"))
            {
                children.Add(index);
            }
        }

        var columnsStyle = tags.FirstOrDefault(tag => tag.HasClass("listChildren"))?.Attribute("style");
        var declared = Numbers(StyleValue(columnsStyle, "grid-template-columns")).FirstOrDefault();
        var columns = declared >= 1 ? (int)declared : 3;
        var rows = Math.Max(1, (children.Count + columns - 1) / columns);
        var width = (2 * GridLeft) + ((columns - 1) * GridPitch) + GridNoteWidth;
        var height = GridTop + ((rows - 1) * GridPitch) + GridNoteHeight + GridLeft - 1;

        yield return new MicrosoftWhiteboardShape(
            anchor.ToCanvas(new PointD(width / 2, height / 2)),
            width * anchor.Scale,
            height * anchor.Scale,
            anchor.AngleDegrees,
            ShapeKind.Rectangle,
            0xFFD1D1D1,
            0xFFFFFFFF,
            anchor.Scale,
            string.Empty,
            new MicrosoftWhiteboardFont(LabelStyles.DefaultFontFamily, 20, Black, false, false, false));

        var title = ReadText(tags, 0, children.Count > 0 ? children[0] : tags.Count);
        if (title.Length > 0)
        {
            yield return new MicrosoftWhiteboardLabel(
                anchor.ToCanvas(default),
                anchor.Scale,
                anchor.AngleDegrees,
                new PointD(GridLeft, 20),
                title,
                new MicrosoftWhiteboardFont(LabelStyles.DefaultFontFamily, 30, Black, true, false, false),
                width - 150,
                null);
        }

        for (var child = 0; child < children.Count; child++)
        {
            var end = child + 1 < children.Count ? children[child + 1] : tags.Count;
            var cell = new PointD(
                GridLeft + (child % columns * GridPitch),
                GridTop + (child / columns * GridPitch));
            yield return ReadNote(anchor, tags, children[child], end, cell);
        }
    }

    /// <summary>
    /// A text box wraps at its maximum width. When it is laid out inside a wider box
    /// with its text centered, the width of that box is what centers it.
    /// </summary>
    private static MicrosoftWhiteboardLabel? ReadLabel(Anchor anchor, List<HtmlTag> tags)
    {
        var boxIndex = tags.FindIndex(tag => tag.Name == "div" && tag.HasClass("plainText"));
        if (boxIndex < 0)
        {
            return null;
        }

        var text = ReadText(tags, boxIndex, tags.Count);
        if (text.Length == 0)
        {
            return null;
        }

        var boxStyle = tags[boxIndex].Attribute("style");
        var wrapper = tags.Take(boxIndex).LastOrDefault(tag =>
            tag.Name == "div" && StyleValue(tag.Attribute("style"), "justify-content") is not null);
        var wrapperStyle = wrapper?.Attribute("style");
        double? centered = string.Equals(StyleValue(wrapperStyle, "justify-content"), "center", StringComparison.Ordinal)
            ? StyleLength(wrapperStyle, "width")
            : null;
        var fontSize = StyleLength(boxStyle, "font-size") ?? 34;
        var maximum = StyleLength(boxStyle, "max-width") ?? 400;
        var core = tags.Skip(boxIndex).FirstOrDefault(tag => tag.HasClass("textBoxCore"));

        return new MicrosoftWhiteboardLabel(
            anchor.ToCanvas(default),
            anchor.Scale,
            anchor.AngleDegrees,
            new PointD(TextBoxPadding, TextBoxPadding),
            text,
            ReadFont(core?.Attribute("style"), fontSize, defaultBold: false),
            Math.Max(1, maximum - (2 * TextBoxPadding)),
            centered);
    }

    /// <summary>
    /// A list of text, bullets, or tasks becomes Markdown titled <c>List</c>, with the
    /// list's own title in bold above one line per item. Tasks show a box that is
    /// crossed or empty, because Markdown here has no task lists and the ticked box
    /// is drawn as an emoji beside a plain empty one. Who a task is
    /// assigned to and its likes are left out. A list is as wide as the web client
    /// lays it out: an icon column, the text, and the likes.
    /// </summary>
    private static MicrosoftWhiteboardTextContainer? ReadList(Anchor anchor, List<HtmlTag> tags)
    {
        var children = new List<int>();
        var titleIndex = -1;
        for (var index = 0; index < tags.Count; index++)
        {
            if (tags[index].Name != "div")
            {
                continue;
            }

            if (tags[index].HasClass("listChild"))
            {
                children.Add(index);
            }
            else if (tags[index].HasClass("verticalListTitle") && titleIndex < 0)
            {
                titleIndex = index;
            }
        }

        var lines = new List<string>();
        var title = titleIndex >= 0
            ? ReadText(tags, titleIndex, children.Count > 0 ? children[0] : tags.Count)
            : string.Empty;
        if (title.Length > 0)
        {
            lines.Add($"**{EscapeMarkdown(title).Replace("\n", "**  \n**", StringComparison.Ordinal)}**");
        }

        var tasks = anchor.Type == "VerticalCheckboxList";
        var bullets = anchor.Type == "VerticalBulletList";
        var items = new List<string>();
        for (var child = 0; child < children.Count; child++)
        {
            var end = child + 1 < children.Count ? children[child + 1] : tags.Count;
            var text = ReadText(tags, children[child], end);
            if (text.Length == 0)
            {
                continue;
            }

            var body = EscapeMarkdown(text).Replace("\n", "  \n", StringComparison.Ordinal);
            if (bullets)
            {
                items.Add($"- {body}");
            }
            else if (tasks)
            {
                var ticked = tags.Skip(children[child]).Take(end - children[child]).Any(tag =>
                    tag.HasClass("checkedListItemIcon") ||
                    (tag.Attribute("data-icon-name")?.StartsWith("CheckmarkCircle", StringComparison.Ordinal) ?? false));
                items.Add($"{(ticked ? "☒" : "☐")} {body}");
            }
            else
            {
                items.Add(body);
            }
        }

        if (lines.Count == 0 && items.Count == 0)
        {
            return null;
        }

        // Bullets make a Markdown list; boxes and plain lines break line by line.
        lines.Add(string.Join(bullets ? "\n" : "  \n", items));
        var assigned = tags.Any(tag => tag.HasClass("assignedUserDisplayName"));
        var width = tasks && assigned ? ListIconWidth + ListAssignedTextWidth + ListLikesWidth : ListIconWidth + ListTextWidth + ListLikesWidth;
        return new MicrosoftWhiteboardTextContainer(
            anchor.ToCanvas(default),
            width * anchor.Scale,
            ListTitle,
            string.Join("\n\n", lines.Where(line => line.Length > 0)));
    }

    /// <summary>
    /// A table drawn with ink in the Windows 10 app. The web client names its type
    /// Unknown and draws it as a CSS grid: each column and row is as long as its
    /// <c>minmax()</c> says, each cell has a 4-pixel border in the table's color that
    /// sits over the grid line, and each cell's ink is a collection of ink groups
    /// placed from the cell's corner, moved by the offset that keeps ink reaching left
    /// of or above the cell inside it. The cells become rectangles, then the ink comes
    /// on top of them.
    /// </summary>
    private static IEnumerable<MicrosoftWhiteboardItem> ReadTable(Anchor anchor, List<HtmlTag> tags)
    {
        var gridIndex = tags.FindIndex(tag => tag.Name == "div" && tag.HasClass("inkTableContainer"));
        var gridStyle = tags[gridIndex].Attribute("style");
        var columns = Numbers(StyleValue(gridStyle, "grid-template-columns"));
        var rows = Numbers(StyleValue(gridStyle, "grid-template-rows"));
        var cells = new List<int>();
        for (var index = gridIndex + 1; index < tags.Count; index++)
        {
            if (tags[index].Name == "div" && tags[index].HasClass("inkCellContainer"))
            {
                cells.Add(index);
            }
        }

        if (columns.Count == 0 || rows.Count == 0)
        {
            yield break;
        }

        var border = cells.Count > 0 ? StyleValue(tags[cells[0]].Attribute("style"), "border") : null;
        var solid = border?.IndexOf("solid", StringComparison.OrdinalIgnoreCase) ?? -1;
        var color = (solid >= 0 ? PaintOrNull(border![(solid + 5)..]) : null) ?? Black;
        var lefts = Starts(columns);
        var tops = Starts(rows);
        for (var row = 0; row < rows.Count; row++)
        {
            for (var column = 0; column < columns.Count; column++)
            {
                yield return new MicrosoftWhiteboardShape(
                    anchor.ToCanvas(new PointD(lefts[column] + (columns[column] / 2), tops[row] + (rows[row] / 2))),
                    columns[column] * anchor.Scale,
                    rows[row] * anchor.Scale,
                    anchor.AngleDegrees,
                    ShapeKind.Rectangle,
                    color,
                    null,
                    TableBorder * anchor.Scale,
                    string.Empty,
                    new MicrosoftWhiteboardFont(LabelStyles.DefaultFontFamily, 20, Black, false, false, false));
            }
        }

        for (var cell = 0; cell < cells.Count && cell / columns.Count < rows.Count; cell++)
        {
            var start = cells[cell];
            var end = cell + 1 < cells.Count ? cells[cell + 1] : tags.Count;
            var shiftStyle = start + 1 < end ? tags[start + 1].Attribute("style") : null;
            var corner = new PointD(
                lefts[cell % columns.Count] + (TableBorder / 2) + (StyleLength(shiftStyle, "left") ?? 0),
                tops[cell / columns.Count] + (TableBorder / 2) + (StyleLength(shiftStyle, "top") ?? 0));
            var groups = Enumerable.Range(start, end - start)
                .Where(index => tags[index].Name == "svg" && tags[index].HasClass("inkGroup"))
                .ToArray();
            for (var group = 0; group < groups.Length; group++)
            {
                // The collection child that places the group is the nearest div before
                // it with a position of its own, after the div that shifts the cell.
                var place = Enumerable.Range(start + 2, Math.Max(0, groups[group] - start - 2))
                    .Reverse()
                    .Select(index => tags[index])
                    .FirstOrDefault(tag => tag.Name == "div" && StyleValue(tag.Attribute("style"), "left") is not null);
                var child = place is null
                    ? new Anchor("InkGroup", default, 1, 0, 0, 1, 0, 0)
                    : Anchor.From(place, "InkGroup");
                var groupEnd = group + 1 < groups.Length ? groups[group + 1] : end;
                foreach (var item in ReadInkGroup(child.Inside(anchor, corner), tags.GetRange(groups[group], groupEnd - groups[group])))
                {
                    yield return item;
                }
            }
        }
    }

    private static double[] Starts(List<double> lengths)
    {
        var starts = new double[lengths.Count];
        for (var index = 1; index < lengths.Count; index++)
        {
            starts[index] = starts[index - 1] + lengths[index - 1];
        }

        return starts;
    }

    /// <summary>
    /// A sticker from the Windows 10 app is a picture with a caption over it. The web
    /// client draws the picture as any other and the caption as a text editor in a box
    /// moved from the picture's corner, at 30 pixels or smaller when it shrinks the text
    /// to fit. The caption becomes a label centered across that box.
    /// </summary>
    private static IEnumerable<MicrosoftWhiteboardItem> ReadSticker(Anchor anchor, List<HtmlTag> tags)
    {
        if (ReadImage(anchor, tags) is { } picture)
        {
            yield return picture;
        }

        var captionIndex = tags.FindIndex(tag => tag.Name == "div" &&
            StyleValue(tag.Attribute("style"), "font-size") is not null &&
            string.Equals(StyleValue(tag.Attribute("style"), "position"), "absolute", StringComparison.OrdinalIgnoreCase));
        var box = tags.FirstOrDefault(tag => tag.Name == "div" && tag.HasClass("imageComponent"));
        if (captionIndex < 0 || box is null)
        {
            yield break;
        }

        var text = ReadText(tags, captionIndex, tags.Count);
        if (text.Length == 0)
        {
            yield break;
        }

        var boxWidth = StyleLength(box.Attribute("style"), "width") ?? 0;
        var boxHeight = StyleLength(box.Attribute("style"), "height") ?? 0;
        var caption = Anchor.From(tags[captionIndex], "Caption");
        var captionStyle = tags[captionIndex].Attribute("style");
        var width = tags.Skip(captionIndex + 1)
            .Select(tag => StyleLength(tag.Attribute("style"), "width"))
            .FirstOrDefault(value => value > 0) ?? boxWidth;
        var fontSize = tags.Skip(captionIndex)
            .Select(tag => StyleLength(tag.Attribute("style"), "font-size"))
            .OfType<double>()
            .Where(value => value > 0)
            .DefaultIfEmpty(StickerCaptionFontSize)
            .Min();
        var core = tags.Skip(captionIndex).FirstOrDefault(tag => tag.HasClass("textBoxCore"))?.Attribute("style");
        yield return new MicrosoftWhiteboardLabel(
            anchor.ToCanvas(new PointD(
                (-boxWidth / 2) + caption.Origin.X + caption.E,
                (-boxHeight / 2) + caption.Origin.Y + caption.F)),
            anchor.Scale,
            anchor.AngleDegrees,
            default,
            text,
            ReadFont($"{core};{captionStyle}", fontSize, defaultBold: false),
            Math.Max(1, width),
            width);
    }

    /// <summary>
    /// A connector's SVG draws its route from the anchor, and its head as a small
    /// path moved to one end. A route with corners is kept as a straight line.
    /// </summary>
    private static MicrosoftWhiteboardConnector? ReadConnector(Anchor anchor, List<HtmlTag> tags)
    {
        var group = tags.FirstOrDefault(tag => tag.Name == "g" && tag.Attribute("stroke") is not null);
        var route = tags.FirstOrDefault(tag => tag.Name == "path" && tag.Attribute("transform") is null);
        if (group is null || route?.Attribute("d") is not { } data)
        {
            return null;
        }

        var numbers = Numbers(data);
        if (numbers.Count < 4)
        {
            return null;
        }

        var start = new PointD(numbers[0], numbers[1]);
        var end = new PointD(numbers[^2], numbers[^1]);
        var head = tags
            .Where(tag => tag.Name == "path" && tag.Attribute("transform") is { } transform &&
                          transform.Contains("translate", StringComparison.Ordinal))
            .Select(tag => Numbers(tag.Attribute("transform")))
            .Where(values => values.Count >= 2)
            .Select(values => (PointD?)new PointD(values[0], values[1]))
            .FirstOrDefault();
        if (head is { } point && Distance(point, start) < Distance(point, end))
        {
            (start, end) = (end, start);
        }

        return new MicrosoftWhiteboardConnector(
            anchor.ToCanvas(start),
            anchor.ToCanvas(end),
            head is not null,
            PaintOrNull(group.Attribute("stroke")) ?? Black,
            CssLength(group.Attribute("stroke-width"), 2.5) * anchor.Scale);
    }

    /// <summary>
    /// A link card becomes a Markdown container holding the link and its description.
    /// The preview picture is left out.
    /// </summary>
    private static MicrosoftWhiteboardTextContainer? ReadLink(Anchor anchor, List<HtmlTag> tags)
    {
        var link = tags.FirstOrDefault(tag => tag.Name == "a" && tag.Attribute("href") is not null);
        if (link is null)
        {
            return null;
        }

        var href = link.Attribute("href")!;
        var title = string.IsNullOrWhiteSpace(link.Text) ? href : link.Text.Trim();
        var description = tags.FirstOrDefault(tag => tag.HasClass("previewCardDescription"))?.Text?.Trim();
        var cardWidth = StyleLength(
            tags.FirstOrDefault(tag => tag.HasClass("previewCardTitleContainer"))?.Attribute("style"),
            "width") ?? 320;
        var markdown = $"[{EscapeMarkdown(title)}]({href})";
        if (!string.IsNullOrWhiteSpace(description))
        {
            markdown += $"\n\n{EscapeMarkdown(description)}";
        }

        return new MicrosoftWhiteboardTextContainer(
            anchor.ToCanvas(default),
            (cardWidth + 22) * anchor.Scale,
            "Link",
            markdown);
    }

    /// <summary>
    /// A comment pin on the page carries its thread's number, and the comments file
    /// holds the thread under that number. The thread becomes a Markdown container
    /// beside the pin. Authors are named; their addresses are left out.
    /// </summary>
    private static MicrosoftWhiteboardTextContainer? ReadComment(
        Anchor anchor,
        List<HtmlTag> tags,
        IReadOnlyDictionary<string, IReadOnlyList<(string Author, string Date, string Body)>> threads)
    {
        const string HintPrefix = "Comment hint:";
        var hint = tags
            .Select(tag => tag.Attribute("aria-label"))
            .FirstOrDefault(label => label is not null && label.StartsWith(HintPrefix, StringComparison.Ordinal));
        if (hint is null || !threads.TryGetValue(hint[HintPrefix.Length..].Trim(), out var comments) || comments.Count == 0)
        {
            return null;
        }

        var markdown = string.Join(
            "\n\n",
            comments.Select(comment =>
                $"**{EscapeMarkdown(comment.Author)}** · {EscapeMarkdown(comment.Date)}\n\n{EscapeMarkdown(comment.Body)}"));
        return new MicrosoftWhiteboardTextContainer(
            anchor.ToCanvas(new PointD(44, 0)),
            360 * anchor.Scale,
            CommentTitle,
            markdown);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<(string Author, string Date, string Body)>> ReadCommentThreads(string? json)
    {
        var threads = new Dictionary<string, IReadOnlyList<(string, string, string)>>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json))
        {
            return threads;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("commentThreads", out var list) || list.ValueKind != JsonValueKind.Array)
            {
                return threads;
            }

            foreach (var thread in list.EnumerateArray())
            {
                if (!thread.TryGetProperty("id", out var id) ||
                    !thread.TryGetProperty("comments", out var comments) ||
                    comments.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var entries = new List<(string, string, string)>();
                foreach (var comment in comments.EnumerateArray())
                {
                    var author = comment.TryGetProperty("author", out var who) && who.TryGetProperty("name", out var named)
                        ? named.GetString() ?? string.Empty
                        : string.Empty;
                    var date = comment.TryGetProperty("displayDate", out var when) ? when.GetString() ?? string.Empty : string.Empty;
                    var body = comment.TryGetProperty("body", out var text) ? text.GetString()?.Trim() ?? string.Empty : string.Empty;
                    entries.Add((author, date, body));
                }

                threads[id.ValueKind == JsonValueKind.Number ? id.GetRawText() : id.GetString() ?? string.Empty] = entries;
            }
        }
        catch (JsonException)
        {
            // A comments file that cannot be read leaves the board without its
            // comments rather than without everything else.
        }

        return threads;
    }

    /// <summary>
    /// The text of an editor on the page: one line per block, from the spans that
    /// carry text. The grey "Add text" of an empty note is a placeholder, not a block.
    /// </summary>
    private static string ReadText(List<HtmlTag> tags, int from, int to)
    {
        var lines = new List<StringBuilder>();
        for (var index = from; index < to; index++)
        {
            var tag = tags[index];
            if (tag.Name == "div" && tag.Attribute("data-block") == "true")
            {
                lines.Add(new StringBuilder());
            }
            else if (tag.Name == "span" && tag.Attribute("data-text") == "true" && lines.Count > 0)
            {
                lines[^1].Append(tag.Text);
            }
        }

        return string.Join("\n", lines.Select(line => line.ToString())).TrimEnd('\n', ' ');
    }

    private static MicrosoftWhiteboardFont ReadFont(string? style, double size, bool defaultBold)
    {
        var weight = StyleValue(style, "font-weight");
        var bold = weight is null
            ? defaultBold
            : weight is "bold" or "bolder" || (Numbers(weight).FirstOrDefault() is var number && number >= 600);
        var families = StyleValue(style, "font-family");
        var family = FontFamilyFor(families);
        var pageFamily = families?.Split(',')[0].Trim().Trim('"', '\'').Trim();
        return new MicrosoftWhiteboardFont(
            family,
            size,
            TryParseColor(StyleValue(style, "color"), out var argb, out _) ? argb : Black,
            bold,
            string.Equals(StyleValue(style, "font-style"), "italic", StringComparison.OrdinalIgnoreCase),
            StyleValue(style, "text-decoration")?.Contains("underline", StringComparison.OrdinalIgnoreCase) == true,
            string.IsNullOrEmpty(pageFamily) || string.Equals(pageFamily, family, StringComparison.OrdinalIgnoreCase)
                ? null
                : pageFamily);
    }

    /// <summary>
    /// The label font nearest to what the page asks for. Microsoft Whiteboard offers
    /// three: Simple (Aptos), Professional, and Handwritten (Segoe Print, or Ink Free
    /// on older boards). A family the labels have is kept as it is.
    /// </summary>
    internal static string FontFamilyFor(string? families)
    {
        var first = families?.Split(',')[0].Trim().Trim('"', '\'').Trim() ?? string.Empty;
        if (LabelStyles.Fonts.FirstOrDefault(font => string.Equals(font, first, StringComparison.OrdinalIgnoreCase)) is { } known)
        {
            return known;
        }

        if (families is not null && families.Contains("Print", StringComparison.OrdinalIgnoreCase))
        {
            return "Segoe Print";
        }

        if (families is not null && families.Contains("ink free", StringComparison.OrdinalIgnoreCase))
        {
            return "Ink Free";
        }

        return first.ToLowerInvariant() switch
        {
            "serif" or "cambria" or "georgia" => "Georgia",
            "monospace" or "consolas" or "courier new" => "Consolas",
            _ => LabelStyles.DefaultFontFamily,
        };
    }

    private static string EscapeMarkdown(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (character is '\\' or '*' or '_' or '[' or ']' or '`' or '#' or '<' or '>')
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static byte[]? DecodeDataUri(string? source)
    {
        const string DataPrefix = "data:";
        const string Base64Marker = ";base64,";
        if (source is null || !source.StartsWith(DataPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var marker = source.IndexOf(Base64Marker, StringComparison.Ordinal);
        if (marker < 0)
        {
            return null;
        }

        try
        {
            return Convert.FromBase64String(source[(marker + Base64Marker.Length)..]);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static (string? ContentType, string Extension) SniffImage(byte[] bytes)
    {
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == (byte)'P' && bytes[2] == (byte)'N' && bytes[3] == (byte)'G')
        {
            return ("image/png", ".png");
        }

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return ("image/jpeg", ".jpg");
        }

        if (bytes.Length >= 6 && bytes[0] == (byte)'G' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F')
        {
            return ("image/gif", ".gif");
        }

        if (bytes.Length >= 2 && bytes[0] == (byte)'B' && bytes[1] == (byte)'M')
        {
            return ("image/bmp", ".bmp");
        }

        if (DroppedFileImport.LooksLikeSvg(bytes))
        {
            return (DroppedFileImport.SvgContentType, DroppedFileImport.SvgExtension);
        }

        return (null, string.Empty);
    }

    /// <summary>
    /// A fill or stroke as a color, or null for none. Fully transparent is none too.
    /// </summary>
    private static uint? PaintOrNull(string? paint) =>
        paint is not null && !paint.Trim().Equals("none", StringComparison.OrdinalIgnoreCase) &&
        TryParseColor(paint, out var argb, out var alpha) && alpha > 0
            ? argb
            : null;

    /// <summary>
    /// A CSS length in pixels. Points are 4/3 of a pixel.
    /// </summary>
    private static double CssLength(string? value, double fallback)
    {
        var number = Numbers(value).FirstOrDefault();
        if (number <= 0)
        {
            return fallback;
        }

        return value!.Contains("pt", StringComparison.OrdinalIgnoreCase) ? number * 4 / 3 : number;
    }

    private static bool TryParseColor(string? fill, out uint argb, out double alpha)
    {
        argb = 0;
        alpha = 1;
        if (fill is null)
        {
            return false;
        }

        var text = fill.Trim();
        if (text.StartsWith('#'))
        {
            var hex = text[1..];
            if (hex.Length == 3)
            {
                hex = string.Concat(hex.Select(digit => new string(digit, 2)));
            }

            if (hex.Length == 6 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            {
                argb = 0xFF000000 | rgb;
                return true;
            }

            return false;
        }

        if (!text.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parts = Numbers(text);
        if (parts.Count < 3)
        {
            return false;
        }

        alpha = parts.Count >= 4 ? Math.Clamp(parts[3], 0, 1) : 1;
        argb = ((uint)Math.Round(alpha * 255) << 24) |
               ((uint)Math.Clamp(parts[0], 0, 255) << 16) |
               ((uint)Math.Clamp(parts[1], 0, 255) << 8) |
               (uint)Math.Clamp(parts[2], 0, 255);
        return true;
    }

    private static List<double> Numbers(string? text)
    {
        var numbers = new List<double>();
        if (string.IsNullOrEmpty(text))
        {
            return numbers;
        }

        var index = 0;
        while (index < text.Length)
        {
            if (!IsNumberStart(text, index))
            {
                index++;
                continue;
            }

            var start = index;
            index++;
            while (index < text.Length &&
                   (char.IsAsciiDigit(text[index]) || text[index] is '.' or 'e' or 'E' ||
                    (text[index] is '-' or '+' && text[index - 1] is 'e' or 'E')))
            {
                index++;
            }

            if (double.TryParse(text.AsSpan(start, index - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                numbers.Add(value);
            }
        }

        return numbers;
    }

    private static bool IsNumberStart(string text, int index) =>
        char.IsAsciiDigit(text[index]) ||
        (text[index] is '-' or '+' or '.' && index + 1 < text.Length &&
         (char.IsAsciiDigit(text[index + 1]) || text[index + 1] == '.'));

    private static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return 1;
        }

        var sorted = values.Order().ToArray();
        return sorted.Length % 2 == 1
            ? sorted[sorted.Length / 2]
            : (sorted[(sorted.Length / 2) - 1] + sorted[sorted.Length / 2]) / 2;
    }

    private static double Distance(PointD a, PointD b) =>
        Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));

    private static double? StyleLength(string? style, string property)
    {
        if (StyleValue(style, property) is not { } value)
        {
            return null;
        }

        var numbers = Numbers(value);
        return numbers.Count > 0 ? numbers[0] : null;
    }

    private static string? StyleValue(string? style, string property)
    {
        if (style is null)
        {
            return null;
        }

        foreach (var declaration in style.Split(';'))
        {
            var colon = declaration.IndexOf(':');
            if (colon > 0 && string.Equals(declaration[..colon].Trim(), property, StringComparison.OrdinalIgnoreCase))
            {
                return declaration[(colon + 1)..].Trim();
            }
        }

        return null;
    }

    /// <summary>
    /// An object's position on the canvas. The page places each object in a box of no
    /// size at left and top, and its transform scales, turns, and moves it around that
    /// point. Most objects start at the point, and pictures and shapes are centered on it.
    /// </summary>
    private sealed record Anchor(string Type, PointD Origin, double A, double B, double C, double D, double E, double F)
    {
        public double Scale => Math.Sqrt(Math.Abs((A * D) - (B * C)));

        /// <summary>
        /// Clockwise on screen, as the board measures angles.
        /// </summary>
        public double AngleDegrees => RotatedRectangle.NormalizeAngle(Math.Atan2(B, A) * 180 / Math.PI);

        public static Anchor From(HtmlTag tag, string type)
        {
            var style = tag.Attribute("style");
            var origin = new PointD(StyleLength(style, "left") ?? 0, StyleLength(style, "top") ?? 0);
            var matrix = StyleMatrix(style);
            return new Anchor(type, origin, matrix[0], matrix[1], matrix[2], matrix[3], matrix[4], matrix[5]);
        }

        public PointD ToCanvas(PointD local) => new(
            Origin.X + (A * local.X) + (C * local.Y) + E,
            Origin.Y + (B * local.X) + (D * local.Y) + F);

        /// <summary>
        /// This anchor placed at a point inside another, so that a point here reaches
        /// the canvas through both transforms.
        /// </summary>
        public Anchor Inside(Anchor parent, PointD at) => new(
            Type,
            parent.ToCanvas(at + Origin + new PointD(E, F)),
            (parent.A * A) + (parent.C * B),
            (parent.B * A) + (parent.D * B),
            (parent.A * C) + (parent.C * D),
            (parent.B * C) + (parent.D * D),
            0,
            0);

        private static double[] StyleMatrix(string? style)
        {
            if (StyleValue(style, "transform") is { } value && value.Contains("matrix(", StringComparison.Ordinal))
            {
                var numbers = Numbers(value[value.IndexOf("matrix(", StringComparison.Ordinal)..]);
                if (numbers.Count >= 6)
                {
                    return numbers.Take(6).ToArray();
                }
            }

            return [1, 0, 0, 1, 0, 0];
        }
    }
}
