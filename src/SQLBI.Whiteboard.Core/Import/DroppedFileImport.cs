namespace SQLBI.Whiteboard.Core.Import;

public enum DroppedFileKind
{
    Unsupported = 0,
    Image = 1,
    Text = 2,
    Import = 3,
}

public static class DroppedFileImport
{
    public const int MaximumTextBytes = 1_000_000;
    public const string ImportExtension = ".wimport";

    private static readonly HashSet<string> PlainTextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt",
    };

    public static DroppedFileKind Classify(string? path, ImportCatalog? catalog = null)
    {
        catalog ??= ImportCatalog.Default;
        var extension = Path.GetExtension(path);
        if (string.IsNullOrEmpty(extension))
        {
            return DroppedFileKind.Unsupported;
        }

        if (string.Equals(extension, ImportExtension, StringComparison.OrdinalIgnoreCase))
        {
            return DroppedFileKind.Import;
        }

        if (catalog.IsImageExtension(extension))
        {
            return DroppedFileKind.Image;
        }

        if (catalog.LanguageForExtension(extension) is not null ||
            PlainTextExtensions.Contains(extension))
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

    public static string LanguageIdFor(string? path, ImportCatalog? catalog = null) =>
        (catalog ?? ImportCatalog.Default).LanguageIdForPath(path);
}
