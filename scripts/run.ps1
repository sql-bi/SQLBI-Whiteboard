param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$workspaceDirectory = Split-Path -Parent $PSScriptRoot
$outputRoot = Join-Path $workspaceDirectory "src\SQLBI.Whiteboard\bin\$Configuration"

# The target framework folder is found rather than named. It carries the Windows SDK version
# ("net10.0-windows10.0.26100.0"), so spelling it out here goes stale on every retarget.
$executable = Get-ChildItem -Path $outputRoot -Filter 'SQLBI.Whiteboard.exe' -Recurse -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $executable) {
    throw "SQLBI Whiteboard has not been built. Run scripts\build.ps1 first."
}

Start-Process -FilePath $executable.FullName
