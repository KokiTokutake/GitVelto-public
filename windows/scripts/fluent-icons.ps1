# Shared helpers for the icon scripts (dot-source this file).
# Fluent UI System Icons (Filled) — MIT License, Copyright (c) 2020 Microsoft Corporation.
# Source: https://github.com/microsoft/fluentui-system-icons (font + name → code point map, kept in
# windows/third_party/fluentui-system-icons).
Add-Type -AssemblyName System.Drawing

$fluentDir = Join-Path $PSScriptRoot '..\third_party\fluentui-system-icons'
$script:fluentFonts = New-Object System.Drawing.Text.PrivateFontCollection
$script:fluentFonts.AddFontFile((Resolve-Path (Join-Path $fluentDir 'FluentSystemIcons-Filled.ttf')).Path)
$script:fluentFamily = $script:fluentFonts.Families[0]
$script:fluentMap = Get-Content (Join-Path $fluentDir 'FluentSystemIcons-Filled.json') -Raw -Encoding utf8 | ConvertFrom-Json

# Outline of a Fluent icon (24px design) scaled to fit a square of side $box, centered at ($cx, $cy).
function Get-FluentPath([string]$name, [double]$box, [double]$cx, [double]$cy) {
    $key = "ic_fluent_${name}_24_filled"
    $code = $script:fluentMap.$key
    if ($null -eq $code) { throw "unknown Fluent icon: $name" }
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $text = [char]::ConvertFromUtf32([int]$code)
    $path.AddString($text, $script:fluentFamily, 0, [single]240, (New-Object System.Drawing.PointF 0, 0), [System.Drawing.StringFormat]::GenericTypographic)
    # Scale by the em square (not the glyph bounds) so all icons keep Fluent's consistent proportions.
    $em = 240.0
    $b = $path.GetBounds()
    $k = $box / [Math]::Max($em * 0.92, [Math]::Max($b.Width, $b.Height))
    $m = New-Object System.Drawing.Drawing2D.Matrix
    $m.Translate([single]$cx, [single]$cy)
    $m.Scale([single]$k, [single]$k)
    $m.Translate([single](-$b.X - $b.Width / 2), [single](-$b.Y - $b.Height / 2))
    $path.Transform($m)
    return $path
}

# Writes a multi-size .ico (PNG entries). $draw: scriptblock(size) returning a Bitmap.
function Save-Ico([string]$file, [int[]]$sizes, [scriptblock]$draw) {
    $pngs = @()
    foreach ($s in $sizes) {
        $bmp = & $draw $s
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
        $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]$p[1].Length); $w.Write([UInt32]$offset)
        $offset += $p[1].Length
    }
    foreach ($p in $pngs) { $w.Write([byte[]]$p[1]) }
    $w.Flush()
    [System.IO.File]::WriteAllBytes($file, $ico.ToArray())
}
