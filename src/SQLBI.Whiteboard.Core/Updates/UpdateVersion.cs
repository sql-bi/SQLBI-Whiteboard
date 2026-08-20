using System.Text.Json;

namespace SQLBI.Whiteboard.Core.Updates;

public static class UpdateVersion
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static string? ReadManifestVersion(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var manifest = JsonSerializer.Deserialize<UpdateManifestDto>(json, JsonOptions);
            return TryParse(manifest?.Version, out var version) ? Format(version) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static bool TryParse(string? text, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim();
        if (value.StartsWith('v') || value.StartsWith('V'))
        {
            value = value[1..];
        }

        var cut = value.IndexOfAny(['-', '+']);
        if (cut >= 0)
        {
            value = value[..cut];
        }

        if (!Version.TryParse(value, out var parsed))
        {
            return false;
        }

        version = new Version(
            Math.Max(parsed.Major, 0),
            Math.Max(parsed.Minor, 0),
            Math.Max(parsed.Build, 0));
        return true;
    }

    public static bool IsNewer(string? current, string? latest) =>
        TryParse(current, out var currentVersion) &&
        TryParse(latest, out var latestVersion) &&
        latestVersion > currentVersion;

    public static string Format(Version version) =>
        $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";

    private sealed class UpdateManifestDto
    {
        public string? Version { get; set; }
    }
}
