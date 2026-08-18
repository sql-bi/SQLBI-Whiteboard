using SQLBI.Whiteboard.Core.Model;

namespace SQLBI.Whiteboard.Core.Import;

public enum DroppedFileKind
{
    Unsupported = 0,
    Image = 1,
    Text = 2,
}

public static class DroppedFileImport
{
    public const int MaximumTextBytes = 1_000_000;

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".bmp",
        ".gif",
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt",
        ".sql",
        ".dax",
    };

    public static DroppedFileKind Classify(string? path)
    {
        var extension = Path.GetExtension(path);
        if (string.IsNullOrEmpty(extension))
        {
            return DroppedFileKind.Unsupported;
        }

        if (ImageExtensions.Contains(extension))
        {
            return DroppedFileKind.Image;
        }

        if (TextExtensions.Contains(extension))
        {
            return DroppedFileKind.Text;
        }

        return DroppedFileKind.Unsupported;
    }

    public static bool CanImport(string? path) => Classify(path) != DroppedFileKind.Unsupported;

    public static bool CanImportAny(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return paths.Any(CanImport);
    }

    public static string LanguageIdFor(string? path)
    {
        var extension = Path.GetExtension(path);
        return extension?.ToLowerInvariant() switch
        {
            ".dax" => TextLanguageIds.Dax,
            ".sql" => TextLanguageIds.SqlServer,
            _ => TextLanguageIds.Plain,
        };
    }
}
