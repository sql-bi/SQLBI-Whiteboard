using SQLBI.Whiteboard.Core.Geometry;
using SQLBI.Whiteboard.Core.Settings;

namespace SQLBI.Whiteboard.Core.Import;

/// <summary>
/// Where the slides of an imported deck go on the board. A slide is 1920 board
/// pixels wide, so at 100% zoom it is one 1080p screen and a pen is as wide on
/// it as on an empty board.
/// </summary>
public static class SlideDeckLayout
{
    public const double SlideWidth = 1920;

    /// <summary>
    /// Between slides, and between a deck and what was on the board before it.
    /// </summary>
    public const double Gap = SlideWidth / 10;

    /// <summary>
    /// One rectangle per slide, in the order given.
    /// </summary>
    /// <param name="sections">The section each slide belongs to, in deck order.</param>
    /// <param name="aspect">Slide height over slide width.</param>
    /// <param name="existingContent">
    /// What is already on the board. The deck goes below it, aligned with its left
    /// edge, so a second deck adds rows rather than lengthening the first one's.
    /// </param>
    public static IReadOnlyList<RectD> Place(
        IReadOnlyList<int> sections,
        double aspect,
        SlideArrangement arrangement,
        RectD? existingContent)
    {
        ArgumentNullException.ThrowIfNull(sections);
        var height = SlideWidth * (double.IsFinite(aspect) && aspect > 0 ? aspect : 9.0 / 16);
        var origin = existingContent is { } content
            ? new PointD(content.Left, content.Bottom + Gap)
            : new PointD(0, 0);

        var placed = new RectD[sections.Count];
        var row = 0;
        var column = 0;
        for (var index = 0; index < sections.Count; index++)
        {
            if (arrangement == SlideArrangement.RowPerSection &&
                index > 0 &&
                sections[index] != sections[index - 1])
            {
                row++;
                column = 0;
            }

            var (x, y) = arrangement == SlideArrangement.OneColumn
                ? (0, index)
                : arrangement == SlideArrangement.OneRow
                    ? (index, 0)
                    : (column, row);
            placed[index] = new RectD(
                origin.X + (x * (SlideWidth + Gap)),
                origin.Y + (y * (height + Gap)),
                SlideWidth,
                height);
            column++;
        }

        return placed;
    }

    /// <summary>
    /// The title on a slide's frame: its number and its title, or its number alone
    /// when the slide has no title. The section is left out; the row shows it.
    /// </summary>
    public static string FrameTitle(int number, string? title) =>
        string.IsNullOrWhiteSpace(title)
            ? $"{number}. Slide {number}"
            : $"{number}. {CollapseWhitespace(title)}";

    private static string CollapseWhitespace(string text) =>
        string.Join(' ', text.Split((char[])[' ', '\t', '\r', '\n', '\v'], StringSplitOptions.RemoveEmptyEntries));
}
