using System.Windows.Media;
using Markdig.Syntax;
using SQLBI.Whiteboard.Core.Model;

namespace SQLBI.Whiteboard;

internal sealed record MermaidDiagram(DrawingImage? Image, string? Error, string? Svg = null)
{
    public static MermaidDiagram Failure(string message) => new(null, message);
}

internal interface IMermaidRenderer
{
    Task<MermaidDiagram> RenderAsync(string source);
}

internal static class MermaidSource
{
    public const int MaximumCharacters = MermaidSnapshot.MaximumSourceLength;
    public const int MaximumDiagrams = MermaidSnapshot.MaximumCount;

    public static bool IsDiagram(FencedCodeBlock fence) =>
        string.Equals(fence.Info?.Trim(), "mermaid", StringComparison.OrdinalIgnoreCase);

    public static string? Validate(string source)
    {
        if (source.Length > MaximumCharacters) return "Whiteboard accepts up to 12,000 characters per diagram.";
        if (source.Contains("%%{", StringComparison.Ordinal) || source.TrimStart().StartsWith("---", StringComparison.Ordinal))
            return "Diagram configuration directives are not supported.";
        if (string.IsNullOrWhiteSpace(source)) return "Enter Mermaid diagram source inside this code block.";
        // The bundled engine owns syntax detection, including aliases and beta types.
        return null;
    }
}
