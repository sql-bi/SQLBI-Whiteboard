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

        if (string.Equals(extension, ".wboard", StringComparison.OrdinalIgnoreCase))
        {
            return DroppedFileKind.Unsupported;
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

        // Unrecognized extensions still drop as text so paste-style language
        // order can choose DAX, SQL, or plain text. Images and .wimport above
        // keep their own paths.
        return DroppedFileKind.Text;
    }

    public static bool CanImport(string? path) => Classify(path) != DroppedFileKind.Unsupported;

    public static bool CanImportAny(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return paths.Any(CanImport);
    }

    public static string LanguageIdFor(string? path, ImportCatalog? catalog = null) =>
        (catalog ?? ImportCatalog.Default).LanguageIdForPath(path);

    public static bool HasRecognizedLanguageExtension(string? path, ImportCatalog? catalog = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        catalog ??= ImportCatalog.Default;
        var extension = Path.GetExtension(path);
        if (string.IsNullOrEmpty(extension))
        {
            return false;
        }

        return catalog.LanguageForExtension(extension) is not null ||
               PlainTextExtensions.Contains(extension);
    }

    public static bool LooksLikeText(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return false;
        }

        var probe = Math.Min(bytes.Length, 512);
        for (var index = 0; index < probe; index++)
        {
            if (bytes[index] == 0)
            {
                return false;
            }
        }

        return true;
    }
}
