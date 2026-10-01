namespace SQLBI.Whiteboard.Core.Model;

/// <summary>A normalized SVG and the exact diagram source it represents.</summary>
public sealed record MermaidSnapshot(string Source, string Svg)
{
    public const int MaximumCount = 16;
    public const int MaximumSourceLength = 12_000;
    public const int MaximumSvgLength = 2_000_000;

    public bool IsWithinLimits() => Source is { Length: > 0 and <= MaximumSourceLength } &&
                                    Svg is { Length: > 0 and <= MaximumSvgLength };
}
