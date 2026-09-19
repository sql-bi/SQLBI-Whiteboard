using SQLBI.Whiteboard.Core.Geometry;

namespace SQLBI.Whiteboard.Core.Model;

public sealed class BoardDocument
{
    private readonly List<BoardObject> _objects = [];
    private readonly Dictionary<string, BoardAsset> _assets = new(StringComparer.Ordinal);

    public event EventHandler? Changed;

    public IReadOnlyList<BoardObject> Objects => _objects;
    public IReadOnlyDictionary<string, BoardAsset> Assets => _assets;
    public int NextZIndex => _objects.Count == 0 ? 0 : _objects.Max(item => item.ZIndex) + 1;
    public RectD? ContentBounds
    {
        get
        {
            if (_objects.Count == 0)
            {
                return null;
            }

            var left = _objects.Min(item => item.Bounds.Left);
            var top = _objects.Min(item => item.Bounds.Top);
            var right = _objects.Max(item => item.Bounds.Right);
            var bottom = _objects.Max(item => item.Bounds.Bottom);
            return new RectD(left, top, right - left, bottom - top);
        }
    }

    public void AddObject(BoardObject item)
    {
        if (_objects.Any(existing => existing.Id == item.Id))
        {
            throw new InvalidOperationException($"An object with id {item.Id} already exists.");
        }

        _objects.Add(item);
        _objects.Sort(static (left, right) => left.ZIndex.CompareTo(right.ZIndex));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool RemoveObject(Guid id)
    {
        var removed = _objects.RemoveAll(item => item.Id == id) > 0;
        if (removed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return removed;
    }

    public void ReplaceObject(BoardObject replacement)
    {
        var index = _objects.FindIndex(item => item.Id == replacement.Id);
        if (index < 0)
        {
            throw new KeyNotFoundException($"Object {replacement.Id} was not found.");
        }

        _objects[index] = replacement;
        _objects.Sort(static (left, right) => left.ZIndex.CompareTo(right.ZIndex));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ReplaceObjects(IEnumerable<BoardObject> replacements)
    {
        var replacementList = replacements.ToArray();
        if (replacementList.Length == 0)
        {
            return;
        }

        if (replacementList.Select(item => item.Id).Distinct().Count() != replacementList.Length)
        {
            throw new ArgumentException("Replacement object ids must be unique.", nameof(replacements));
        }

        var replacementById = replacementList.ToDictionary(item => item.Id);
        if (replacementById.Keys.Any(id => _objects.All(item => item.Id != id)))
        {
            throw new KeyNotFoundException("One or more replacement objects were not found.");
        }

        for (var index = 0; index < _objects.Count; index++)
        {
            if (replacementById.TryGetValue(_objects[index].Id, out var replacement))
            {
                _objects[index] = replacement;
            }
        }

        _objects.Sort(static (left, right) => left.ZIndex.CompareTo(right.ZIndex));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void AddAsset(BoardAsset asset)
    {
        _assets[asset.Id] = asset;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// A shallow copy that shares this document's objects and assets but none of its
    /// mutability. Autosave takes one on the UI thread and writes it on another: objects are
    /// records and an asset's bytes are never rewritten once it exists, so what the writer
    /// sees cannot change underneath it while the board carries on being drawn.
    /// </summary>
    public BoardDocument Snapshot()
    {
        var copy = new BoardDocument();
        copy._objects.AddRange(_objects);
        foreach (var asset in _assets)
        {
            copy._assets[asset.Key] = asset.Value;
        }

        return copy;
    }

    public bool RemoveAsset(string id)
    {
        var removed = _assets.Remove(id);
        if (removed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return removed;
    }

    public IEnumerable<BoardObject> Query(RectD worldBounds) =>
        _objects.Where(item => item.Bounds.Intersects(worldBounds));

    public ImageBoardObject? HitTestTopImage(PointD worldPoint) =>
        _objects.OfType<ImageBoardObject>()
            .Where(image => image.Bounds.Contains(worldPoint))
            .OrderByDescending(image => image.ZIndex)
            .FirstOrDefault();

    /// <summary>
    /// The topmost thing a select gesture lands on, whatever it is: a container
    /// anywhere inside it, a frame by its edge or tab, a stroke within a band
    /// of the line itself. Each object answers for itself, and the zoom sizes
    /// the bands so they stay the same size under the pen at any zoom.
    /// </summary>
    /// <param name="accepts">
    /// Which kinds of object the caller can take hold of at all. A mode that
    /// leaves out stroke selection passes strokes over here, the way an area
    /// passes frames over: the ink stays on the board, and what is under it
    /// answers instead of nothing answering.
    /// </param>
    public BoardObject? HitTestTopSelectable(
        PointD worldPoint,
        double zoom = 1,
        Func<BoardObject, bool>? accepts = null) =>
        _objects.Where(item => accepts is null || accepts(item))
            .Where(item => Hits(item, worldPoint, zoom))
            .OrderByDescending(item => item.ZIndex)
            .FirstOrDefault();

    /// <summary>
    /// The topmost container or frame, for the gestures that mean a container
    /// rather than whatever happens to be on top: double-click framing, and the
    /// hover that offers a text container's width handle.
    /// </summary>
    public BoardObject? HitTestTopContainer(PointD worldPoint, double zoom = 1) =>
        _objects.Where(item => item is IBoardContainer or FrameBoardObject)
            .Where(item => Hits(item, worldPoint, zoom))
            .OrderByDescending(item => item.ZIndex)
            .FirstOrDefault();

    /// <summary>
    /// Everything the area takes, in z-order.
    /// </summary>
    public IReadOnlyList<BoardObject> ObjectsInArea(SelectionArea area, AreaSelection rule)
    {
        ArgumentNullException.ThrowIfNull(area);
        return _objects
            .Where(item => item.IsAreaSelectable && Taken(item, area, rule))
            .ToArray();
    }

    /// <summary>
    /// What select-all takes, in z-order: everything an area could take, or the
    /// ink strokes alone. It is the area's own rule with no area, so frames are
    /// left out here for the reason they are left out there - a band drawn over
    /// a slide means the things on it.
    /// </summary>
    public IReadOnlyList<BoardObject> AllSelectable(bool strokesOnly) =>
        _objects
            .Where(item => item.IsAreaSelectable && (!strokesOnly || item is InkStrokeObject))
            .ToArray();

    /// <summary>
    /// The objects whose geometry meets this one's box. It is the partly-inside
    /// test of the area selection, asked of one object's bounds, so what counts
    /// as touching is the same thing a rubber band counts as taking.
    /// </summary>
    public IReadOnlyList<Guid> Touching(Guid id)
    {
        var target = _objects.FirstOrDefault(item => item.Id == id);
        if (target is null)
        {
            return [];
        }

        var area = SelectionArea.Rectangle(target.Bounds);
        return _objects
            .Where(item => item.Id != id &&
                           item.IsAreaSelectable &&
                           Taken(item, area, AreaSelection.PartlyInside))
            .Select(item => item.Id)
            .ToArray();
    }

    /// <summary>
    /// Grows a selection to what it touches. An object is examined once and
    /// never again, so a ring of objects that touch each other ends the rounds
    /// rather than circling them.
    /// </summary>
    public IReadOnlyList<Guid> GrowSelection(IEnumerable<Guid> seed, ExtendSelection mode)
    {
        ArgumentNullException.ThrowIfNull(seed);
        var selected = new List<Guid>();
        var chosen = new HashSet<Guid>();
        foreach (var id in seed)
        {
            if (chosen.Add(id))
            {
                selected.Add(id);
            }
        }

        if (mode == ExtendSelection.Ignore)
        {
            return selected;
        }

        var examined = new HashSet<Guid>();
        var frontier = selected.ToList();
        while (frontier.Count > 0)
        {
            var added = new List<Guid>();
            foreach (var id in frontier)
            {
                if (!examined.Add(id))
                {
                    continue;
                }

                foreach (var touched in Touching(id))
                {
                    if (chosen.Add(touched))
                    {
                        selected.Add(touched);
                        added.Add(touched);
                    }
                }
            }

            if (mode == ExtendSelection.Single)
            {
                break;
            }

            frontier = added;
        }

        return selected;
    }

    public IEnumerable<FrameBoardObject> Frames => _objects.OfType<FrameBoardObject>();

    public BoardObject? FindSingleTouchedContainer(InkStrokeObject stroke)
    {
        BoardObject? match = null;
        foreach (var item in _objects.Where(item => item is IBoardContainer))
        {
            if (!stroke.Touches(item.Bounds))
            {
                continue;
            }

            if (match is not null)
            {
                return null;
            }

            match = item;
        }

        return match;
    }

    /// <summary>
    /// The connectors with an end bound to this object. A gesture that moves it
    /// carries them, and deleting it detaches them.
    /// </summary>
    public IReadOnlyList<ConnectorBoardObject> ConnectorsAttachedTo(Guid objectId) =>
        _objects.OfType<ConnectorBoardObject>()
            .Where(connector => connector.StartAnchor?.ObjectId == objectId ||
                                connector.EndAnchor?.ObjectId == objectId)
            .ToArray();

    /// <summary>
    /// A tap and an area asked of the line as it is drawn. Only a connector
    /// answers differently for it: a curve bound to a shape that has been turned
    /// leaves along the side as that side now faces, and the board is the only
    /// place that knows which shape that is.
    /// </summary>
    private bool Hits(BoardObject item, PointD worldPoint, double zoom) =>
        item is ConnectorBoardObject connector
            ? connector.HitTest(worldPoint, zoom, FrameOf(connector.StartAnchor), FrameOf(connector.EndAnchor))
            : item.HitTest(worldPoint, zoom);

    private bool Taken(BoardObject item, SelectionArea area, AreaSelection rule) =>
        item is ConnectorBoardObject connector
            ? connector.IsTakenBy(area, rule, FrameOf(connector.StartAnchor), FrameOf(connector.EndAnchor))
            : item.IsTakenBy(area, rule);

    /// <summary>
    /// The rectangles the two ends are anchored in, as the board has them now,
    /// and nothing for an end that is bound to nothing. It is what a curve is
    /// drawn from and what an automatic anchor is chosen against.
    /// </summary>
    public (AnchorFrame? Start, AnchorFrame? End) AnchorFrames(ConnectorBoardObject connector)
    {
        ArgumentNullException.ThrowIfNull(connector);
        return (FrameOf(connector.StartAnchor), FrameOf(connector.EndAnchor));
    }

    /// <summary>
    /// The connector with its automatic anchors brought to the sides that now
    /// face each other. A Fixed connector comes back unchanged, so this is safe
    /// to run wherever a connector is rebuilt.
    /// </summary>
    public ConnectorBoardObject Reroute(ConnectorBoardObject connector)
    {
        (AnchorFrame? start, AnchorFrame? end) = AnchorFrames(connector);
        return connector.Reroute(start, end);
    }

    private AnchorFrame? FrameOf(ConnectorAnchor? anchor) =>
        anchor is { } bound && _objects.FirstOrDefault(item => item.Id == bound.ObjectId) is { } target
            ? target.AnchorFrame
            : null;

    public IEnumerable<InkStrokeObject> LinkedStrokes(Guid containerId) =>
        _objects.OfType<InkStrokeObject>()
            .Where(stroke => stroke.ContainerId == containerId);

    public IReadOnlyList<BoardObject> GetDeletionGroup(Guid objectId) =>
        GetDeletionGroup([objectId]);

    /// <summary>
    /// The objects a delete takes: the selection itself, plus every stroke
    /// linked to a selected container, whether or not the stroke was selected.
    /// </summary>
    public IReadOnlyList<BoardObject> GetDeletionGroup(IEnumerable<Guid> objectIds)
    {
        ArgumentNullException.ThrowIfNull(objectIds);
        var ids = objectIds.ToHashSet();
        var containerIds = _objects
            .Where(item => ids.Contains(item.Id) && item is IBoardContainer)
            .Select(item => item.Id)
            .ToHashSet();
        return _objects
            .Where(item =>
                ids.Contains(item.Id) ||
                (item is InkStrokeObject { ContainerId: Guid containerId } &&
                 containerIds.Contains(containerId)))
            .ToArray();
    }
}
