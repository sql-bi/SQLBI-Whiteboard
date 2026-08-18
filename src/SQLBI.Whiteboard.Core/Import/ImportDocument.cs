using System.Text.RegularExpressions;
using SQLBI.Whiteboard.Core.Model;

namespace SQLBI.Whiteboard.Core.Import;

public enum ImportItemKind
{
    Image = 0,
    Text = 1,
}

public sealed record ImportItem
{
    public required string Title { get; init; }

    public required ImportItemKind Kind { get; init; }

    public string LanguageId { get; init; } = TextLanguageIds.Plain;

    public string? Text { get; init; }

    public string? SourcePath { get; init; }

    public bool StartNewRow { get; init; }

    public byte[]? ImageBytes { get; init; }

    public string? ImageFileName { get; init; }
}

public sealed class ImportDocument
{
    private static readonly Regex ImagePattern = new(
        @"!\[(?<alt>[^\]]*)\]\((?<path><[^>]+>|[^)\s]+)\)",
        RegexOptions.CultureInvariant);

    private static readonly Regex LinkPattern = new(
        @"(?<!!)\[(?<text>[^\]]*)\]\((?<path><[^>]+>|[^)\s]+)\)",
        RegexOptions.CultureInvariant);

    public ImportDocument(
        string? title,
        IReadOnlyList<ImportItem> items,
        IReadOnlyList<string> missingFiles)
    {
        Title = title;
        Items = items;
        MissingFiles = missingFiles;
    }

    public string? Title { get; }

    public IReadOnlyList<ImportItem> Items { get; }

    public IReadOnlyList<string> MissingFiles { get; }

    public static ImportDocument Parse(string markdown, ImportCatalog? catalog = null)
    {
        catalog ??= ImportCatalog.Default;
        var sections = SplitSections(markdown ?? string.Empty);
        var items = new List<ImportItem>();
        foreach (var section in sections)
        {
            if (Recognize(section, catalog) is { } item)
            {
                items.Add(item);
            }
        }

        return new ImportDocument(
            ReadBoardTitle(markdown ?? string.Empty),
            items,
            []);
    }

    public ImportDocument Resolve(string baseDirectory, ImportCatalog? catalog = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        catalog ??= ImportCatalog.Default;
        var items = new List<ImportItem>();
        var missing = new List<string>();
        foreach (var item in Items)
        {
            if (item.SourcePath is null)
            {
                items.Add(item);
                continue;
            }

            if (IsRemote(item.SourcePath))
            {
                missing.Add(item.SourcePath);
                continue;
            }

            var fullPath = Path.GetFullPath(Path.Combine(baseDirectory, item.SourcePath));
            if (!File.Exists(fullPath))
            {
                missing.Add(fullPath);
                continue;
            }

            if (item.Kind == ImportItemKind.Image)
            {
                items.Add(item with
                {
                    ImageBytes = File.ReadAllBytes(fullPath),
                    ImageFileName = Path.GetFileName(fullPath),
                    SourcePath = fullPath,
                });
                continue;
            }

            var info = new FileInfo(fullPath);
            if (info.Length > DroppedFileImport.MaximumTextBytes)
            {
                missing.Add(fullPath);
                continue;
            }

            items.Add(item with
            {
                Text = File.ReadAllText(fullPath),
                SourcePath = fullPath,
            });
        }

        return new ImportDocument(Title, items, missing);
    }

    private static string? ReadBoardTitle(string markdown)
    {
        using var reader = new StringReader(markdown);
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith("##", StringComparison.Ordinal))
            {
                return null;
            }

            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                var title = line[2..].Trim();
                return title.Length == 0 ? null : title;
            }
        }

        return null;
    }

    private static List<Section> SplitSections(string markdown)
    {
        var sections = new List<Section>();
        string? title = null;
        var body = new List<string>();
        var pendingNewRow = false;
        var started = false;

        void Flush()
        {
            if (!started || title is null)
            {
                body.Clear();
                return;
            }

            sections.Add(new Section(title, string.Join("\n", body).Trim(), pendingNewRow));
            pendingNewRow = false;
            body.Clear();
        }

        using var reader = new StringReader(markdown);
        while (reader.ReadLine() is { } line)
        {
            if (IsThematicBreak(line))
            {
                Flush();
                started = false;
                title = null;
                pendingNewRow = true;
                continue;
            }

            if (line.StartsWith("## ", StringComparison.Ordinal) &&
                !line.StartsWith("###", StringComparison.Ordinal))
            {
                Flush();
                started = true;
                title = line[3..].Trim();
                if (title.Length == 0)
                {
                    title = "Text";
                }

                continue;
            }

            if (started)
            {
                body.Add(line);
            }
        }

        Flush();
        return sections;
    }

    private static ImportItem? Recognize(Section section, ImportCatalog catalog)
    {
        if (ImagePattern.Match(section.Body) is { Success: true } image)
        {
            var path = NormalizeLinkTarget(image.Groups["path"].Value);
            var title = section.Title == "Text"
                ? NullIfEmpty(image.Groups["alt"].Value) ??
                  Path.GetFileNameWithoutExtension(path) ??
                  "Image"
                : section.Title;
            return new ImportItem
            {
                Title = title,
                Kind = ImportItemKind.Image,
                SourcePath = path,
                StartNewRow = section.StartNewRow,
            };
        }

        if (TryFindFence(section.Body, out var fenceTag, out var fenceBody) &&
            catalog.LanguageForFence(fenceTag) is { } fencedLanguage)
        {
            return new ImportItem
            {
                Title = section.Title,
                Kind = ImportItemKind.Text,
                LanguageId = fencedLanguage.Id,
                Text = fenceBody,
                StartNewRow = section.StartNewRow,
            };
        }

        foreach (Match link in LinkPattern.Matches(section.Body))
        {
            var path = NormalizeLinkTarget(link.Groups["path"].Value);
            var extension = Path.GetExtension(path);
            if (catalog.IsImageExtension(extension))
            {
                return new ImportItem
                {
                    Title = section.Title,
                    Kind = ImportItemKind.Image,
                    SourcePath = path,
                    StartNewRow = section.StartNewRow,
                };
            }

            if (catalog.LanguageForExtension(extension) is { } language)
            {
                return new ImportItem
                {
                    Title = section.Title,
                    Kind = ImportItemKind.Text,
                    LanguageId = language.Id,
                    SourcePath = path,
                    StartNewRow = section.StartNewRow,
                };
            }
        }

        if (string.IsNullOrWhiteSpace(section.Body))
        {
            return null;
        }

        return new ImportItem
        {
            Title = section.Title,
            Kind = ImportItemKind.Text,
            LanguageId = TextLanguageIds.Plain,
            Text = section.Body.Trim(),
            StartNewRow = section.StartNewRow,
        };
    }

    private static bool TryFindFence(string body, out string tag, out string contents)
    {
        tag = string.Empty;
        contents = string.Empty;
        using var reader = new StringReader(body);
        string? line;
        string? opening = null;
        string? fenceTag = null;
        var inner = new List<string>();
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.TrimStart();
            if (opening is null)
            {
                if (!trimmed.StartsWith("```", StringComparison.Ordinal) &&
                    !trimmed.StartsWith("~~~", StringComparison.Ordinal))
                {
                    continue;
                }

                opening = trimmed[..3];
                fenceTag = trimmed[3..].Trim();
                var space = fenceTag.IndexOfAny([' ', '\t']);
                if (space >= 0)
                {
                    fenceTag = fenceTag[..space];
                }

                continue;
            }

            if (trimmed.StartsWith(opening, StringComparison.Ordinal))
            {
                tag = fenceTag ?? string.Empty;
                contents = string.Join("\n", inner).Trim();
                return true;
            }

            inner.Add(line);
        }

        return false;
    }

    private static bool IsThematicBreak(string line)
    {
        var compact = string.Concat(line.Where(character => !char.IsWhiteSpace(character)));
        return compact.Length >= 3 &&
               compact.Distinct().Count() == 1 &&
               compact[0] is '-' or '*' or '_';
    }

    private static string NormalizeLinkTarget(string raw)
    {
        var path = raw.Trim();
        if (path.Length >= 2 && path[0] == '<' && path[^1] == '>')
        {
            path = path[1..^1].Trim();
        }

        var hash = path.IndexOf('#', StringComparison.Ordinal);
        return hash >= 0 ? path[..hash] : path;
    }

    private static bool IsRemote(string path) =>
        path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record Section(string Title, string Body, bool StartNewRow);
}
