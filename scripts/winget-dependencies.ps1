<#
.SYNOPSIS
    Adds the packages SQLBI Whiteboard depends on to a winget installer manifest.

.DESCRIPTION
    `wingetcreate update` copies the previous version's manifests out of winget-pkgs and
    replaces only the installers, so a dependency added by hand to one submission would
    have to be added again whenever winget-pkgs dropped it. The publish-winget workflow
    runs this between `wingetcreate update --out` and `wingetcreate submit`, so every
    submission declares the dependencies listed here whatever the previous one held.

    Mermaid diagrams need the Microsoft Edge WebView2 Runtime (decision 34). Windows 11
    includes it, and winget skips a dependency that is already installed, so this changes
    something only on a Windows 10 machine without the runtime.

    The manifest is edited as text rather than parsed and rewritten, so the rest of what
    wingetcreate produced, comments included, reaches the pull request unchanged.

.PARAMETER ManifestDirectory
    A folder that contains SQLBI.Whiteboard.installer.yaml, at any depth, as written by
    `wingetcreate update --out`.
#>
param(
    [Parameter(Mandatory)]
    [string]$ManifestDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$dependencies = @('Microsoft.EdgeWebView2Runtime')

$installer = @(Get-ChildItem -LiteralPath $ManifestDirectory -Recurse -File -Filter 'SQLBI.Whiteboard.installer.yaml')
if ($installer.Count -ne 1) {
    throw "Expected one SQLBI.Whiteboard.installer.yaml under $ManifestDirectory, found $($installer.Count)."
}
$path = $installer[0].FullName
$lines = [System.Collections.Generic.List[string]](Get-Content -LiteralPath $path)

$existing = $lines.FindIndex({ param($line) $line -match '^Dependencies:' })
if ($existing -ge 0) {
    # Merging into a block that someone else wrote is where a text edit goes wrong, so a
    # dependency block that is already there has to list exactly what this script would.
    $declared = $lines | Where-Object { $_ -match '^\s*-\s*PackageIdentifier:\s*(\S+)' } |
        ForEach-Object { $Matches[1] }
    $missing = $dependencies | Where-Object { $_ -notin $declared }
    if ($missing) {
        throw "$path already has a Dependencies block without $($missing -join ', '). Edit it by hand."
    }
    Write-Host "$path already declares $($dependencies -join ', ')."
    return
}

$at = $lines.FindIndex({ param($line) $line -match '^Installers:' })
if ($at -lt 0) {
    throw "$path has no Installers: line to place the dependencies before."
}
$block = @('Dependencies:', '  PackageDependencies:') +
    ($dependencies | ForEach-Object { "  - PackageIdentifier: $_" })
$lines.InsertRange($at, [string[]]$block)

# wingetcreate writes UTF-8 without a byte order mark, and winget-pkgs validation expects it.
[System.IO.File]::WriteAllLines($path, $lines, [System.Text.UTF8Encoding]::new($false))
Write-Host "Added $($dependencies -join ', ') to $path."
