param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$workspaceDirectory = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $workspaceDirectory ".dotnet"
$env:APPDATA = Join-Path $workspaceDirectory ".appdata"
$env:NUGET_PACKAGES = Join-Path $workspaceDirectory ".packages"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

dotnet restore (Join-Path $workspaceDirectory "Whiteboard.sln") `
    --configfile (Join-Path $workspaceDirectory "NuGet.Config") `
    -p:Platform=x64

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

dotnet build (Join-Path $workspaceDirectory "Whiteboard.sln") `
    --configuration $Configuration `
    --no-restore `
    -p:Platform=x64

exit $LASTEXITCODE
