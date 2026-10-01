using System.Text.RegularExpressions;
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
        if (source.Length > MaximumCharacters) return "This prototype accepts up to 12,000 characters per diagram.";
        if (source.Contains("%%{", StringComparison.Ordinal) || source.TrimStart().StartsWith("---", StringComparison.Ordinal))
            return "Diagram configuration directives are not supported in this prototype.";
        if (!Regex.IsMatch(source, @"\A(?:\s|%%[^\r\n]*(?:\r?\n|$))*(?:flowchart|graph|sequenceDiagram|erDiagram)\b",
                RegexOptions.CultureInvariant | RegexOptions.NonBacktracking))
            return "This prototype supports flowcharts, sequence diagrams, and ER diagrams.";
        return null;
    }
}
