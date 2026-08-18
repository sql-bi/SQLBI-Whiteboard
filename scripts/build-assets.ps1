#Requires -Version 7.0
<#
.SYNOPSIS
    Regenerates the derived brand assets: the application icon and PNG, the
    installer banner and dialog background, and the Store tile images.

.DESCRIPTION
    The master artwork is src/SQLBI.Whiteboard/Assets/SQLBI.Whiteboard.svg. The generator
    in tools/AssetGenerator mirrors that composition, so a colour change belongs in both
    files. Outputs are committed, so this only needs running when the artwork changes.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$generator = Join-Path $repoRoot 'tools/AssetGenerator/AssetGenerator.csproj'

Write-Host '==> Generating brand assets' -ForegroundColor Cyan
dotnet run --project $generator --configuration Release -- $repoRoot
if ($LASTEXITCODE -ne 0) { throw "Asset generation failed with exit code $LASTEXITCODE" }

Write-Host 'Done.' -ForegroundColor Green
