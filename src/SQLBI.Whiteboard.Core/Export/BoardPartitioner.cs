using SQLBI.Whiteboard.Core.Geometry;
using SQLBI.Whiteboard.Core.Model;

namespace SQLBI.Whiteboard.Core.Export;

/// <summary>
/// Turns the unbounded board into an ordered list of areas, each of which fits a
/// slide or a page. The board is cut only where it is empty: project every unit
/// onto an axis, take the widest empty band, cut there, and recurse until the
/// region fits or no band is wide enough. A unit is a container with the strokes
/// linked to it, or a stroke on its own, so nothing is ever cut through.
/// </summary>
public static class BoardPartitioner
{
    public static IReadOnlyList<ExportArea> Partition(
        BoardDocument document,
        ExportLayoutOptions? options = null,
        Func<BoardObject, string?>? titleResolver = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        options ??= ExportLayoutOptions.Default;

        var units = BuildUnits(document.Objects);
        var areas = new List<ExportArea>();

        // Frames win: whatever sits inside one belongs to it, and frames come
        // first, in the order they were drawn or in reading order.
        IEnumerable<FrameBoardObject> frames = options.Order == AreaOrder.Reading
            ? document.Frames.OrderBy(frame => frame.Bounds.Top).ThenBy(frame => frame.Bounds.Left)
            : document.Frames;
        foreach (var frame in frames)
        {
            var inside = units.Where(unit => frame.Bounds.Contains(unit.Bounds.Center)).ToArray();
            units.RemoveAll(inside.Contains);
            var objects = inside.SelectMany(unit => unit.Objects).OrderBy(item => item.ZIndex).ToArray();
            areas.Add(new ExportArea(
                areas.Count + 1,
                frame.Bounds,
                objects,
                string.IsNullOrWhiteSpace(frame.Title)
                    ? ResolveTitle(document, objects, titleResolver)
                    : frame.Title.Trim(),
                TextScaleFor(frame.Bounds, options)));
        }

        if (units.Count == 0)
        {
            return areas;
        }

        var leaves = new List<Region>();
        Split(units, options, leaves);

        IEnumerable<Region> ordered = options.Order == AreaOrder.Drawing
            ? leaves.OrderBy(region => region.MinZIndex)
            : leaves;

        foreach (var region in ordered)
        {
            areas.Add(new ExportArea(
                areas.Count + 1,
                region.Bounds,
                region.Objects,
                ResolveTitle(document, region.Objects, titleResolver),
                TextScaleFor(region.Bounds, options)));
        }

        return areas;
    }

    /// <summary>
    /// The scale, relative to the requested smallest text, at which an area
    /// renders: 1 when it fits, less when it had to be shrunk.
    /// </summary>
    public static double TextScaleFor(RectD bounds, ExportLayoutOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var scale = Math.Min(
            options.MaximumAreaWidth / Math.Max(1, bounds.Width),
            options.MaximumAreaHeight / Math.Max(1, bounds.Height));
        return Math.Min(1, scale);
    }

    /// <summary>
    /// The container that names an area: the largest one, since a lecture note
    /// is annotated around the picture or the code it is about.
    /// </summary>
    public static BoardObject? DominantContainer(IEnumerable<BoardObject> objects) =>
        objects
            .Where(item => item is IBoardContainer)
            .OrderByDescending(item => item.Bounds.Width * item.Bounds.Height)
            .ThenBy(item => item.ZIndex)
            .FirstOrDefault();

    public static string? DefaultTitle(BoardDocument document, BoardObject item) => item switch
    {
        TextBoardObject text => string.IsNullOrWhiteSpace(text.Title) ? null : text.Title,
        ImageBoardObject image when document.Assets.TryGetValue(image.AssetId, out var asset) &&
                                    !string.IsNullOrWhiteSpace(asset.OriginalFileName) =>
            Path.GetFileNameWithoutExtension(asset.OriginalFileName),
        LiveViewBoardObject liveView when !string.IsNullOrWhiteSpace(liveView.Source.DisplayName) =>
            liveView.Source.DisplayName,
        _ => null,
    };

    private static string? ResolveTitle(
        BoardDocument document,
        IReadOnlyList<BoardObject> objects,
        Func<BoardObject, string?>? titleResolver)
    {
        if (DominantContainer(objects) is not { } container)
        {
            return null;
        }

        var title = titleResolver?.Invoke(container) ?? DefaultTitle(document, container);
        return string.IsNullOrWhiteSpace(title) ? null : title.Trim();
    }

    private static List<Unit> BuildUnits(IReadOnlyList<BoardObject> objects)
    {
        var units = new List<Unit>();
        var strokesByContainer = objects
            .OfType<InkStrokeObject>()
            .Where(stroke => stroke.ContainerId is not null)
            .ToLookup(stroke => stroke.ContainerId!.Value);
        var containerIds = new HashSet<Guid>();

        foreach (var item in objects)
        {
            if (item is IBoardContainer)
            {
                containerIds.Add(item.Id);
                var members = new List<BoardObject> { item };
                members.AddRange(strokesByContainer[item.Id]);
                units.Add(Unit.Of(members));
            }
        }

        foreach (var item in objects)
        {
            if (item is InkStrokeObject stroke &&
                (stroke.ContainerId is null || !containerIds.Contains(stroke.ContainerId.Value)))
            {
                units.Add(Unit.Of([stroke]));
            }
            else if (item is not InkStrokeObject && item is not IBoardContainer && item is not FrameBoardObject)
            {
                units.Add(Unit.Of([item]));
            }
        }

        return units;
    }

    private static void Split(List<Unit> units, ExportLayoutOptions options, List<Region> leaves)
    {
        var region = Region.Of(units);
        if (units.Count == 1 || Fits(region.Bounds, options))
        {
            leaves.Add(region);
            return;
        }

        var vertical = WidestGap(units, static unit => (unit.Bounds.Left, unit.Bounds.Right));
        var horizontal = WidestGap(units, static unit => (unit.Bounds.Top, unit.Bounds.Bottom));
        var cutAcrossX = vertical.Width >= horizontal.Width;
        var gap = cutAcrossX ? vertical : horizontal;
        if (gap.Width < options.GapThreshold)
        {
            leaves.Add(region);
            return;
        }

        var cut = gap.Start + (gap.Width / 2);
        var first = new List<Unit>();
        var second = new List<Unit>();
        foreach (var unit in units)
        {
            var position = cutAcrossX ? unit.Bounds.Center.X : unit.Bounds.Center.Y;
            (position < cut ? first : second).Add(unit);
        }

        if (first.Count == 0 || second.Count == 0)
        {
            leaves.Add(region);
            return;
        }

        Split(first, options, leaves);
        Split(second, options, leaves);
    }

    private static bool Fits(RectD bounds, ExportLayoutOptions options) =>
        bounds.Width <= options.MaximumAreaWidth && bounds.Height <= options.MaximumAreaHeight;

    /// <summary>
    /// The widest empty band between the units' projections on one axis.
    /// </summary>
    private static Gap WidestGap(List<Unit> units, Func<Unit, (double Start, double End)> project)
    {
        var intervals = units.Select(project).OrderBy(interval => interval.Start).ToArray();
        var best = new Gap(0, 0);
        var coveredTo = intervals[0].End;
        foreach (var interval in intervals.Skip(1))
        {
            var width = interval.Start - coveredTo;
            if (width > best.Width)
            {
                best = new Gap(coveredTo, width);
            }

            coveredTo = Math.Max(coveredTo, interval.End);
        }

        return best;
    }

    private static RectD Union(IEnumerable<RectD> rectangles)
    {
        var left = double.PositiveInfinity;
        var top = double.PositiveInfinity;
        var right = double.NegativeInfinity;
        var bottom = double.NegativeInfinity;
        foreach (var rectangle in rectangles)
        {
            left = Math.Min(left, rectangle.Left);
            top = Math.Min(top, rectangle.Top);
            right = Math.Max(right, rectangle.Right);
            bottom = Math.Max(bottom, rectangle.Bottom);
        }

        return new RectD(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    private readonly record struct Gap(double Start, double Width);

    private sealed record Unit(RectD Bounds, IReadOnlyList<BoardObject> Objects, int MinZIndex)
    {
        public static Unit Of(List<BoardObject> objects) => new(
            Union(objects.Select(item => item.Bounds)),
            objects,
            objects.Min(item => item.ZIndex));
    }

    private sealed record Region(RectD Bounds, IReadOnlyList<BoardObject> Objects, int MinZIndex)
    {
        public static Region Of(List<Unit> units) => new(
            Union(units.Select(unit => unit.Bounds)),
            units.SelectMany(unit => unit.Objects).OrderBy(item => item.ZIndex).ToArray(),
            units.Min(unit => unit.MinZIndex));
    }
}
