#Requires -Version 7.0
<#
.SYNOPSIS
    Regenerates the two demo images referenced by demo-board.wimport.

.DESCRIPTION
    Draws assets/star-schema.png and assets/sales-trend.png with GDI+ in the brand
    palette (#F42727 / #B71D1D). The outputs are committed; run this only when the
    pictures should change. Windows-only, like the recording itself.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.Drawing

$assetDir = Join-Path $PSScriptRoot 'assets'
New-Item -ItemType Directory -Force $assetDir | Out-Null

$red      = [System.Drawing.Color]::FromArgb(0xF4, 0x27, 0x27)
$darkRed  = [System.Drawing.Color]::FromArgb(0xB7, 0x1D, 0x1D)
$ink      = [System.Drawing.Color]::FromArgb(0x33, 0x33, 0x33)
$muted    = [System.Drawing.Color]::FromArgb(0x77, 0x77, 0x77)
$line     = [System.Drawing.Color]::FromArgb(0xC9, 0xC9, 0xC9)
$cardFill = [System.Drawing.Color]::FromArgb(0xF6, 0xF6, 0xF6)
$cardEdge = [System.Drawing.Color]::FromArgb(0xD4, 0xD4, 0xD4)

# Whiteboard places an imported image at its pixel size in world units
# (ImportLayout.ImageSize, clamped to 900x700), so the pixel size decides how large
# the container lands next to the ~460-unit-wide text containers. The drawings keep
# their 1400-unit logical layout and render at this scale.
$renderScale = 0.5

function New-Canvas([int] $w, [int] $h) {
    $bmp = [System.Drawing.Bitmap]::new([int]($w * $renderScale), [int]($h * $renderScale))
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.ScaleTransform($renderScale, $renderScale)
    $g.Clear([System.Drawing.Color]::White)
    # A hairline frame so the container bounds read on the white board.
    $framePen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(0xE6, 0xE6, 0xE6), 3)
    $g.DrawRectangle($framePen, 1, 1, $w - 3, $h - 3)
    $framePen.Dispose()
    return $bmp, $g
}

function Get-RoundedPath([System.Drawing.RectangleF] $r, [float] $radius) {
    $p = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $d = $radius * 2
    $p.AddArc($r.X, $r.Y, $d, $d, 180, 90)
    $p.AddArc($r.Right - $d, $r.Y, $d, $d, 270, 90)
    $p.AddArc($r.Right - $d, $r.Bottom - $d, $d, $d, 0, 90)
    $p.AddArc($r.X, $r.Bottom - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Draw-CenteredText([System.Drawing.Graphics] $g, [string] $text,
        [System.Drawing.Font] $font, [System.Drawing.Color] $color,
        [System.Drawing.RectangleF] $rect) {
    $brush = [System.Drawing.SolidBrush]::new($color)
    $fmt = [System.Drawing.StringFormat]::new()
    $fmt.Alignment = 'Center'
    $fmt.LineAlignment = 'Center'
    $fmt.FormatFlags = [System.Drawing.StringFormatFlags]::NoWrap -bor [System.Drawing.StringFormatFlags]::NoClip
    $fmt.Trimming = [System.Drawing.StringTrimming]::None
    $g.DrawString($text, $font, $brush, $rect, $fmt)
    $fmt.Dispose(); $brush.Dispose()
}

# ---------------------------------------------------------------- star schema
$bmp, $g = New-Canvas 1400 900
$titleFont = [System.Drawing.Font]::new('Segoe UI', 34, [System.Drawing.FontStyle]::Bold)
$dimFont   = [System.Drawing.Font]::new('Segoe UI', 27, [System.Drawing.FontStyle]::Bold)
$rowFont   = [System.Drawing.Font]::new('Segoe UI', 22)

$factRect = [System.Drawing.RectangleF]::new(470, 360, 460, 180)
$dims = @(
    @{ Name = 'Date';     Rect = [System.Drawing.RectangleF]::new(100,  110, 340, 130) }
    @{ Name = 'Product';  Rect = [System.Drawing.RectangleF]::new(960,  110, 340, 130) }
    @{ Name = 'Customer'; Rect = [System.Drawing.RectangleF]::new(100,  660, 340, 130) }
    @{ Name = 'Store';    Rect = [System.Drawing.RectangleF]::new(960,  660, 340, 130) }
)

$connector = [System.Drawing.Pen]::new($line, 4)
foreach ($d in $dims) {
    $g.DrawLine($connector,
        $d.Rect.X + $d.Rect.Width / 2, $d.Rect.Y + $d.Rect.Height / 2,
        $factRect.X + $factRect.Width / 2, $factRect.Y + $factRect.Height / 2)
}
$connector.Dispose()

$edgePen = [System.Drawing.Pen]::new($cardEdge, 3)
$dimBrush = [System.Drawing.SolidBrush]::new($cardFill)
foreach ($d in $dims) {
    $path = Get-RoundedPath $d.Rect 18
    $g.FillPath($dimBrush, $path)
    $g.DrawPath($edgePen, $path)
    Draw-CenteredText $g $d.Name $dimFont $ink $d.Rect
    $path.Dispose()
}
$dimBrush.Dispose(); $edgePen.Dispose()

$factPath = Get-RoundedPath $factRect 18
$factBrush = [System.Drawing.SolidBrush]::new($red)
$g.FillPath($factBrush, $factPath)
$titleRect = [System.Drawing.RectangleF]::new($factRect.X, $factRect.Y + 24, $factRect.Width, 60)
$rowRect   = [System.Drawing.RectangleF]::new($factRect.X, $factRect.Y + 96, $factRect.Width, 50)
Draw-CenteredText $g 'Sales' $titleFont ([System.Drawing.Color]::White) $titleRect
Draw-CenteredText $g 'Quantity · Net Price' $rowFont ([System.Drawing.Color]::White) $rowRect
$factBrush.Dispose(); $factPath.Dispose()

$g.Dispose()
$bmp.Save((Join-Path $assetDir 'star-schema.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Host 'assets/star-schema.png' -ForegroundColor Green

# ---------------------------------------------------------------- sales trend
$bmp, $g = New-Canvas 1400 800
$chartTitleFont = [System.Drawing.Font]::new('Segoe UI', 32, [System.Drawing.FontStyle]::Bold)
$labelFont      = [System.Drawing.Font]::new('Segoe UI', 20)

$titleBrush = [System.Drawing.SolidBrush]::new($ink)
$g.DrawString('Sales by month', $chartTitleFont, $titleBrush, 70, 48)
$titleBrush.Dispose()

$plot = [System.Drawing.RectangleF]::new(70, 150, 1260, 540)
$gridPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(0xEA, 0xEA, 0xEA), 2)
foreach ($frac in 0.25, 0.5, 0.75) {
    $y = $plot.Bottom - $plot.Height * $frac
    $g.DrawLine($gridPen, $plot.X, $y, $plot.Right, $y)
}
$gridPen.Dispose()

$values = 56, 51, 60, 66, 58, 63, 70, 67, 62, 71, 78, 100
$months = 'Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'
$slot = $plot.Width / $values.Count
$barWidth = $slot * 0.62
$labelBrush = [System.Drawing.SolidBrush]::new($muted)
$fmt = [System.Drawing.StringFormat]::new(); $fmt.Alignment = 'Center'
for ($i = 0; $i -lt $values.Count; $i++) {
    $h = $plot.Height * ($values[$i] / 100)
    $x = $plot.X + $slot * $i + ($slot - $barWidth) / 2
    $barColor = if ($i -eq 11) { $darkRed } else { $red }
    $barBrush = [System.Drawing.SolidBrush]::new($barColor)
    $g.FillRectangle($barBrush, $x, $plot.Bottom - $h, $barWidth, $h)
    $barBrush.Dispose()
    $g.DrawString($months[$i], $labelFont, $labelBrush,
        $plot.X + $slot * $i + $slot / 2, $plot.Bottom + 16, $fmt)
}
$fmt.Dispose(); $labelBrush.Dispose()

$axisPen = [System.Drawing.Pen]::new($line, 3)
$g.DrawLine($axisPen, $plot.X, $plot.Bottom, $plot.Right, $plot.Bottom)
$axisPen.Dispose()

$g.Dispose()
$bmp.Save((Join-Path $assetDir 'sales-trend.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Host 'assets/sales-trend.png' -ForegroundColor Green
