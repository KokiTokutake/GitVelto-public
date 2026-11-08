# Draws the six overlay icons (same design as the macOS badges: colored disc with a white ring and symbol)
# into windows/src/GitVelto/Assets/overlays/<kind>.ico. The badge sits in the lower-left corner of the icon.
# Symbols: Fluent UI System Icons (Filled), MIT License, Copyright (c) 2020 Microsoft Corporation.
#   powershell -ExecutionPolicy Bypass -File windows/scripts/make-overlays.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
. (Join-Path $PSScriptRoot 'fluent-icons.ps1')

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$outDir = Join-Path $root 'src\GitVelto\Assets\overlays'
New-Item -ItemType Directory -Force $outDir | Out-Null

# kind, color, Fluent icon, symbol size (fraction of the badge)
$kinds = @(
    @('normal',    [System.Drawing.Color]::FromArgb(0x2E, 0xA1, 0x42), 'checkmark', 0.72),
    @('modified',  [System.Drawing.Color]::FromArgb(0xD6, 0x29, 0x2E), 'important', 0.78),
    @('added',     [System.Drawing.Color]::FromArgb(0x0A, 0x69, 0xD9), 'add',       0.70),
    @('conflict',  [System.Drawing.Color]::FromArgb(0xED, 0x9E, 0x00), 'flash',     0.78),
    @('untracked', [System.Drawing.Color]::FromArgb(0x8C, 0x94, 0x9E), 'question',  0.74),
    @('ignored',   [System.Drawing.Color]::FromArgb(0x57, 0x5E, 0x6B), 'subtract',  0.70)
)
$sizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256

function Draw-Badge([int]$size, $color, [string]$icon, [double]$scale) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    # badge: ~72% of the icon (at least 11px), lower-left corner
    $d = [Math]::Max(11.0, [Math]::Round($size * 0.72))
    $x = 0.0; $y = $size - $d
    $ring = [Math]::Max(1.0, $d * 0.09)
    $g.FillEllipse([System.Drawing.Brushes]::White, [single]$x, [single]$y, [single]$d, [single]$d)
    $brush = New-Object System.Drawing.SolidBrush $color
    $g.FillEllipse($brush, [single]($x + $ring), [single]($y + $ring), [single]($d - 2 * $ring), [single]($d - 2 * $ring))
    $path = Get-FluentPath $icon ($d * $scale) ($x + $d / 2) ($y + $d / 2)
    $g.FillPath([System.Drawing.Brushes]::White, $path)
    $g.Dispose()
    return $bmp
}

foreach ($k in $kinds) {
    Save-Ico (Join-Path $outDir "$($k[0]).ico") $sizes { param($s) Draw-Badge $s $k[1] $k[2] $k[3] }
    # preview for documentation / review
    $prev = Draw-Badge 64 $k[1] $k[2] $k[3]
    $prev.Save((Join-Path $outDir "$($k[0]).png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $prev.Dispose()
}
Write-Output "wrote overlays to $outDir"
