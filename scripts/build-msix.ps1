#Requires -Version 7.0
<#
.SYNOPSIS
    Packs the published Whiteboard binaries into an unsigned MSIX for the Store.

.DESCRIPTION
    Store Identity Version is VersionPrefix with a trailing .0 (0.9.0.0). That is
    not the four-part assembly stamp the MSI uses. The Store re-signs the package,
    so this script does not apply the EV certificate.

    Partner Center overwrites Publisher after the app is associated. Pass
    -Publisher from the Partner Center identity page when packing a Store upload.
#>
[CmdletBinding()]
param(
    [string] $Version,
    [string] $PublishFolder,
    [string] $OutputFolder,
    [string] $PackageName = 'SQLBI.Whiteboard',
    [string] $Publisher = 'CN=SQLBI Corp',
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
    Write-Host "==> makeappx pack ($identityVersion)" -ForegroundColor Cyan
    & $makeAppxPath pack /d $staging /p $msixPath /o
    if ($LASTEXITCODE -ne 0) { throw "makeappx failed with exit code $LASTEXITCODE" }

    Write-Host "  $msixPath" -ForegroundColor Green
    return $msixPath
}
finally {
    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
}
