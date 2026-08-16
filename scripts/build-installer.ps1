#Requires -Version 7.0
<#
.SYNOPSIS
    Publishes SQLBI Whiteboard and builds the per-machine MSI, the per-user MSI,
    and the portable ZIP. Local equivalent of .azure/pipelines/build-whiteboard.yaml,
    minus code signing.

.EXAMPLE
    ./scripts/build-installer.ps1 -Version 1.0.0
#>
[CmdletBinding()]
param(
    [string] $Version = '0.1.0',
    [ValidateSet('x64')]
    [string] $Architecture = 'x64',
    [ValidateSet('true', 'false')]
    [string] $SelfContained = 'true',
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src/SQLBI.Whiteboard/SQLBI.Whiteboard.csproj'
$installerRoot = Join-Path $repoRoot 'installer/wix'
$publishFolder = Join-Path $repoRoot 'artifacts/publish'
$outputFolder = Join-Path $repoRoot 'artifacts/installer'

$suffix = if ($SelfContained -eq 'true') { '' } else { '-frameworkdependent' }
$artifactName = "SQLBI.Whiteboard.$Version.$Architecture$suffix"

# WiX v4 requires a four-part numeric version; the informational version keeps the semver string.
$fileVersion = if ($Version -match '^\d+\.\d+\.\d+$') { "$Version.0" } else { $Version }

foreach ($folder in @($publishFolder, $outputFolder)) {
    if (Test-Path $folder) { Remove-Item $folder -Recurse -Force }
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
}

Write-Host "==> dotnet publish ($Architecture, self-contained=$SelfContained)" -ForegroundColor Cyan
dotnet publish $project `
    --configuration $Configuration `
    --runtime "win-$Architecture" `
    --self-contained $SelfContained `
    --output $publishFolder `
    -p:Version=$fileVersion `
    -p:InformationalVersion=$Version `
    -p:ContinuousIntegrationBuild=true `
    -p:DebugType=none `
    --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

Write-Host '==> Restoring the WiX tool' -ForegroundColor Cyan
Push-Location $repoRoot
try {
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed with exit code $LASTEXITCODE" }

    # Note: '-d Name=(expression)' would be split into two arguments by PowerShell,
    # so every value is expanded into a quoted string first.
    $sourceFile = Join-Path $installerRoot 'SQLBI.Whiteboard.wxs'
    $localizationFile = Join-Path $installerRoot 'SQLBI.Whiteboard.en-us.wxl'
    $assetsFolder = Join-Path $installerRoot 'assets'

    foreach ($scope in @('perMachine', 'perUser')) {
        $msiSuffix = if ($scope -eq 'perUser') { '-userinstaller' } else { '' }
        $msiPath = Join-Path $outputFolder "$artifactName$msiSuffix.msi"

        Write-Host "==> wix build ($scope)" -ForegroundColor Cyan
        dotnet wix build $sourceFile `
            -arch $Architecture `
            -d "Scope=$scope" `
            -d "PublishFolder=$publishFolder" `
            -d "AssetsFolder=$assetsFolder" `
            -ext WixToolset.UI.wixext `
            -ext WixToolset.Util.wixext `
            -culture en-us `
            -loc $localizationFile `
            -pdbtype none `
            -out $msiPath
        if ($LASTEXITCODE -ne 0) { throw "wix build ($scope) failed with exit code $LASTEXITCODE" }
    }
}
finally {
    Pop-Location
}

Write-Host '==> Portable ZIP' -ForegroundColor Cyan
Compress-Archive `
    -Path (Join-Path $publishFolder '*') `
    -DestinationPath (Join-Path $outputFolder "$artifactName-portable.zip") `
    -CompressionLevel Optimal `
    -Force

Write-Host ''
Write-Host 'Artifacts:' -ForegroundColor Green
Get-ChildItem $outputFolder | ForEach-Object {
    '  {0}  ({1:N1} MB)' -f $_.Name, ($_.Length / 1MB)
}
