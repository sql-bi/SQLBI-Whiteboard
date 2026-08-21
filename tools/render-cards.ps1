<#
    Renders the card artwork from SVG to PNG with Chrome, which is already on any
    machine that builds this project and rasterizes the same way the site does:
    same engine, same Segoe fonts, so an export matches the page preview.

    Usage:  pwsh tools/render-cards.ps1
#>

$ErrorActionPreference = 'Stop'

$chrome = @(
    "$env:ProgramFiles\Google\Chrome\Application\chrome.exe",
    "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe",
    "$env:LOCALAPPDATA\Google\Chrome\Application\chrome.exe",
    "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe",
    "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $chrome) { throw "Chrome or Edge is required to render the cards." }

$site = (Resolve-Path (Join-Path $PSScriptRoot '..\site')).Path
$work = Join-Path ([System.IO.Path]::GetTempPath()) 'whiteboard-cards'
New-Item -ItemType Directory -Force -Path $work | Out-Null

# Source, output, and the width to render at. Height follows the viewBox.
# A social card is fetched by a scraper rather than examined, so it is sized for
# delivery: wide enough for a high-density display, small enough to always fetch.
$cards = @(
    @{ svg = 'og-image.svg';         png = 'og-image.png';         width = 1200 },
    @{ svg = 'hero.svg';             png = 'hero.png';             width = 1200 },
    @{ svg = 'hero-launch-card.svg'; png = 'hero-launch-card.png'; width = 1200 },
    @{ svg = 'hero-launch.svg';      png = 'hero-launch.png';      width = 1600 }
)

foreach ($card in $cards) {
    $source = Join-Path $site $card.svg
    if (-not (Test-Path $source)) { Write-Host "skipped $($card.svg): not found"; continue }

    $viewBox = ([regex]'viewBox="0 0 ([\d.]+) ([\d.]+)"').Match((Get-Content $source -Raw))
    $height = [Math]::Round($card.width * [double]$viewBox.Groups[2].Value / [double]$viewBox.Groups[1].Value)

    # A wrapper page pins the size and drops the document margin that would
    # otherwise offset the artwork and bring in scrollbars.
    $page = Join-Path $work ($card.png + '.html')
    $uri = ([Uri]$source).AbsoluteUri
    @"
<!doctype html><meta charset="utf-8">
<style>html,body{margin:0;padding:0;overflow:hidden}
img{display:block;width:$($card.width)px;height:${height}px}</style>
<img src="$uri">
"@ | Set-Content -Path $page -Encoding utf8

    $out = Join-Path $site $card.png

    # Remove the previous export first: the wait below watches for the file to
    # appear, and a stale copy would satisfy it even when the render failed.
    Remove-Item $out -Force -ErrorAction SilentlyContinue

    # A private profile: headless refuses to start against a running Chrome.
    & $chrome --headless --disable-gpu --hide-scrollbars `
        --user-data-dir="$work\profile" `
        --screenshot="$out" --window-size="$($card.width),$height" `
        ([Uri]$page).AbsoluteUri *> $null

    # Chrome can still be flushing the file as it exits, so wait for it to appear
    # and then settle on a stable size before reporting.
    $deadline = (Get-Date).AddSeconds(8)
    while (-not (Test-Path $out) -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 150
    }
    if (-not (Test-Path $out)) { throw "Chrome did not write $out" }
    $kb = 0
    do { $previous = $kb; Start-Sleep -Milliseconds 150; $kb = [Math]::Round((Get-Item $out).Length / 1KB) }
    while ($kb -ne $previous)
    Write-Host ("{0}: {1}x{2}, {3} KB" -f $card.png, $card.width, $height, $kb)
}
