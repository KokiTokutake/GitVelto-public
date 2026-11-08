# Draws the six overlay icons (same design as the macOS badges: colored disc with a white ring and symbol)
# into windows/src/GitVelto/Assets/overlays/<kind>.ico. The badge sits in the lower-left corner of the icon.
#   powershell -ExecutionPolicy Bypass -File windows/scripts/make-overlays.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$outDir = Join-Path $root 'src\GitVelto\Assets\overlays'
New-Item -ItemType Directory -Force $outDir | Out-Null

$iconFont = 'Segoe Fluent Icons'
$probe = New-Object System.Drawing.FontFamily 'Segoe UI'
try { $null = New-Object System.Drawing.FontFamily $iconFont } catch { $iconFont = 'Segoe MDL2 Assets' }

# kind, color, glyph, font
$kinds = @(
    @('normal',    [System.Drawing.Color]::FromArgb(0x2E, 0xA1, 0x42), [string][char]0xE73E, $iconFont),
    @('modified',  [System.Drawing.Color]::FromArgb(0xD6, 0x29, 0x2E), '!', 'Segoe UI Black'),
    @('added',     [System.Drawing.Color]::FromArgb(0x0A, 0x69, 0xD9), [string][char]0xE710, $iconFont),
    @('conflict',  [System.Drawing.Color]::FromArgb(0xED, 0x9E, 0x00), [string][char]0xE945, $iconFont),
    @('untracked', [System.Drawing.Color]::FromArgb(0x8C, 0x94, 0x9E), '?', 'Segoe UI Black'),
    @('ignored',   [System.Drawing.Color]::FromArgb(0x57, 0x5E, 0x6B), [string][char]0xE738, $iconFont)
)
$sizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256

function Draw-Badge($size, $color, $glyph, $fontName) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    # badge: ~56% of the icon (at least 9px), lower-left corner
    $d = [Math]::Max(9.0, [Math]::Round($size * 0.56))
    $x = 0.0; $y = $size - $d
    $ring = [Math]::Max(1.0, $d * 0.09)
    $g.FillEllipse([System.Drawing.Brushes]::White, [single]$x, [single]$y, [single]$d, [single]$d)
    $brush = New-Object System.Drawing.SolidBrush $color
    $g.FillEllipse($brush, [single]($x + $ring), [single]($y + $ring), [single]($d - 2 * $ring), [single]($d - 2 * $ring))
    # glyph as a path, centered
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $family = New-Object System.Drawing.FontFamily $fontName
    $em = if ($glyph -eq '!' -or $glyph -eq '?') { $d * 0.78 } else { $d * 0.52 }
    $fmt = [System.Drawing.StringFormat]::GenericTypographic
    $path.AddString($glyph, $family, [int][System.Drawing.FontStyle]::Bold, [single]$em, (New-Object System.Drawing.PointF 0, 0), $fmt)
    $b = $path.GetBounds()
    $m = New-Object System.Drawing.Drawing2D.Matrix
    $m.Translate([single]($x + $d / 2 - $b.X - $b.Width / 2), [single]($y + $d / 2 - $b.Y - $b.Height / 2))
    $path.Transform($m)
    $g.FillPath([System.Drawing.Brushes]::White, $path)
    $g.Dispose()
    return $bmp
}

foreach ($k in $kinds) {
    $pngs = @()
    foreach ($s in $sizes) {
        $bmp = Draw-Badge $s $k[1] $k[2] $k[3]
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        $pngs += , @($s, $ms.ToArray())
    }
    $ico = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $ico
    $w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$pngs.Count)
    $offset = 6 + 16 * $pngs.Count
    foreach ($p in $pngs) {
        $dim = if ($p[0] -ge 256) { 0 } else { $p[0] }
        $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([UInt16]1); $w.Write([UInt16]32)
        $w.Write([UInt32]$p[1].Length); $w.Write([UInt32]$offset)
        $offset += $p[1].Length
    }
    foreach ($p in $pngs) { $w.Write([byte[]]$p[1]) }
    $w.Flush()
    [System.IO.File]::WriteAllBytes((Join-Path $outDir "$($k[0]).ico"), $ico.ToArray())
    # preview for documentation / review
    $prev = Draw-Badge 64 $k[1] $k[2] $k[3]
    $prev.Save((Join-Path $outDir "$($k[0]).png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $prev.Dispose()
}
Write-Output "wrote overlays to $outDir"
