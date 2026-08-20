<#
.SYNOPSIS
    Writes stable.json and dev.json, the release manifests the download page, a future
    in-app update check, and the winget submission all read.

.DESCRIPTION
    Three consumers need the same answer to "what is the newest build, and where is it".
    Each one working it out for itself is how they drift apart: the download page used to
    re-implement the installer file-name matching in JavaScript, and anything else that
    wanted the same answer would have had to copy it.

    The manifests are generated from the GitHub releases API rather than from the build
    that produced the assets, for two reasons. The API reports a sha256 digest for every
    asset, so nothing has to be downloaded or re-hashed to describe it. And generating
    them here means they describe whatever is actually published right now, including
    releases made before this script existed, rather than only releases made after it.

    They are written into site/ and deployed with the rest of the page, so they are served
    same-origin from whiteboard.sqlbi.com. That matters: github.com release-asset URLs send
    no Access-Control-Allow-Origin header, so a browser cannot fetch a manifest published
    as a release asset. api.github.com does allow it, which is why the page's fallback
    still works.

    A channel with no published release produces no file. The contract is that the file
    existing means a release exists, so a consumer that gets a 404 knows to fall back
    rather than having to interpret an empty manifest.

.PARAMETER Repository
    owner/name of the GitHub repository to read releases from.

.PARAMETER OutputFolder
    Folder to write stable.json and dev.json into.

.PARAMETER Token
    Optional GitHub token. Only raises the API rate limit; the data read is public.
#>
[CmdletBinding()]
param(
    [string] $Repository = 'sql-bi/SQLBI-Whiteboard',
    [string] $OutputFolder = (Join-Path $PSScriptRoot '..' 'site'),
    [string] $Token
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$SchemaVersion = 1

# The three assets a visitor can choose between. The framework-dependent build stays a
# pipeline artifact and is deliberately not offered (decision 15), so it is excluded here
# as well as in collect-release-assets.yml.
$InstallerKinds = [ordered]@{
    perMachine = { param($n) $n -like '*.msi' -and $n -notlike '*userinstaller*' -and $n -notlike '*frameworkdependent*' }
    perUser    = { param($n) $n -like '*userinstaller.msi' -and $n -notlike '*frameworkdependent*' }
    portable   = { param($n) $n -like '*portable.zip' -and $n -notlike '*frameworkdependent*' }
}

function ConvertTo-Installer {
    param($Asset)

    # The API reports the digest as 'sha256:<hex>'. Older assets predate the field, so a
    # missing digest is possible and is left absent rather than guessed at - a consumer
    # that requires a hash, such as the winget submission, must fail rather than proceed
    # with one it invented.
    $sha256 = $null
    $digest = if ($Asset.PSObject.Properties.Name -contains 'digest') { $Asset.digest } else { $null }
    if ($digest -and $digest -match '^sha256:([0-9a-fA-F]{64})$') {
        $sha256 = $Matches[1].ToLowerInvariant()
    }

    $installer = [ordered]@{
        name = $Asset.name
        url  = $Asset.browser_download_url
        size = $Asset.size
    }
    if ($sha256) { $installer.sha256 = $sha256 }
    return $installer
}

function New-Manifest {
    param($Release, [string] $Channel)

    $installers = [ordered]@{}
    foreach ($kind in $InstallerKinds.Keys) {
        $match = $Release.assets | Where-Object { & $InstallerKinds[$kind] $_.name } | Select-Object -First 1
        if ($match) { $installers[$kind] = ConvertTo-Installer $match }
    }

    if ($installers.Count -eq 0) {
        Write-Warning "Release $($Release.tag_name) has no publishable installers; skipping $Channel."
        return $null
    }

    # The tag without its leading v, which for a pre-release keeps the -dev.<build> suffix.
    # That makes it a semver pre-release string, so 0.9.3-dev.3241 orders after 0.9.2 and
    # before 0.9.3, and an update check can compare two of these directly. Dropping the
    # suffix would make every pre-release of one version indistinguishable.
    return [ordered]@{
        schemaVersion   = $SchemaVersion
        channel         = $Channel
        version         = ($Release.tag_name -replace '^v', '')
        tag             = $Release.tag_name
        published       = $Release.published_at
        releaseNotesUrl = $Release.html_url
        installers      = $installers
    }
}

$headers = @{
    Accept                 = 'application/vnd.github+json'
    'X-GitHub-Api-Version' = '2022-11-28'
    'User-Agent'           = 'sqlbi-whiteboard-release-manifests'
}
if ($Token) { $headers['Authorization'] = "Bearer $Token" }

# Called here rather than from a function: returning the response through one collapses
# the array of releases into a single object, and everything downstream then filters it
# away and reports that nothing is published.
$uri = "https://api.github.com/repos/$Repository/releases?per_page=50"
try {
    $response = Invoke-RestMethod -Uri $uri -Headers $headers -Method Get
}
catch {
    throw "Could not read releases from $uri - $($_.Exception.Message)"
}

$releases = @($response) | Where-Object { -not $_.draft }

# Select-Object rather than [0]: indexing an empty result throws under Set-StrictMode,
# and a channel with nothing published is an ordinary state, not an error.
$newestStable = $releases | Where-Object { -not $_.prerelease } | Select-Object -First 1
$newestDev = $releases | Where-Object { $_.prerelease } | Select-Object -First 1

$channels = [ordered]@{
    stable = $newestStable
    dev    = $newestDev
}

if (-not (Test-Path $OutputFolder)) {
    New-Item -ItemType Directory -Path $OutputFolder -Force | Out-Null
}

foreach ($channel in $channels.Keys) {
    $path = Join-Path $OutputFolder "$channel.json"
    $release = $channels[$channel]

    if (-not $release) {
        Write-Host "No $channel release published; not writing $channel.json."
        if (Test-Path $path) { Remove-Item $path -Force }
        continue
    }

    $manifest = New-Manifest -Release $release -Channel $channel
    if (-not $manifest) { continue }

    # Depth matters: the default of 2 would flatten the installer objects into type names.
    $json = $manifest | ConvertTo-Json -Depth 8
    Set-Content -Path $path -Value $json -Encoding utf8NoBOM

    $kinds = ($manifest.installers.Keys) -join ', '
    Write-Host "$channel.json -> $($manifest.tag) ($kinds)"
}
