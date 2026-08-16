using System.IO.Compression;
using System.Text.Json;
using SQLBI.Whiteboard.Core.Geometry;
using SQLBI.Whiteboard.Core.Model;

namespace SQLBI.Whiteboard.Core.Persistence;

public static class BoardArchive
{
    public const int CurrentVersion = 5;
    private const string SceneEntryName = "scene.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static async Task SaveAsync(
        BoardDocument document,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(destination);

        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
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
            CurrentVersion,
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

        foreach (var objectDto in scene.Objects.OrderBy(item => item.ZIndex))
        {
            document.AddObject(FromDto(objectDto));
        }

        return document;
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
        _ => throw new NotSupportedException($"Unsupported board object type {item.GetType().Name}."),
    };

    private static BoardObject FromDto(ObjectDto dto) => dto.Type switch
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
        _ => throw new InvalidDataException($"Invalid board object type '{dto.Type}'."),
    };

    private static int NormalizeFrameRate(int? frameRate) => frameRate is 15 or 30 or 60
        ? frameRate.Value
        : 15;

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
        string? TextLanguageId = null);

    private sealed record InkPointDto(double X, double Y, float Pressure, long Timestamp);

    private sealed record AssetDto(
        string Id,
        string OriginalFileName,
        string ContentType,
        string EntryName);
}
