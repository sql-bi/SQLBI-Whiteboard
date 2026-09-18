using SQLBI.Whiteboard.Core.Geometry;
using SQLBI.Whiteboard.Core.Import;

namespace SQLBI.Whiteboard.Core.SmokeTests;

internal static class ImportLayoutSmokeTests
{
    public static void Run()
    {
        var origin = new PointD(10, 20);
        var (width, height) = ImportLayout.VectorImageSize(1920, 1080);
        var figures = string.Join("\n\n", Enumerable.Range(1, 4)
            .Select(index => $"## Figure {index}\n![](figure-{index}.svg)"));

        IReadOnlyList<RectD> Place(ImportDocument document) => ImportLayout.Place(
            document.Items.Select(item => (width, height, item.StartNewRow)).ToArray(),
            origin,
            autoWrap: !document.HasExplicitRows);

        foreach (var separator in new[] { "---", "***", "___" })
        {
            // The reported case: one introduction, then three 900-wide figures.
            var recipe = ImportDocument.Parse(
                $"## Introduction\n![](intro.svg)\n\n{separator}\n\n" +
                $"## Model\n![](model.svg)\n\n## Roadmap\n![](roadmap.svg)\n\n" +
                $"## Decisions\n![](decisions.svg)\n\n{separator}\n\n{figures}");
            var placed = Place(recipe);
            Assert(recipe.HasExplicitRows && placed.Count == 8,
                "Every supported separator should select explicit rows for the entire recipe.");
            Assert(placed[0] == new RectD(origin.X, origin.Y, width, height) &&
                placed[1].Y == placed[0].Bottom + ImportLayout.Gap &&
                placed[1].Y == placed[2].Y && placed[2].Y == placed[3].Y &&
                placed[3].X == origin.X + 2 * (width + ImportLayout.Gap),
                "The second row must keep all three full-size figures, even beyond 2400 units.");
            Assert(placed[4].X == origin.X && placed[4].Y == placed[3].Bottom + ImportLayout.Gap &&
                placed.Skip(4).All(rect => rect.Y == placed[4].Y),
                "The next separator should start a row, and the final row may also exceed the width limit.");

            var beforeFirstBreak = ImportDocument.Parse($"{figures}\n\n{separator}");
            Assert(beforeFirstBreak.HasExplicitRows && Place(beforeFirstBreak).All(rect => rect.Y == origin.Y),
                "A trailing separator must keep the wide first row together, even without an item after it.");
            var leadingBreaks = ImportDocument.Parse($"{separator}\n\n{separator}\n\n{figures}");
            Assert(Place(leadingBreaks).All(rect => rect.Y == origin.Y),
                "Leading or repeated separators must not insert an empty row above the first figure.");
        }

        var automatic = ImportDocument.Parse(figures);
        var wrapped = Place(automatic);
        Assert(!automatic.HasExplicitRows && wrapped[0].Y == wrapped[1].Y &&
            wrapped[2].X == origin.X && wrapped[2].Y == wrapped[0].Bottom + ImportLayout.Gap &&
            wrapped[2].Y == wrapped[3].Y,
            "A recipe without separators must retain automatic wrapping at 2400 units.");

        foreach (var spacing in new[] { 0d, 80d, 500d })
        {
            var custom = ImportLayout.Place(
                [(900, 100, false), (900, 300, false), (900, 200, false), (100, 80, true)],
                origin, horizontalSpacing: spacing, verticalSpacing: 120, autoWrap: false);
            Assert(custom[2] == new RectD(origin.X + 2 * (900 + spacing), origin.Y, 900, 200) &&
                custom[3] == new RectD(origin.X, origin.Y + 300 + 120, 100, 80),
                "Explicit rows must honor horizontal spacing and vertical spacing below the tallest item.");
        }

        var emptySection = ImportDocument.Parse("## First\nText\n\n---\n\n## Empty\n\n## Second\nText");
        Assert(emptySection.Items.Count == 2 && emptySection.Items[1].StartNewRow,
            "Skipping an empty section must carry its row break to the next container.");

        var folder = Directory.CreateTempSubdirectory("wimport-rows-");
        try
        {
            var resolved = ImportDocument.Parse(
                "## First\nText\n\n---\n\n## Missing\n![](missing.svg)\n\n" +
                "## Also missing\n![](also-missing.svg)\n\n## Second\nText").Resolve(folder.FullName);
            Assert(resolved.HasExplicitRows && resolved.Items.Count == 2 &&
                resolved.MissingFiles.Count == 2 && resolved.Items[1].StartNewRow &&
                Place(resolved)[1].Y == origin.Y + height + ImportLayout.Gap,
                "Missing figures must not lose the separator before the remaining items in their row.");

            var onlyMissingAfterBreak = ImportDocument.Parse(
                "## First\nText\n\n---\n\n## Missing\n![](missing.svg)").Resolve(folder.FullName);
            Assert(onlyMissingAfterBreak.HasExplicitRows && onlyMissingAfterBreak.Items.Count == 1,
                "Resolution must retain explicit row mode even if it removes every item with a row break.");
        }
        finally
        {
            folder.Delete();
        }

        Assert(ImportLayout.Place([], origin, autoWrap: false).Count == 0,
            "An empty explicit-row import should remain empty.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
