param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$workspaceDirectory = Split-Path -Parent $PSScriptRoot
$executable = Join-Path $workspaceDirectory "src\SQLBI.Whiteboard\bin\$Configuration\net8.0-windows\SQLBI.Whiteboard.exe"

if (-not (Test-Path $executable)) {
    throw "SQLBI Whiteboard has not been built. Run scripts\build.ps1 first."
}

Start-Process -FilePath $executable
