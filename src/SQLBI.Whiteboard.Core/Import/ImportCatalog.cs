using SQLBI.Whiteboard.Core.Model;

namespace SQLBI.Whiteboard.Core.Import;

public sealed class ImportLanguage
{
    public required string Id { get; init; }

    public required IReadOnlyList<string> FenceTags { get; init; }

    public required IReadOnlyList<string> Extensions { get; init; }
}

public sealed class ImportCatalog
{
    public static ImportCatalog Default { get; } = new(
        [
            new ImportLanguage
            {
                Id = TextLanguageIds.Dax,
                FenceTags = ["dax"],
                Extensions = [".dax"],
            },
            new ImportLanguage
            {
                Id = TextLanguageIds.SqlServer,
                FenceTags = ["sql", "tsql"],
                Extensions = [".sql"],
            },
        ],
        [".png", ".jpg", ".jpeg", ".bmp", ".gif"]);

    public ImportCatalog(
        IReadOnlyList<ImportLanguage> languages,
        IReadOnlyList<string> imageExtensions)
    {
        ArgumentNullException.ThrowIfNull(languages);
        ArgumentNullException.ThrowIfNull(imageExtensions);
        Languages = languages;
        ImageExtensions = imageExtensions;
    }

    public IReadOnlyList<ImportLanguage> Languages { get; }

    public IReadOnlyList<string> ImageExtensions { get; }

    public ImportCatalog WithLanguage(ImportLanguage language)
    {
        ArgumentNullException.ThrowIfNull(language);
        return new ImportCatalog([.. Languages, language], ImageExtensions);
    }

    public bool IsImageExtension(string? extension) =>
        !string.IsNullOrEmpty(extension) &&
        ImageExtensions.Any(known =>
            string.Equals(known, extension, StringComparison.OrdinalIgnoreCase));

    public ImportLanguage? LanguageForFence(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        var normalized = tag.Trim();
        return Languages.FirstOrDefault(language =>
            language.FenceTags.Any(fence =>
                string.Equals(fence, normalized, StringComparison.OrdinalIgnoreCase)));
    }

    public ImportLanguage? LanguageForExtension(string? extension)
    {
        if (string.IsNullOrEmpty(extension))
        {
            return null;
        }

        return Languages.FirstOrDefault(language =>
            language.Extensions.Any(known =>
                string.Equals(known, extension, StringComparison.OrdinalIgnoreCase)));
    }

    public string LanguageIdForPath(string? path)
    {
        var language = LanguageForExtension(Path.GetExtension(path));
        return language?.Id ?? TextLanguageIds.Plain;
    }
}
