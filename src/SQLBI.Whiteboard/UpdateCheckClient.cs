using System.Net;
using System.Net.Http;
using SQLBI.Whiteboard.Core.Updates;

namespace SQLBI.Whiteboard;

internal sealed class UpdateCheckResult
{
    public string? Version { get; init; }

    public string? ETag { get; init; }

    public bool NotModified { get; init; }
}

internal static class UpdateCheckClient
{
    public const string DownloadUrl = "https://whiteboard.sqlbi.com/#get";

    // The release manifests the download page and the winget submission already read,
    // published beside the site by scripts/build-release-manifests.ps1. Reading the same
    // file is the point: a second source for "what is the newest build" is a second thing
    // that can disagree.
    //
    // The channel picks the file. A pre-release copy asks about pre-releases, which is the
    // only sense in which it is up to date; a released copy asks about releases. The
    // release-asset form of these files cannot serve the pre-release side at all, because
    // releases/latest/download resolves only to the newest full release.
    private static readonly string ManifestUrl =
        AppChannel.Name is null
            ? "https://whiteboard.sqlbi.com/stable.json"
            : "https://whiteboard.sqlbi.com/dev.json";

    // Used when the site cannot be reached. It reports the newest full release whichever
    // channel is asking, so it is a floor rather than an answer, but a stale "up to date"
    // is worse than a slightly conservative one.
    private const string LatestReleaseUrl =
        "https://github.com/sql-bi/SQLBI-Whiteboard/releases/latest";

    private static readonly HttpClient FollowRedirects = CreateClient(allowAutoRedirect: true);

    private static readonly HttpClient NoRedirects = CreateClient(allowAutoRedirect: false);

    public static async Task<UpdateCheckResult?> CheckAsync(
        string? etag,
        CancellationToken cancellationToken)
    {
        var fromManifest = await TryReadManifestAsync(etag, cancellationToken).ConfigureAwait(false);
        if (fromManifest is not null)
        {
            return fromManifest;
        }

        var fromTag = await TryReadLatestTagAsync(cancellationToken).ConfigureAwait(false);
        return fromTag is null
            ? null
            : new UpdateCheckResult { Version = fromTag };
    }

    private static HttpClient CreateClient(bool allowAutoRedirect)
    {
        var client = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = allowAutoRedirect,
        })
        {
            Timeout = TimeSpan.FromSeconds(3),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SQLBI.Whiteboard/" + AppVersion.Informational);
        return client;
    }

    private static async Task<UpdateCheckResult?> TryReadManifestAsync(
        string? etag,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ManifestUrl);
        if (!string.IsNullOrWhiteSpace(etag))
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        }

        using var response = await FollowRedirects
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            return new UpdateCheckResult { NotModified = true, ETag = etag };
        }

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var version = UpdateVersion.ReadManifestVersion(body);
        if (version is null)
        {
            return null;
        }

        return new UpdateCheckResult
        {
            Version = version,
            ETag = response.Headers.ETag?.ToString(),
        };
    }

    private static async Task<string?> TryReadLatestTagAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, LatestReleaseUrl);
        using var response = await NoRedirects
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if ((int)response.StatusCode is < 300 or >= 400)
        {
            return null;
        }

        var location = response.Headers.Location;
        if (location is null)
        {
            return null;
        }

        var path = location.IsAbsoluteUri ? location.AbsolutePath : location.OriginalString;
        var tag = path.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        return UpdateVersion.TryParse(tag, out var version) ? UpdateVersion.Format(version) : null;
    }
}
