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
    # Defaults to VersionPrefix in Directory.Build.props, the one place the version is defined.
    [string] $Version,
    [ValidateSet('x64')]
    [string] $Architecture = 'x64',
    [ValidateSet('true', 'false')]
    [string] $SelfContained = 'true',
    [string] $Configuration = 'Release',
    # Supply an existing publish folder to package binaries that are already built, and
    # signed. The build pipeline does this so signing happens between publish and packaging.
    [string] $PublishFolder,
    [string] $OutputFolder,
    # Which channel/scope combinations to package. Anything that ships must build all four.
    # Pull request validation restricts this to a diagonal pair, because packaging is almost
    # entirely CAB compression and the authoring being validated is the same either way.
    [ValidateSet('stable/perMachine', 'stable/perUser', 'dev/perMachine', 'dev/perUser')]
    [string[]] $Variants = @('stable/perMachine', 'stable/perUser', 'dev/perMachine', 'dev/perUser')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src/SQLBI.Whiteboard/SQLBI.Whiteboard.csproj'
$installerRoot = Join-Path $repoRoot 'installer/wix'
$skipPublish = -not [string]::IsNullOrWhiteSpace($PublishFolder)
$publishFolder = if ($skipPublish) { $PublishFolder } else { Join-Path $repoRoot 'artifacts/publish' }
$outputFolder = if ([string]::IsNullOrWhiteSpace($OutputFolder)) {
    Join-Path $repoRoot 'artifacts/installer'
} else {
    $OutputFolder
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = (dotnet msbuild $project -getProperty:VersionPrefix --nologo).Trim()
    if ($LASTEXITCODE -ne 0) { throw "Could not read VersionPrefix from $project" }
    Write-Host "Version from Directory.Build.props: $Version" -ForegroundColor DarkGray
}

$suffix = if ($SelfContained -eq 'true') { '' } else { '-frameworkdependent' }
$artifactName = "SQLBI.Whiteboard.$Version.$Architecture$suffix"

# WiX v4 requires a four-part numeric version; the informational version keeps the semver string.
$fileVersion = if ($Version -match '^\d+\.\d+\.\d+$') { "$Version.0" } else { $Version }

if (-not $skipPublish -and (Test-Path $publishFolder)) { Remove-Item $publishFolder -Recurse -Force }
foreach ($folder in @($publishFolder, $outputFolder)) {
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
}

if ($skipPublish) {
    Write-Host "==> Packaging the existing publish folder $publishFolder" -ForegroundColor Cyan
} else {
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
}

Write-Host '==> Restoring the WiX tool' -ForegroundColor Cyan
Push-Location $repoRoot
try {
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed with exit code $LASTEXITCODE" }

    # Extensions are per-machine state rather than part of the tool manifest, so they are
    # added on every run. Adding one that is already present is a no-op.
    $manifest = Get-Content (Join-Path $repoRoot '.config/dotnet-tools.json') -Raw | ConvertFrom-Json
    $wixVersion = $manifest.tools.wix.version
    Write-Host "==> Adding the WiX $wixVersion extensions" -ForegroundColor Cyan
    foreach ($extension in @('WixToolset.UI.wixext', 'WixToolset.Util.wixext')) {
        dotnet wix extension add -g "$extension/$wixVersion"
        if ($LASTEXITCODE -ne 0) { throw "Adding $extension failed with exit code $LASTEXITCODE" }
    }

    # Note: '-d Name=(expression)' would be split into two arguments by PowerShell,
    # so every value is expanded into a quoted string first.
    $sourceFile = Join-Path $installerRoot 'SQLBI.Whiteboard.wxs'
    $localizationFile = Join-Path $installerRoot 'SQLBI.Whiteboard.en-us.wxl'
    $assetsFolder = Join-Path $installerRoot 'assets'

    # Both channels are built from one publish, so the released installers are produced by
    # the same run that produced the pre-release ones and can be promoted without rebuilding.
    foreach ($variant in $Variants) {
        $channel, $scope = $variant.Split('/')
        $channelSuffix = if ($channel -eq 'dev') { '-dev' } else { '' }
        $scopeSuffix = if ($scope -eq 'perUser') { '-userinstaller' } else { '' }
        $msiPath = Join-Path $outputFolder "$artifactName$channelSuffix$scopeSuffix.msi"

        Write-Host "==> wix build ($channel, $scope)" -ForegroundColor Cyan
        dotnet wix build $sourceFile `
            -arch $Architecture `
            -d "Channel=$channel" `
            -d "Scope=$scope" `
            -d "PublishFolder=$publishFolder" `
            -d "AssetsFolder=$assetsFolder" `
            -ext WixToolset.UI.wixext `
            -ext WixToolset.Util.wixext `
            -culture en-us `
            -loc $localizationFile `
            -pdbtype none `
            -out $msiPath
        if ($LASTEXITCODE -ne 0) {
            throw "wix build ($channel, $scope) failed with exit code $LASTEXITCODE"
        }
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
