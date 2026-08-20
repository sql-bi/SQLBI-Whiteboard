#Requires -Version 7.0
<#
.SYNOPSIS
    Publishes SQLBI Whiteboard and builds the per-machine MSI, the per-user MSI,
    the portable ZIP, and an unsigned released-channel MSIX. Local equivalent of
    .azure/pipelines/build-whiteboard.yaml, minus code signing.

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
$assetsFolder = Join-Path $installerRoot 'assets'
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

$thumbnailDll = Join-Path $publishFolder 'SQLBI.Whiteboard.ThumbnailHandler.dll'
if (-not (Test-Path $thumbnailDll)) {
    $thumbnailProject = Join-Path $repoRoot 'src\SQLBI.Whiteboard.ThumbnailHandler\SQLBI.Whiteboard.ThumbnailHandler.csproj'
    Write-Host "==> dotnet publish thumbnail handler (Native AOT in-proc)" -ForegroundColor Cyan
    $thumbnailOut = Join-Path $repoRoot 'artifacts\thumbnail'
    if (Test-Path $thumbnailOut) { Remove-Item $thumbnailOut -Recurse -Force }
    dotnet publish $thumbnailProject `
        --configuration $Configuration `
        --runtime "win-$Architecture" `
        --self-contained true `
        --output $thumbnailOut `
        -p:Version=$fileVersion `
        -p:InformationalVersion=$Version `
        -p:ContinuousIntegrationBuild=true `
        -p:DebugType=none `
        --nologo
    if ($LASTEXITCODE -ne 0) { throw "Thumbnail handler publish failed with exit code $LASTEXITCODE" }
    Copy-Item (Join-Path $thumbnailOut 'SQLBI.Whiteboard.ThumbnailHandler.dll') $publishFolder -Force
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

# One portable ZIP per channel, differing only by the channel marker, exactly as the
# installers do. Without this a portable pre-release would report itself as the released
# channel and write to the released channel's settings folder, which is the collision
# channel.txt exists to prevent.
foreach ($channel in ($Variants | ForEach-Object { $_.Split('/')[0] } | Sort-Object -Unique)) {
    $channelSuffix = if ($channel -eq 'dev') { '-dev' } else { '' }
    $zipPath = Join-Path $outputFolder "$artifactName$channelSuffix-portable.zip"

    Write-Host "==> Portable ZIP ($channel)" -ForegroundColor Cyan
    Compress-Archive `
        -Path (Join-Path $publishFolder '*') `
        -DestinationPath $zipPath `
        -CompressionLevel Optimal `
        -Force

    # The same file the installer ships, so the two can never disagree about a channel name.
    # The released channel has no marker: its absence is what identifies it.
    $marker = Join-Path $assetsFolder "channel-$channel.txt"
    if (Test-Path $marker) {
        # Staged and added afterward rather than copied into the publish folder, which is
        # shared between channels and, in the pipeline, already signed.
        $staging = Join-Path $outputFolder ".marker-$channel"
        New-Item -ItemType Directory -Path $staging -Force | Out-Null
        try {
            Copy-Item $marker (Join-Path $staging 'channel.txt') -Force
            Compress-Archive -Path (Join-Path $staging 'channel.txt') -DestinationPath $zipPath -Update
        }
        finally {
            Remove-Item $staging -Recurse -Force
        }
    }
}

# One unsigned MSIX for the released channel. Version is VersionPrefix.0, which is
# what the Store accepts. Dev is MSI-only, same as file-type registration.
if ($Variants -contains 'stable/perMachine' -or $Variants -contains 'stable/perUser') {
    $msixScript = Join-Path $PSScriptRoot 'build-msix.ps1'
    Write-Host '==> MSIX (released channel, unsigned)' -ForegroundColor Cyan
    & $msixScript `
        -Version $Version `
        -PublishFolder $publishFolder `
        -OutputFolder $outputFolder `
        -Architecture $Architecture
}

Write-Host ''
Write-Host 'Artifacts:' -ForegroundColor Green
Get-ChildItem $outputFolder | ForEach-Object {
    '  {0}  ({1:N1} MB)' -f $_.Name, ($_.Length / 1MB)
}
