using SQLBI.Whiteboard.Core.Geometry;

namespace SQLBI.Whiteboard.Core.Import;

public static class ImportLayout
{
    public const double MaxRowWidth = 2400;
    public const double Gap = 32;
    public const double MaxImageWidth = 900;
    public const double MaxImageHeight = 700;

    public static IReadOnlyList<RectD> Place(
        IReadOnlyList<(double Width, double Height, bool StartNewRow)> items,
        PointD originTopLeft)
    {
        ArgumentNullException.ThrowIfNull(items);
        var placed = new RectD[items.Count];
        var x = originTopLeft.X;
        var y = originTopLeft.Y;
        var rowHeight = 0d;
        var rowOccupied = false;
        for (var index = 0; index < items.Count; index++)
        {
            var (width, height, startNewRow) = items[index];
            width = Math.Max(1, width);
            height = Math.Max(1, height);
            var wrap = startNewRow ||
                       (rowOccupied && x + width > originTopLeft.X + MaxRowWidth);
            if (wrap)
            {
                x = originTopLeft.X;
                y += rowHeight + Gap;
                rowHeight = 0;
                rowOccupied = false;
            }

            placed[index] = new RectD(x, y, width, height);
            x += width + Gap;
            rowHeight = Math.Max(rowHeight, height);
            rowOccupied = true;
        }

        return placed;
    }

    public static (double Width, double Height) ImageSize(double naturalWidth, double naturalHeight)
    {
        var width = Math.Max(1, naturalWidth);
        var height = Math.Max(1, naturalHeight);
        var scale = Math.Min(1, Math.Min(MaxImageWidth / width, MaxImageHeight / height));
        return (width * scale, height * scale);
    }
}
