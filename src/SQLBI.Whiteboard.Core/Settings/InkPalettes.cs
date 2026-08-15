using SQLBI.Whiteboard.Core.Model;

namespace SQLBI.Whiteboard.Core.Settings;

public readonly record struct InkSwatch(string Name, uint Argb);

public sealed class InkToolSettings
{
    public uint Argb { get; set; }
    public double Thickness { get; set; }

    public PenStyle ToStyle(PenKind kind) => new(Argb, Thickness, kind);

    public static InkToolSettings From(PenStyle style) => new()
    {
        Argb = style.Argb,
        Thickness = style.Thickness,
    };
}

public static class InkPalettes
{
    public static IReadOnlyList<InkSwatch> Pen { get; } =
    [
        new("Near-black", 0xFF1F2937),
        new("Vermillion", 0xFFE64B3D),
        new("Orange", 0xFFE69F00),
        new("Sky", 0xFF56B4E9),
        new("Teal", 0xFF009E73),
        new("Magenta", 0xFFCC79A7),
    ];

    public static IReadOnlyList<InkSwatch> Highlighter { get; } =
    [
        new("Yellow", 0xFFFACC15),
        new("Pink", 0xFFF472B6),
        new("Sky", 0xFF38BDF8),
        new("Mint", 0xFF2DD4BF),
    ];

    public static IReadOnlyList<double> PenThicknesses { get; } = [2, 4, 8, 14];

    public static IReadOnlyList<double> HighlighterThicknesses { get; } = [3, 6, 10, 16];

    public static PenStyle DefaultPen { get; } = new(0xFFE64B3D, 4, PenKind.Pen);

    public static PenStyle DefaultHighlighter { get; } = new(0xFFFACC15, 6, PenKind.Highlighter);

    public static PenStyle DefaultCalligraphy { get; } = new(0xFF1F2937, 4, PenKind.Calligraphy);

    public static IReadOnlyList<InkSwatch> ColorsFor(PenKind kind) =>
        kind == PenKind.Highlighter ? Highlighter : Pen;

    public static IReadOnlyList<double> ThicknessesFor(PenKind kind) =>
        kind == PenKind.Highlighter ? HighlighterThicknesses : PenThicknesses;

    public static PenStyle DefaultFor(PenKind kind) => kind switch
    {
        PenKind.Highlighter => DefaultHighlighter,
        PenKind.Calligraphy => DefaultCalligraphy,
        _ => DefaultPen,
    };

    public static InkToolSettings Normalize(InkToolSettings? settings, PenKind kind)
    {
        var fallback = DefaultFor(kind);
        if (settings is null)
        {
            return InkToolSettings.From(fallback);
        }

        var colors = ColorsFor(kind);
        var thicknesses = ThicknessesFor(kind);
        return new InkToolSettings
        {
            Argb = colors.Any(swatch => swatch.Argb == settings.Argb)
                ? settings.Argb
                : fallback.Argb,
            Thickness = thicknesses.Any(thickness => thickness == settings.Thickness)
                ? settings.Thickness
                : fallback.Thickness,
        };
    }
}
