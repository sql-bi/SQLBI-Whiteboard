using System.IO.Compression;
using System.Text.Json;
using SQLBI.Whiteboard.Core.Geometry;
using SQLBI.Whiteboard.Core.Model;
using SQLBI.Whiteboard.Core.Settings;

namespace SQLBI.Whiteboard.Core.Persistence;

public static class BoardArchive
{
    public const int CurrentVersion = 7;

    /// <summary>
    /// Frames arrived in version 6. A board without one is still written as
    /// version 5, so it keeps opening in a release that predates them; only
    /// a board that needs the new object asks for the new reader.
    /// </summary>
    public const int VersionBeforeFrames = 5;

    /// <summary>
    /// Frames but none of the design objects, which arrived in version 7 and
    /// extend the same rule one step further.
    /// </summary>
    public const int VersionWithFrames = 6;

    public static int VersionFor(BoardDocument document) =>
        document.Objects.Any(NeedsVersion7) ? CurrentVersion
            : document.Objects.Any(item => item is FrameBoardObject) ? VersionWithFrames
            : VersionBeforeFrames;

    /// <summary>
    /// The objects that only a reader of version 7 understands. Connectors join
    /// labels and shapes here as they arrive.
    /// </summary>
    private static bool NeedsVersion7(BoardObject item) =>
        item is FreeTextBoardObject or ShapeBoardObject or ConnectorBoardObject;
    private const string SceneEntryName = "scene.json";
    public const string PreviewEntryName = "preview.png";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static async Task SaveAsync(
        BoardDocument document,
        Stream destination,
        CancellationToken cancellationToken = default,
        ReadOnlyMemory<byte> previewPng = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(destination);

        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        if (!previewPng.IsEmpty)
        {
            var previewEntry = archive.CreateEntry(PreviewEntryName, CompressionLevel.NoCompression);
            await using var previewStream = previewEntry.Open();
            await previewStream.WriteAsync(previewPng, cancellationToken);
        }

        var assetDtos = new List<AssetDto>();

        foreach (var asset in document.Assets.Values)
        {
            var entryName = $"assets/{asset.Id}.bin";
            var assetEntry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            await using (var assetStream = assetEntry.Open())
            {
                await assetStream.WriteAsync(asset.Data, cancellationToken);
            }

            assetDtos.Add(new AssetDto(asset.Id, asset.OriginalFileName, asset.ContentType, entryName));
        }

        var scene = new SceneDto(
            VersionFor(document),
            document.Objects.Select(ToDto).ToArray(),
            assetDtos.ToArray());

        var sceneEntry = archive.CreateEntry(SceneEntryName, CompressionLevel.Optimal);
        await using var sceneStream = sceneEntry.Open();
        await JsonSerializer.SerializeAsync(sceneStream, scene, JsonOptions, cancellationToken);
    }

    public static async Task<BoardDocument> LoadAsync(
        Stream source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        var sceneEntry = archive.GetEntry(SceneEntryName)
            ?? throw new InvalidDataException("The board does not contain scene.json.");

        SceneDto? scene;
        await using (var sceneStream = sceneEntry.Open())
        {
            scene = await JsonSerializer.DeserializeAsync<SceneDto>(
                sceneStream,
                JsonOptions,
                cancellationToken);
        }

        if (scene is null || scene.Version is < 1 or > CurrentVersion)
        {
            throw new InvalidDataException($"Unsupported board format version {scene?.Version}.");
        }

        var document = new BoardDocument();

        foreach (var assetDto in scene.Assets)
        {
            var assetEntry = archive.GetEntry(assetDto.EntryName)
                ?? throw new InvalidDataException($"Missing board asset {assetDto.Id}.");

            await using var assetStream = assetEntry.Open();
            using var memory = new MemoryStream();
            await assetStream.CopyToAsync(memory, cancellationToken);
            document.AddAsset(new BoardAsset(
                assetDto.Id,
                assetDto.OriginalFileName,
                assetDto.ContentType,
                memory.ToArray()));
        }

        // An anchor is only an anchor while what it names is in the file: a
        // board saved from a selection, or edited by hand, can carry a connector
        // bound to something that is not there, and that endpoint is simply free.
        var savedIds = scene.Objects.Select(item => item.Id).ToHashSet();
        foreach (var objectDto in scene.Objects.OrderBy(item => item.ZIndex))
        {
            document.AddObject(FromDto(objectDto, savedIds));
        }

        return document;
    }

    /// <summary>
    /// Copies the embedded preview bitmap if the archive has one. Older boards and
    /// empty boards have none; callers should fall back to the document icon.
    /// </summary>
    public static bool TryCopyPreview(Stream source, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        var previewEntry = archive.GetEntry(PreviewEntryName);
        if (previewEntry is null)
        {
            return false;
        }

        if (previewEntry.Length == 0)
        {
            return false;
        }

        using var previewStream = previewEntry.Open();
        previewStream.CopyTo(destination);
        return true;
    }

    private static ObjectDto ToDto(BoardObject item) => item switch
    {
        InkStrokeObject stroke => new ObjectDto(
            "ink",
            stroke.Id,
            stroke.ZIndex,
            stroke.Bounds,
            stroke.Points.Select(point => new InkPointDto(
                point.Position.X,
                point.Position.Y,
                point.Pressure,
                point.Timestamp)).ToArray(),
            stroke.Style,
            null,
            stroke.ContainerId),
        ImageBoardObject image => new ObjectDto(
            "image",
            image.Id,
            image.ZIndex,
            image.Bounds,
            null,
            null,
            image.AssetId,
            null,
            null,
            null,
            null,
            null),
        TextBoardObject text => new ObjectDto(
            "text",
            text.Id,
            text.ZIndex,
            text.Bounds,
            null,
            null,
            null,
            null,
            TextTitle: text.Title,
            TextContent: text.Text,
            TextVisualScale: text.VisualScale,
            TextLanguageId: TextLanguageIds.Normalize(text.LanguageId)),
        LiveViewBoardObject liveView => new ObjectDto(
            "liveView",
            liveView.Id,
            liveView.ZIndex,
            liveView.Bounds,
            null,
            null,
            liveView.SnapshotAssetId,
            null,
            liveView.Source,
            liveView.DesiredFrameRate,
            liveView.CaptureCursor,
            liveView.IsFrozen),
        // The frame's title travels in the text title field: one field for
        // "what this is called" rather than a second that means the same.
        FrameBoardObject frame => new ObjectDto(
            "frame",
            frame.Id,
            frame.ZIndex,
            frame.Bounds,
            null,
            null,
            null,
            null,
            TextTitle: frame.Title),
        // A label's text travels in the same field as a container's, since it is
        // the same thing to a reader: what the object says.
        FreeTextBoardObject label => new ObjectDto(
            "label",
            label.Id,
            label.ZIndex,
            label.Bounds,
            null,
            null,
            null,
            null,
            TextContent: label.Text,
            FontFamily: label.FontFamily,
            FontSize: label.FontSize,
            Argb: label.Argb,
            Bold: label.Bold,
            Italic: label.Italic,
            Underline: label.Underline,
            AngleDegrees: label.AngleDegrees,
            LayoutWidth: label.LayoutWidth,
            LayoutHeight: label.LayoutHeight),
        ShapeBoardObject shape => new ObjectDto(
            "shape",
            shape.Id,
            shape.ZIndex,
            shape.Bounds,
            null,
            null,
            null,
            null,
            AngleDegrees: shape.AngleDegrees,
            LayoutWidth: shape.LayoutWidth,
            LayoutHeight: shape.LayoutHeight,
            ShapeKind: shape.Kind.ToString(),
            OutlineArgb: shape.OutlineArgb,
            FillArgb: shape.FillArgb,
            Thickness: shape.Thickness),
        // The line's color and width travel in the fields the label and the
        // shape already have, since they mean the same thing here.
        ConnectorBoardObject connector => new ObjectDto(
            "connector",
            connector.Id,
            connector.ZIndex,
            connector.Bounds,
            null,
            null,
            null,
            null,
            Argb: connector.Argb,
            Thickness: connector.Thickness,
            ConnectorKind: connector.Kind.ToString(),
            StartX: connector.Start.X,
            StartY: connector.Start.Y,
            EndX: connector.End.X,
            EndY: connector.End.Y,
            StartAnchor: ToAnchorDto(connector.StartAnchor),
            EndAnchor: ToAnchorDto(connector.EndAnchor)),
        _ => throw new NotSupportedException($"Unsupported board object type {item.GetType().Name}."),
    };

    private static ConnectorAnchorDto? ToAnchorDto(ConnectorAnchor? anchor) =>
        anchor is { } bound ? new ConnectorAnchorDto(bound.ObjectId, bound.U, bound.V) : null;

    private static BoardObject FromDto(ObjectDto dto, IReadOnlySet<Guid> savedIds) => dto.Type switch
    {
        "ink" when dto.Points is { Length: > 0 } && dto.Style is not null =>
            InkStrokeObject.Create(
                dto.Points.Select(point => new InkPoint(
                    new PointD(point.X, point.Y),
                    point.Pressure,
                    point.Timestamp)),
                dto.Style.Value,
                dto.ZIndex,
                dto.Id,
                dto.ContainerId),
        "image" when !string.IsNullOrWhiteSpace(dto.AssetId) =>
            new ImageBoardObject(dto.Id, dto.ZIndex, dto.Bounds, dto.AssetId),
        "text" when dto.TextContent is not null =>
            new TextBoardObject(
                dto.Id,
                dto.ZIndex,
                dto.Bounds,
                string.IsNullOrWhiteSpace(dto.TextTitle) ? "Text" : dto.TextTitle,
                dto.TextContent,
                NormalizeTextVisualScale(dto.TextVisualScale),
                TextLanguageIds.Normalize(dto.TextLanguageId)),
        "liveView" when dto.LiveViewSource is not null =>
            new LiveViewBoardObject(
                dto.Id,
                dto.ZIndex,
                dto.Bounds,
                dto.LiveViewSource,
                dto.AssetId,
                NormalizeFrameRate(dto.DesiredFrameRate),
                dto.CaptureCursor ?? false,
                dto.IsFrozen ?? true),
        "frame" => new FrameBoardObject(dto.Id, dto.ZIndex, dto.Bounds, dto.TextTitle ?? ""),
        "label" => LabelFromDto(dto),
        "shape" => ShapeFromDto(dto),
        "connector" => ConnectorFromDto(dto, savedIds),
        _ => throw new InvalidDataException($"Invalid board object type '{dto.Type}'."),
    };

    /// <summary>
    /// A connector as the file has it, with the box worked out again from the
    /// two ends rather than trusted: a normalized kind curves where the saved
    /// box says it ran straight.
    /// </summary>
    private static ConnectorBoardObject ConnectorFromDto(ObjectDto dto, IReadOnlySet<Guid> savedIds) =>
        ConnectorBoardObject.Create(
            dto.Id,
            dto.ZIndex,
            NormalizeConnectorKind(dto.ConnectorKind),
            new PointD(dto.StartX ?? dto.Bounds.Left, dto.StartY ?? dto.Bounds.Top),
            new PointD(dto.EndX ?? dto.Bounds.Right, dto.EndY ?? dto.Bounds.Bottom),
            dto.Argb ?? PenStyle.Default.Argb,
            NormalizeConnectorThickness(dto.Thickness),
            NormalizeAnchor(dto.StartAnchor, savedIds),
            NormalizeAnchor(dto.EndAnchor, savedIds));

    /// <summary>
    /// An arrow is what a connector whose kind this release does not know
    /// becomes: it is the one of the three that says which way it was pointing.
    /// </summary>
    private static ConnectorKind NormalizeConnectorKind(string? kind) =>
        Enum.TryParse(kind, ignoreCase: true, out ConnectorKind parsed) && Enum.IsDefined(parsed)
            ? parsed
            : ConnectorKind.Arrow;

    private static double NormalizeConnectorThickness(double? thickness) =>
        thickness is > 0 and <= 100
            ? thickness.Value
            : ConnectorBoardObject.DefaultThickness;

    private static ConnectorAnchor? NormalizeAnchor(
        ConnectorAnchorDto? anchor,
        IReadOnlySet<Guid> savedIds) =>
        anchor is { } bound && savedIds.Contains(bound.ObjectId)
            ? ConnectorAnchor.Normalize(bound.ObjectId, bound.U, bound.V)
            : null;

    /// <summary>
    /// A label as the file has it, with everything the file could have got
    /// wrong put right: an uninstalled font becomes the default, a size outside
    /// what a label can be read at is brought back into range, and an angle
    /// that is not a multiple of 45 is snapped. The bounds are recomputed from
    /// the centre rather than trusted, because a snapped angle makes the saved
    /// box the box of a rectangle that is no longer there.
    /// </summary>
    private static FreeTextBoardObject LabelFromDto(ObjectDto dto)
    {
        var savedSize = dto.FontSize ?? LabelStyles.DefaultFontSize;
        var fontSize = LabelStyles.ClampFontSize(savedSize);

        // The layout was measured at the size the file carries, so a size
        // brought back into range takes the layout with it and the text still
        // fills the box it is drawn in.
        var scale = double.IsFinite(savedSize) && savedSize > 0 ? fontSize / savedSize : 1;
        return FreeTextBoardObject.Create(
            dto.Id,
            dto.ZIndex,
            dto.Bounds.Center,
            dto.TextContent ?? "",
            LabelStyles.NormalizeFont(dto.FontFamily),
            fontSize,
            dto.Argb ?? LabelStyles.DefaultArgb,
            dto.Bold ?? false,
            dto.Italic ?? false,
            dto.Underline ?? false,
            dto.AngleDegrees ?? 0,
            NormalizeLayoutSize(dto.LayoutWidth, fontSize * 4) * scale,
            NormalizeLayoutSize(dto.LayoutHeight, fontSize * 1.4) * scale);
    }

    /// <summary>
    /// A shape as the file has it. The angle and the box before the turn are
    /// optional, so a board written before a shape could be turned reads as the
    /// upright shape it was: no angle, and the box it was saved with as its
    /// layout. The bounds are recomputed from that box rather than trusted,
    /// because a snapped angle makes the saved box the box of a rectangle that
    /// is no longer there.
    /// </summary>
    private static ShapeBoardObject ShapeFromDto(ObjectDto dto)
    {
        var angle = RotatedRectangle.NormalizeAngle(dto.AngleDegrees ?? 0);
        RectD layout = dto.Bounds;
        if (angle != 0)
        {
            // The box in the file is the turned one, so the rectangle the shape
            // is drawn in is the size beside it, centred where that box is.
            var width = NormalizeLayoutSize(dto.LayoutWidth, dto.Bounds.Width);
            var height = NormalizeLayoutSize(dto.LayoutHeight, dto.Bounds.Height);
            layout = new RectD(
                dto.Bounds.Center.X - (width / 2),
                dto.Bounds.Center.Y - (height / 2),
                width,
                height);
        }

        return ShapeBoardObject.Create(
            dto.Id,
            dto.ZIndex,
            layout,
            NormalizeShapeKind(dto.ShapeKind),
            dto.OutlineArgb ?? PenStyle.Default.Argb,
            dto.FillArgb,
            NormalizeShapeThickness(dto.Thickness),
            angle);
    }

    private static double NormalizeLayoutSize(double? size, double fallback) =>
        size is { } value && double.IsFinite(value) && value > 0 ? value : fallback;

    private static int NormalizeFrameRate(int? frameRate) => frameRate is 15 or 30 or 60
        ? frameRate.Value
        : 15;

    /// <summary>
    /// A kind this release does not know becomes a rounded rectangle, so a board
    /// written by a later one still opens with its shapes where they were.
    /// </summary>
    private static ShapeKind NormalizeShapeKind(string? kind) =>
        Enum.TryParse(kind, ignoreCase: true, out ShapeKind parsed) && Enum.IsDefined(parsed)
            ? parsed
            : ShapeKind.RoundedRectangle;

    private static double NormalizeShapeThickness(double? thickness) =>
        thickness is > 0 and <= 100
            ? thickness.Value
            : ShapeBoardObject.DefaultThickness;

    private static double NormalizeTextVisualScale(double? scale) =>
        scale is > 0 and < 100 ? scale.Value : 1;

    private sealed record SceneDto(int Version, ObjectDto[] Objects, AssetDto[] Assets);

    private sealed record ObjectDto(
        string Type,
        Guid Id,
        int ZIndex,
        RectD Bounds,
        InkPointDto[]? Points,
        PenStyle? Style,
        string? AssetId,
        Guid? ContainerId,
        LiveViewSourceConfiguration? LiveViewSource = null,
        int? DesiredFrameRate = null,
        bool? CaptureCursor = null,
        bool? IsFrozen = null,
        string? TextTitle = null,
        string? TextContent = null,
        double? TextVisualScale = null,
        string? TextLanguageId = null,
        string? FontFamily = null,
        double? FontSize = null,
        uint? Argb = null,
        bool? Bold = null,
        bool? Italic = null,
        bool? Underline = null,
        double? AngleDegrees = null,
        double? LayoutWidth = null,
        double? LayoutHeight = null,
        string? ShapeKind = null,
        uint? OutlineArgb = null,
        uint? FillArgb = null,
        double? Thickness = null,
        string? ConnectorKind = null,
        double? StartX = null,
        double? StartY = null,
        double? EndX = null,
        double? EndY = null,
        ConnectorAnchorDto? StartAnchor = null,
        ConnectorAnchorDto? EndAnchor = null);

    private sealed record ConnectorAnchorDto(Guid ObjectId, double U, double V);

    private sealed record InkPointDto(double X, double Y, float Pressure, long Timestamp);

    private sealed record AssetDto(
        string Id,
        string OriginalFileName,
        string ContentType,
        string EntryName);
}
