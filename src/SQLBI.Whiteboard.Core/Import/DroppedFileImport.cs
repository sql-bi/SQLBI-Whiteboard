using System.Text;

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
    public const int MaximumSvgBytes = 16_000_000;
    public const string ImportExtension = ".wimport";
    public const string SvgExtension = ".svg";
    public const string SvgContentType = "image/svg+xml";

    private const char ByteOrderMark = '\uFEFF';

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

    /// <summary>
    /// Recognizes an SVG document from its markup. Clipboard content arrives without a
    /// file name, so the shape of the text is the only signal there is. The root element
    /// has to be the SVG one — a page of HTML with a picture inside it carries the same
    /// tags and is not an image.
    /// </summary>
    public static bool LooksLikeSvg(string? markup)
    {
        if (string.IsNullOrWhiteSpace(markup))
        {
            return false;
        }

        var text = markup.TrimStart(ByteOrderMark).TrimStart();
        while (TryMeasurePrologue(text, out var prologue))
        {
            text = text[prologue..].TrimStart();
        }

        if (text.Length < 2 || text[0] != '<')
        {
            return false;
        }

        var end = 1;
        while (end < text.Length &&
               !char.IsWhiteSpace(text[end]) &&
               text[end] is not ('>' or '/'))
        {
            end++;
        }

        if (end == text.Length)
        {
            return false;
        }

        // Any prefix may be bound to the SVG namespace, so compare the local name.
        var name = text[1..end];
        var colon = name.LastIndexOf(':');
        return string.Equals(
            colon >= 0 ? name[(colon + 1)..] : name,
            "svg",
            StringComparison.OrdinalIgnoreCase);
    }

    public static bool LooksLikeSvg(byte[]? bytes)
    {
        if (bytes is null || bytes.Length is 0 or > MaximumSvgBytes)
        {
            return false;
        }

        // Bitmaps fail on their first byte, so the decode below only runs for markup.
        var start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
            ? 3
            : 0;
        while (start < bytes.Length && bytes[start] is 0x20 or 0x09 or 0x0D or 0x0A)
        {
            start++;
        }

        if (start >= bytes.Length || bytes[start] != (byte)'<')
        {
            return false;
        }

        try
        {
            return LooksLikeSvg(new UTF8Encoding(false, throwOnInvalidBytes: true)
                .GetString(bytes, start, bytes.Length - start));
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Measures an XML declaration, comment, or doctype sitting ahead of the root element.
    /// </summary>
    private static bool TryMeasurePrologue(string text, out int length)
    {
        length = 0;
        var (opening, closing) = text switch
        {
            _ when text.StartsWith("<?", StringComparison.Ordinal) => ("<?", "?>"),
            _ when text.StartsWith("<!--", StringComparison.Ordinal) => ("<!--", "-->"),
            _ when text.StartsWith("<!", StringComparison.Ordinal) => ("<!", ">"),
            _ => (string.Empty, string.Empty),
        };

        if (opening.Length == 0)
        {
            return false;
        }

        var end = text.IndexOf(closing, opening.Length, StringComparison.Ordinal);
        if (end < 0)
        {
            return false;
        }

        length = end + closing.Length;
        return true;
    }
}
