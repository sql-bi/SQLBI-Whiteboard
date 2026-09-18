using SQLBI.Whiteboard.Core.Geometry;

namespace SQLBI.Whiteboard.Core.Model;

/// <summary>
/// A copy of a selection, offset and ready to be added. What makes this more
/// than a loop is what a copy points at: a stroke linked to a duplicated
/// container has to follow the copy rather than the original, and so does a
/// connector bound to one. Anything pointing outside the set is let go, since
/// the copy would otherwise be tied to something that was not copied with it.
/// </summary>
public static class SelectionDuplicator
{
    /// <summary>
    /// The copies, in the order the three lists gave them, so a caller can pair
    /// a copy with what it was copied from. Each is a new object with a new id,
    /// moved by <paramref name="offsetWorld"/> and given a depth of its own from
    /// <paramref name="firstZIndex"/> upwards in the order the originals were
    /// drawn in, so the copies sit above the board in the shape they had on it.
    /// </summary>
    public static IReadOnlyList<BoardObject> Duplicate(
        IReadOnlyList<BoardObject> selection,
        IReadOnlyList<InkStrokeObject> linkedStrokes,
        IReadOnlyList<ConnectorBoardObject> connectors,
        PointD offsetWorld,
        int firstZIndex)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(linkedStrokes);
        ArgumentNullException.ThrowIfNull(connectors);

        BoardObject[] source = selection
            .Concat(linkedStrokes)
            .Concat(connectors)
            .DistinctBy(item => item.Id)
            .ToArray();
        if (source.Length == 0)
        {
            return [];
        }

        Dictionary<Guid, Guid> copiedIds = source.ToDictionary(item => item.Id, _ => Guid.NewGuid());

        // Depth follows the originals' order rather than the order the lists
        // arrived in, so a stroke that was under its shape stays under the copy.
        Dictionary<Guid, int> depths = source
            .OrderBy(item => item.ZIndex)
            .Select((item, index) => (item.Id, Depth: firstZIndex + index))
            .ToDictionary(pair => pair.Id, pair => pair.Depth);

        return source.Select(item => Copy(item, copiedIds, depths[item.Id], offsetWorld)).ToArray();
    }

    private static BoardObject Copy(
        BoardObject item,
        IReadOnlyDictionary<Guid, Guid> copiedIds,
        int zIndex,
        PointD offsetWorld)
    {
        Guid id = copiedIds[item.Id];
        if (item is ConnectorBoardObject connector)
        {
            // Rebuilt rather than moved, because the box of a curve depends on
            // which sides its ends are bound to, and an end that lets go here
            // changes it.
            return ConnectorBoardObject.Create(
                id,
                zIndex,
                connector.Kind,
                connector.Start + offsetWorld,
                connector.End + offsetWorld,
                connector.Argb,
                connector.Thickness,
                Rebind(connector.StartAnchor, copiedIds),
                Rebind(connector.EndAnchor, copiedIds),
                connector.AutoRoute);
        }

        BoardObject moved = item.WithBounds(item.Bounds.Translate(offsetWorld)).WithZIndex(zIndex);
        return moved switch
        {
            InkStrokeObject stroke => stroke with
            {
                Id = id,
                ContainerId = stroke.ContainerId is Guid container &&
                              copiedIds.TryGetValue(container, out Guid copiedContainer)
                    ? copiedContainer
                    : null,
            },
            ImageBoardObject image => image with { Id = id },
            LiveViewBoardObject liveView => liveView with { Id = id },
            FrameBoardObject frame => frame with { Id = id },
            ShapeBoardObject shape => shape with { Id = id },
            FreeTextBoardObject label => label with { Id = id },
            TextBoardObject text => text with { Id = id },
            _ => throw new NotSupportedException($"{item.GetType().Name} cannot be duplicated."),
        };
    }

    private static ConnectorAnchor? Rebind(
        ConnectorAnchor? anchor,
        IReadOnlyDictionary<Guid, Guid> copiedIds) =>
        anchor is { } bound && copiedIds.TryGetValue(bound.ObjectId, out Guid copied)
            ? bound with { ObjectId = copied }
            : null;
}
