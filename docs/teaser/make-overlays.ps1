#Requires -Version 7.0
<#
.SYNOPSIS
    Regenerates the keystroke callout overlays for the teaser edit.

.DESCRIPTION
    Draws overlays/ctrl-v.png and overlays/f6.png: dark keycaps on a transparent
    background, sized large (keycap height ~340 px) so they stay crisp when scaled
    down on the Camtasia timeline. Windows-only, like make-assets.ps1.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.Drawing

$outDir = Join-Path $PSScriptRoot 'overlays'
New-Item -ItemType Directory -Force $outDir | Out-Null

$faceColor = [System.Drawing.Color]::FromArgb(0x3A, 0x3A, 0x3A)
$lipColor  = [System.Drawing.Color]::FromArgb(0x1D, 0x1D, 0x1D)
$textColor = [System.Drawing.Color]::White

$keyHeight = 320
$lip       = 18
$radius    = 44
$margin    = 24
$gap       = 52
$keyFont   = [System.Drawing.Font]::new('Segoe UI', 84, [System.Drawing.FontStyle]::Bold)
$plusFont  = [System.Drawing.Font]::new('Segoe UI', 64, [System.Drawing.FontStyle]::Bold)

function Get-RoundedPath([System.Drawing.RectangleF] $r, [float] $rad) {
    $p = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $d = $rad * 2
    $p.AddArc($r.X, $r.Y, $d, $d, 180, 90)
    $p.AddArc($r.Right - $d, $r.Y, $d, $d, 270, 90)
    $p.AddArc($r.Right - $d, $r.Bottom - $d, $d, $d, 0, 90)
    $p.AddArc($r.X, $r.Bottom - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

$centerFmt = [System.Drawing.StringFormat]::new()
$centerFmt.Alignment = 'Center'
$centerFmt.LineAlignment = 'Center'
$centerFmt.FormatFlags = [System.Drawing.StringFormatFlags]::NoWrap -bor [System.Drawing.StringFormatFlags]::NoClip
$centerFmt.Trimming = [System.Drawing.StringTrimming]::None

# Measure once with a throwaway graphics so each canvas is sized exactly.
$probe = [System.Drawing.Graphics]::FromImage([System.Drawing.Bitmap]::new(1, 1))
function Get-KeyWidth([string] $label) {
    $w = $probe.MeasureString($label, $keyFont).Width + 90
    return [Math]::Max($w, $keyHeight)   # single letters get a square key
}

function Save-Overlay([string] $name, [string[]] $keys) {
    $widths = $keys | ForEach-Object { Get-KeyWidth $_ }
    $plusWidth = if ($keys.Count -gt 1) { $probe.MeasureString('+', $plusFont).Width } else { 0 }
    $total = ($widths | Measure-Object -Sum).Sum +
             ($keys.Count - 1) * ($gap * 2 + $plusWidth) + $margin * 2

    $bmp = [System.Drawing.Bitmap]::new([int][Math]::Ceiling($total), $margin * 2 + $keyHeight + $lip)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::Transparent)

    $lipBrush  = [System.Drawing.SolidBrush]::new($lipColor)
    $faceBrush = [System.Drawing.SolidBrush]::new($faceColor)
    $textBrush = [System.Drawing.SolidBrush]::new($textColor)
    $plusBrush = [System.Drawing.SolidBrush]::new($faceColor)

    $x = [float] $margin
    for ($i = 0; $i -lt $keys.Count; $i++) {
        if ($i -gt 0) {
            $plusRect = [System.Drawing.RectangleF]::new($x, $margin, $gap * 2 + $plusWidth, $keyHeight)
            $g.DrawString('+', $plusFont, $plusBrush, $plusRect, $centerFmt)
            $x += $gap * 2 + $plusWidth
        }
        $w = $widths[$i]
        $lipPath  = Get-RoundedPath ([System.Drawing.RectangleF]::new($x, $margin, $w, $keyHeight + $lip)) $radius
        $facePath = Get-RoundedPath ([System.Drawing.RectangleF]::new($x, $margin, $w, $keyHeight)) $radius
        $g.FillPath($lipBrush, $lipPath)
        $g.FillPath($faceBrush, $facePath)
        $g.DrawString($keys[$i], $keyFont, $textBrush,
            [System.Drawing.RectangleF]::new($x, $margin, $w, $keyHeight), $centerFmt)
        $lipPath.Dispose(); $facePath.Dispose()
        $x += $w
    }

    $lipBrush.Dispose(); $faceBrush.Dispose(); $textBrush.Dispose(); $plusBrush.Dispose()
    $g.Dispose()
    $bmp.Save((Join-Path $outDir $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "overlays/$name" -ForegroundColor Green
}

Save-Overlay 'ctrl-v.png' @('Ctrl', 'V')
Save-Overlay 'f6.png' @('F6')

$probe.Dispose()
$centerFmt.Dispose()
$keyFont.Dispose()
$plusFont.Dispose()
