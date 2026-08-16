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

dotnet run `
    --project (Join-Path $workspaceDirectory "prototypes\SQLBI.Whiteboard.CalligraphyPrototype\SQLBI.Whiteboard.CalligraphyPrototype.csproj") `
    --configuration $Configuration `
    --no-restore

exit $LASTEXITCODE
