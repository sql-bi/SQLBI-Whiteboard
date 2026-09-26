using System.Globalization;
using System.Xml.Linq;

namespace SQLBI.Whiteboard.Core.Import;

/// <summary>
/// One run of SVG text as the renderer will set it: the families in order of
/// preference, the size in user units, the weight, and whether it is italic.
/// </summary>
public sealed record SvgTextRun(
    IReadOnlyList<string> Families,
    double Size,
    int Weight,
    bool Italic,
    string Text);

/// <summary>
/// Font information that depends on the platform. The application provides it from
/// WPF, and the smoke tests provide fixed values.
/// </summary>
public interface ISvgFontMetrics
{
    /// <summary>
    /// Whether a family by this exact name can be drawn.
    /// </summary>
    bool IsAvailable(string family);

    /// <summary>
    /// The run's width with kerning over its width without, or null when the run's
    /// font cannot be measured.
    /// </summary>
    double? KernedOverPlain(SvgTextRun run);
}

/// <summary>
/// The rewrites that need to know about fonts. Both address how PowerPoint writes SVG,
/// and both produce standard SVG.
/// </summary>
internal static class SvgText
{
    /// <summary>
    /// The strongest horizontal scaling applied. More than 5% visibly narrows the glyphs,
    /// so a run that kerns more than that is drawn slightly wider than its kerned width.
    /// </summary>
    private const double TightestKerning = 0.95;

    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    private static readonly (string Suffix, int Weight)[] WeightNames =
    [
        ("thin", 100), ("hairline", 100),
        ("extralight", 200), ("ultralight", 200),
        ("light", 300),
        ("semilight", 350), ("demilight", 350),
        ("regular", 400), ("normal", 400), ("book", 400),
        ("medium", 500),
        ("semibold", 600), ("demibold", 600),
        ("bold", 700),
        ("extrabold", 800), ("ultrabold", 800),
        ("black", 900), ("heavy", 900),
    ];

    /// <summary>
    /// Turns a family that names a weight, such as <c>Segoe Sans Small Semilight</c>, into
    /// the family and a <c>font-weight</c>, when the name as written cannot be found and
    /// the shorter one can. PowerPoint writes the face's own name for some weights, and
    /// the renderer looks fonts up by family, so without this the text falls back to a
    /// substitute although the font is on the machine.
    /// </summary>
    public static bool SplitWeightNames(XDocument document, ISvgFontMetrics metrics)
    {
        var changed = false;
        foreach (var element in document.Descendants().Where(item => item.Name.Namespace == Svg))
        {
            if (element.Attribute("font-family") is not { } attribute)
            {
                continue;
            }

            var entries = attribute.Value.Split(',');
            for (var index = 0; index < entries.Length; index++)
            {
                var family = Unquote(entries[index]);
                if (family.Length == 0 || IsGeneric(family) || metrics.IsAvailable(family))
                {
                    // The renderer uses the first family it can find, so the list is
                    // left as it is.
                    break;
                }

                if (WeightName(family, metrics) is { } split)
                {
                    entries[index] = split.Family;
                    attribute.Value = string.Join(",", entries);
                    element.SetAttributeValue("font-weight", split.Weight.ToString(CultureInfo.InvariantCulture));
                    changed = true;
                    break;
                }
            }
        }

        return changed;
    }

    /// <summary>
    /// Squeezes each run of text to the width kerning gives it. The renderer does not
    /// kern, so a run comes out as wide as its letters laid end to end. PowerPoint kerns,
    /// and places every run of a line at the position its kerned layout reached, having
    /// dropped the space that ended the run before, so an unkerned run reaches into the
    /// next and the words meet. Anything else that kerns, a browser included, sets its
    /// text at the kerned width too. The squeeze is from where the run starts, and is
    /// about one percent for ordinary text.
    /// </summary>
    public static bool KernRuns(XDocument document, ISvgFontMetrics metrics)
    {
        var changed = false;
        foreach (var text in document.Descendants(Svg + "text").ToArray())
        {
            if (text.HasElements ||
                SvgMarkup.Inherited(text, "text-anchor") is not (null or "start") ||
                NonZero(SvgMarkup.Inherited(text, "letter-spacing")) ||
                NonZero(SvgMarkup.Inherited(text, "word-spacing")) ||
                text.Attribute("textLength") is not null ||
                text.Attribute("dx") is not null ||
                text.Attribute("rotate") is not null ||
                Coordinate(text, "x") is not { } x ||
                Coordinate(text, "y") is not { } y ||
                Run(text) is not { } run)
            {
                continue;
            }

            if (metrics.KernedOverPlain(run) is not { } ratio || !double.IsFinite(ratio) || ratio >= 0.9995)
            {
                continue;
            }

            ratio = Math.Max(ratio, TightestKerning);
            var existing = text.Attribute("transform")?.Value.Trim();
            var squeeze = x == 0 && y == 0
                ? string.Create(CultureInfo.InvariantCulture, $"scale({ratio:R} 1)")
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"translate({x:R} {y:R}) scale({ratio:R} 1) translate({-x:R} {-y:R})");
            text.SetAttributeValue("transform", string.IsNullOrEmpty(existing) ? squeeze : existing + " " + squeeze);
            changed = true;
        }

        return changed;
    }

    private static SvgTextRun? Run(XElement text)
    {
        // SVG's default: runs of whitespace are one space, and the ends are trimmed.
        var content = string.Join(' ', text.Value.Split((char[])[' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
        if (content.Length == 0 ||
            SvgMarkup.Inherited(text, "font-family") is not { } familyList ||
            Size(SvgMarkup.Inherited(text, "font-size")) is not { } size)
        {
            return null;
        }

        var families = familyList.Split(',').Select(Unquote).Where(family => family.Length > 0).ToArray();
        if (families.Length == 0)
        {
            return null;
        }

        var weight = SvgMarkup.Inherited(text, "font-weight") switch
        {
            null or "normal" => 400,
            "bold" => 700,
            var value when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) &&
                           number is >= 1 and <= 1000 => number,
            _ => 400,
        };
        var italic = SvgMarkup.Inherited(text, "font-style") is "italic" or "oblique";
        return new SvgTextRun(families, size, weight, italic, content);
    }

    private static (string Family, int Weight)? WeightName(string family, ISvgFontMetrics metrics)
    {
        var words = family.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Two words first, so "Semi Light" is read as one weight before "Light" alone.
        for (var count = Math.Min(2, words.Length - 1); count >= 1; count--)
        {
            var suffix = string.Concat(words[^count..]).ToLowerInvariant();
            var match = WeightNames.FirstOrDefault(name => name.Suffix == suffix);
            if (match.Suffix is null)
            {
                continue;
            }

            var shorter = string.Join(' ', words[..^count]);
            if (metrics.IsAvailable(shorter))
            {
                return (shorter, match.Weight);
            }
        }

        return null;
    }

    /// <summary>
    /// A single coordinate, or zero when there is none. A list of coordinates positions
    /// each glyph, and scaling the run would move them, so such text is skipped.
    /// </summary>
    private static double? Coordinate(XElement text, string name)
    {
        var value = text.Attribute(name)?.Value.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return 0;
        }

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
    }

    private static double? Size(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var number = value.EndsWith("px", StringComparison.OrdinalIgnoreCase) ? value[..^2] : value;
        return double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var size) && size > 0
            ? size
            : null;
    }

    private static bool NonZero(string? value) =>
        value is not null and not "normal" &&
        !(double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number == 0);

    private static string Unquote(string entry) => entry.Trim().Trim('"', '\'').Trim();

    private static bool IsGeneric(string family) =>
        family is "serif" or "sans-serif" or "monospace" or "cursive" or "fantasy" or "system-ui" or "inherit";
}
