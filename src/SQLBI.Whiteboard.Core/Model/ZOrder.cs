namespace SQLBI.Whiteboard.Core.Model;

/// <summary>
/// Where a selected block sits among everything else, and the one step that
/// takes it past what it meets next. The block keeps its internal order through
/// every move, so what was drawn over what inside the selection stays as it was.
/// </summary>
public static class ZOrder
{
    /// <summary>
    /// Whether nothing outside the block is above it. An empty block is at both
    /// ends at once, which is what disables all four commands when nothing is
    /// selected.
    /// </summary>
    public static bool IsAtFront(IReadOnlyList<BoardObject> objects, IReadOnlyCollection<Guid> blockIds)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(blockIds);
        HashSet<Guid> ids = [.. blockIds];
        BoardObject[] block = objects.Where(item => ids.Contains(item.Id)).ToArray();
        if (block.Length == 0)
        {
            return true;
        }

        var top = block.Max(item => item.ZIndex);
        return objects.Where(item => !ids.Contains(item.Id)).All(item => item.ZIndex < top);
    }

    public static bool IsAtBack(IReadOnlyList<BoardObject> objects, IReadOnlyCollection<Guid> blockIds)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(blockIds);
        HashSet<Guid> ids = [.. blockIds];
        BoardObject[] block = objects.Where(item => ids.Contains(item.Id)).ToArray();
        if (block.Length == 0)
        {
            return true;
        }

        var bottom = block.Min(item => item.ZIndex);
        return objects.Where(item => !ids.Contains(item.Id)).All(item => item.ZIndex > bottom);
    }

    /// <summary>
    /// The block past the one object it meets next, as the objects whose depth
    /// changes and what they change to. The z-indices of the span the move
    /// covers are dealt out again in the new order, so nothing outside that span
    /// moves and no two objects end up at the same depth. A block with something
    /// of its own between its members is closed up as it passes, which is what
    /// Bring to front does with it too.
    /// </summary>
    public static (IReadOnlyList<BoardObject> Before, IReadOnlyList<BoardObject> After) Step(
        IReadOnlyList<BoardObject> objects,
        IReadOnlyCollection<Guid> blockIds,
        bool forward)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(blockIds);
        HashSet<Guid> ids = [.. blockIds];
        BoardObject[] ordered = objects.OrderBy(item => item.ZIndex).ToArray();
        var first = Array.FindIndex(ordered, item => ids.Contains(item.Id));
        var last = Array.FindLastIndex(ordered, item => ids.Contains(item.Id));
        if (first < 0)
        {
            return ([], []);
        }

        var neighbour = forward ? last + 1 : first - 1;
        if (neighbour < 0 || neighbour >= ordered.Length)
        {
            return ([], []);
        }

        BoardObject[] span = ordered[Math.Min(first, neighbour)..(Math.Max(last, neighbour) + 1)];
        int[] depths = span.Select(item => item.ZIndex).ToArray();
        BoardObject[] block = span.Where(item => ids.Contains(item.Id)).ToArray();
        BoardObject[] rest = span.Where(item => !ids.Contains(item.Id)).ToArray();
        IEnumerable<BoardObject> rearranged = forward ? rest.Concat(block) : block.Concat(rest);

        var before = new List<BoardObject>();
        var after = new List<BoardObject>();
        var index = 0;
        foreach (BoardObject item in rearranged)
        {
            var depth = depths[index++];
            if (item.ZIndex == depth)
            {
                continue;
            }

            before.Add(item);
            after.Add(item.WithZIndex(depth));
        }

        return (before, after);
    }
}
