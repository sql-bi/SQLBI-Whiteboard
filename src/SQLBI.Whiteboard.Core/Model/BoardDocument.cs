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

    public IEnumerable<BoardObject> Query(RectD worldBounds) =>
        _objects.Where(item => item.Bounds.Intersects(worldBounds));

    public ImageBoardObject? HitTestTopImage(PointD worldPoint) =>
        _objects.OfType<ImageBoardObject>()
            .Where(image => image.Bounds.Contains(worldPoint))
            .OrderByDescending(image => image.ZIndex)
            .FirstOrDefault();

    public BoardObject? HitTestTopContainer(PointD worldPoint) =>
        _objects.Where(item => item is IBoardContainer && item.Bounds.Contains(worldPoint))
            .OrderByDescending(item => item.ZIndex)
            .FirstOrDefault();

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

    public IEnumerable<InkStrokeObject> LinkedStrokes(Guid containerId) =>
        _objects.OfType<InkStrokeObject>()
            .Where(stroke => stroke.ContainerId == containerId);

    public IReadOnlyList<BoardObject> GetDeletionGroup(Guid objectId)
    {
        var target = _objects.FirstOrDefault(item => item.Id == objectId);
        if (target is null)
        {
            return [];
        }

        if (target is not IBoardContainer)
        {
            return [target];
        }

        return _objects
            .Where(item =>
                item.Id == objectId ||
                item is InkStrokeObject { ContainerId: var containerId } &&
                containerId == objectId)
            .ToArray();
    }
}
