#Requires -Version 7.0
<#
.SYNOPSIS
    Packs the published Whiteboard binaries into an unsigned MSIX for the Store.

.DESCRIPTION
    Store Identity Version is VersionPrefix with a trailing .0 (0.9.1.0). That is
    not the four-part assembly stamp the MSI uses. The Store re-signs the package,
    so this script does not apply the EV certificate.

    Identity Name and Publisher must already match what Partner Center reserved.
    Partner Center validates the identity in the uploaded package and rejects a
    mismatch; it never rewrites the manifest. (Visual Studio's "Associate App with
    the Store" rewrites a local manifest, which is where the opposite belief comes
    from.) The defaults below are the reserved identity, so an ordinary build is
    submittable as-is.

    The package family name is derived from these two values and cannot be set.
    Getting Name and Publisher right yields 17351SQLBICorp.SQLBIWhiteboard_x5fb4jp2zkb6m.

    Override them only to pack a locally installable build: a package can be signed
    only by a certificate whose subject equals Publisher exactly, so sideload testing
    needs a self-signed certificate with a matching subject.
#>
[CmdletBinding()]
param(
    [string] $Version,
    [string] $PublishFolder,
    [string] $OutputFolder,
    [string] $PackageName = '17351SQLBICorp.SQLBIWhiteboard',
    [string] $Publisher = 'CN=922444FE-B5BD-491C-A501-DD2EC37191C8',
    [ValidateSet('x64')]
    [string] $Architecture = 'x64'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src/SQLBI.Whiteboard/SQLBI.Whiteboard.csproj'
$template = Join-Path $repoRoot 'installer/msix/AppxManifest.xml'
$assets = Join-Path $repoRoot 'installer/msix/Assets'

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = (dotnet msbuild $project -getProperty:VersionPrefix --nologo).Trim()
    if ($LASTEXITCODE -ne 0) { throw "Could not read VersionPrefix from $project" }
}

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "MSIX version must be major.minor.patch (VersionPrefix), but reads '$Version'."
}

$identityVersion = "$Version.0"
if ([string]::IsNullOrWhiteSpace($PublishFolder)) {
    $PublishFolder = Join-Path $repoRoot 'artifacts/publish'
}
if ([string]::IsNullOrWhiteSpace($OutputFolder)) {
    $OutputFolder = Join-Path $repoRoot 'artifacts/installer'
}

$exe = Join-Path $PublishFolder 'SQLBI.Whiteboard.exe'
$handler = Join-Path $PublishFolder 'SQLBI.Whiteboard.ThumbnailHandler.dll'
if (-not (Test-Path $exe)) { throw "Published executable not found: $exe" }
if (-not (Test-Path $handler)) { throw "Thumbnail handler not found: $handler" }
if (-not (Test-Path (Join-Path $assets 'StoreLogo.png'))) {
    throw "Store assets are missing. Run scripts/build-assets.ps1 first."
}

$makeAppxPath = $null
$makeAppxCmd = Get-Command makeappx.exe -ErrorAction SilentlyContinue
if ($makeAppxCmd) {
    $makeAppxPath = $makeAppxCmd.Source
} else {
    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (Test-Path $kits) {
        $found = Get-ChildItem $kits -Recurse -Filter makeappx.exe -ErrorAction SilentlyContinue |
            Where-Object { $_.Directory.Name -eq 'x64' } |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($found) { $makeAppxPath = $found.FullName }
    }
}
if (-not $makeAppxPath) {
    throw "makeappx.exe was not found. Install the Windows 10/11 SDK (MakeAppx)."
}

$staging = Join-Path $OutputFolder '.msix-staging'
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Path $staging | Out-Null
New-Item -ItemType Directory -Path $OutputFolder -Force | Out-Null

try {
    Write-Host "==> Stage MSIX payload" -ForegroundColor Cyan
    Copy-Item (Join-Path $PublishFolder '*') $staging -Recurse -Force
    Get-ChildItem $staging -Recurse -Filter *.pdb | Remove-Item -Force
    Copy-Item $assets (Join-Path $staging 'Assets') -Recurse -Force

    $manifest = Get-Content $template -Raw
    $manifest = $manifest.Replace('__PACKAGE_NAME__', $PackageName)
    $manifest = $manifest.Replace('__PUBLISHER__', $Publisher)
    $manifest = $manifest.Replace('__VERSION__', $identityVersion)
    Set-Content -Path (Join-Path $staging 'AppxManifest.xml') -Value $manifest -Encoding utf8

    $msixPath = Join-Path $OutputFolder "SQLBI.Whiteboard.$Version.$Architecture.msix"
    # Print the identity. A Store rejection reads as three separate errors, so
    # having the packed values in the build log is what makes it one glance.
    Write-Host "    Identity Name : $PackageName"
    Write-Host "    Publisher     : $Publisher"
    Write-Host "==> makeappx pack ($identityVersion)" -ForegroundColor Cyan
    & $makeAppxPath pack /d $staging /p $msixPath /o
    if ($LASTEXITCODE -ne 0) { throw "makeappx failed with exit code $LASTEXITCODE" }

    Write-Host "  $msixPath" -ForegroundColor Green
    return $msixPath
}
finally {
    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
}
