# Draws the context menu icons into windows/src/GitVelto/Assets/menuicons/<action>.ico.
# Style: bold, colorful pictograms (in the spirit of classic Explorer Git clients), built from
# Segoe Fluent Icons glyph outlines that are thickened and filled; a small "action" glyph may be layered
# over a "subject" glyph (e.g. repository + arrow). Works on light and dark menus.
#   powershell -ExecutionPolicy Bypass -File windows/scripts/make-menu-icons.ps1 [-Preview <png>]
param([string]$Preview)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$outDir = Join-Path $root 'src\GitVelto\Assets\menuicons'
New-Item -ItemType Directory -Force $outDir | Out-Null
$font = 'Segoe Fluent Icons'
try { $null = New-Object System.Drawing.FontFamily $font } catch { $font = 'Segoe MDL2 Assets' }

function C($hex) { [System.Drawing.ColorTranslator]::FromHtml($hex) }
$green = C '#2E9E3E'; $blue = C '#1F6FD1'; $red = C '#D9302C'; $gold = C '#E0A21B'; $orange = C '#E87F12'
$purple = C '#8B55D6'; $teal = C '#12998A'; $gray = C '#6E7787'; $brown = C '#A86F32'; $navy = C '#2A4FA8'

# Each icon: list of layers @(glyph, color, scale, dx, dy) — scale/offset are fractions of the icon size.
$big = 1.0
$sub = 0.62   # subject glyph when an action badge is layered on top
$bdg = 0.58   # action badge
$icons = [ordered]@{
    sync           = @(, @(0xE895, $green, $big, 0, 0))
    commit         = @(@(0xE8B7, $gold, $sub, -0.17, 0.17), @(0xE74A, $green, $bdg, 0.2, -0.2))
    log            = @(@(0xE8A5, $gray, $sub, -0.17, -0.12), @(0xE823, $blue, $bdg, 0.2, 0.2))
    diff           = @(, @(0xE7C4, $orange, $big, 0, 0))
    diffPrevious   = @(@(0xE7C4, $orange, $sub, -0.15, -0.15), @(0xE7A7, $blue, $bdg, 0.2, 0.2))
    status         = @(, @(0xE721, $blue, $big, 0, 0))
    repoBrowser    = @(@(0xE8B7, $gold, $sub, -0.17, -0.12), @(0xE721, $blue, $bdg, 0.2, 0.2))
    reflog         = @(@(0xEA41, $gray, $sub, -0.17, -0.12), @(0xE823, $purple, $bdg, 0.2, 0.2))
    refBrowser     = @(@(0xE8FD, $gray, $sub, -0.17, -0.12), @(0xE8EC, $gold, $bdg, 0.2, 0.2))
    pull           = @(, @(0xE896, $green, $big, 0, 0))
    fetch          = @(, @(0xE896, $blue, $big, 0, 0))
    push           = @(, @(0xE898, $red, $big, 0, 0))
    switch         = @(, @(0xE8AB, $navy, $big, 0, 0))
    branch         = @(@(0xF003, $navy, $sub, -0.15, 0.15), @(0xE710, $green, $bdg, 0.2, -0.2))
    tag            = @(, @(0xE8EC, $gold, $big, 0, 0))
    merge          = @(, @(0xEA3C, $navy, $big, 0, 0))
    rebase         = @(, @(0xE8CB, $teal, $big, 0, 0))
    stashSave      = @(@(0xE7B8, $brown, $sub, -0.15, 0.15), @(0xE896, $green, $bdg, 0.2, -0.2))
    stashPop       = @(@(0xE7B8, $brown, $sub, -0.15, 0.15), @(0xE898, $blue, $bdg, 0.2, -0.2))
    stashList      = @(@(0xE7B8, $brown, $sub, -0.15, -0.12), @(0xE8FD, $gray, $bdg, 0.2, 0.2))
    bisectStart    = @(@(0xE7C1, $red, $sub, -0.15, -0.12), @(0xE721, $blue, $bdg, 0.2, 0.2))
    bisectGood     = @(, @(0xE8E1, $green, $big, 0, 0))
    bisectBad      = @(, @(0xE8E0, $red, $big, 0, 0))
    bisectSkip     = @(, @(0xE893, $gray, $big, 0, 0))
    bisectReset    = @(@(0xE7C1, $red, $sub, -0.15, -0.12), @(0xE711, $gray, $bdg, 0.2, 0.2))
    add            = @(, @(0xE710, $blue, $big, 0, 0))
    revert         = @(, @(0xE7A7, $blue, $big, 0, 0))
    resolve        = @(@(0xE7BA, $orange, $sub, -0.15, -0.12), @(0xE73E, $green, $bdg, 0.2, 0.2))
    editConflict   = @(@(0xE7BA, $orange, $sub, -0.15, -0.12), @(0xE70F, $blue, $bdg, 0.2, 0.2))
    delete         = @(, @(0xE711, $red, $big, 0, 0))
    deleteKeep     = @(@(0xE8A5, $gray, $sub, -0.15, -0.12), @(0xE711, $red, $bdg, 0.2, 0.2))
    rename         = @(, @(0xE70F, $orange, $big, 0, 0))
    ignore         = @(, @(0xED1A, $gray, $big, 0, 0))
    blame          = @(@(0xE8A5, $gray, $sub, -0.15, -0.12), @(0xE77B, $purple, $bdg, 0.2, 0.2))
    clean          = @(, @(0xED62, $orange, $big, 0, 0))
    submoduleAdd   = @(@(0xEA86, $teal, $sub, -0.15, 0.15), @(0xE710, $blue, $bdg, 0.2, -0.2))
    submoduleUpdate= @(@(0xEA86, $teal, $sub, -0.15, -0.12), @(0xE72C, $green, $bdg, 0.2, 0.2))
    submoduleSync  = @(@(0xEA86, $teal, $sub, -0.15, -0.12), @(0xE895, $blue, $bdg, 0.2, 0.2))
    createPatch    = @(@(0xE8A5, $gray, $sub, -0.15, 0.15), @(0xE710, $green, $bdg, 0.2, -0.2))
    applyPatch     = @(@(0xE8A5, $gray, $sub, -0.15, -0.12), @(0xE73E, $green, $bdg, 0.2, 0.2))
    export         = @(@(0xE7B8, $brown, $sub, -0.15, -0.12), @(0xEDE1, $blue, $bdg, 0.2, 0.2))
    remotes        = @(, @(0xE774, $blue, $big, 0, 0))
    repoSettings   = @(@(0xE8B7, $gold, $sub, -0.15, -0.12), @(0xE713, $gray, $bdg, 0.2, 0.2))
    clone          = @(@(0xE8B7, $gold, $sub, -0.15, -0.12), @(0xE896, $green, $bdg, 0.2, 0.2))
    init           = @(@(0xE8B7, $gold, $sub, -0.15, 0.15), @(0xE710, $green, $bdg, 0.2, -0.2))
    terminal       = @(, @(0xE756, $gray, $big, 0, 0))
    settings       = @(, @(0xE713, $gray, $big, 0, 0))
    glossary       = @(, @(0xE82D, $teal, $big, 0, 0))
    commandHistory = @(, @(0xE81C, $purple, $big, 0, 0))
    help           = @(, @(0xE9CE, $blue, $big, 0, 0))
    about          = @(, @(0xE946, $blue, $big, 0, 0))
}

$fmt = [System.Drawing.StringFormat]::GenericTypographic
$family = New-Object System.Drawing.FontFamily $font

function Draw-Icon($layers, [int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.Clear([System.Drawing.Color]::Transparent)
    $n = 0
    foreach ($l in $layers) {
        $glyph = [string][char]$l[0]; $color = $l[1]; $scale = $l[2]; $dx = $l[3]; $dy = $l[4]
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $path.AddString($glyph, $family, 0, [single]100, (New-Object System.Drawing.PointF 0, 0), $fmt)
        $b = $path.GetBounds()
        $target = $size * $scale * 0.9
        $k = $target / [Math]::Max($b.Width, $b.Height)
        $m = New-Object System.Drawing.Drawing2D.Matrix
        $m.Translate([single]($size / 2 + $dx * $size), [single]($size / 2 + $dy * $size))
        $m.Scale([single]$k, [single]$k)
        $m.Translate([single](-$b.X - $b.Width / 2), [single](-$b.Y - $b.Height / 2))
        $path.Transform($m)
        # Thicken the thin outline glyph so it reads as a bold pictogram.
        $w = [Math]::Max(1.0, $size * 0.07 * $scale)
        if ($n -gt 0) {
            # badge: cut a gap around it so it stays distinct from the subject
            $gap = New-Object System.Drawing.Pen ([System.Drawing.Color]::Transparent), ([single]($w + [Math]::Max(1.5, $size * 0.09)))
            $gap.LineJoin = 'Round'
            $g.CompositingMode = 'SourceCopy'; $g.DrawPath($gap, $path); $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::Transparent)), $path)
            $g.CompositingMode = 'SourceOver'
        }
        $pen = New-Object System.Drawing.Pen $color, ([single]$w)
        $pen.LineJoin = 'Round'
        $g.DrawPath($pen, $path)
        $g.FillPath((New-Object System.Drawing.SolidBrush $color), $path)
        $n++
    }
    $g.Dispose()
    return $bmp
}

$sizes = 16, 20, 24, 32, 40, 48
foreach ($id in $icons.Keys) {
    $pngs = @()
    foreach ($s in $sizes) {
        $bmp = Draw-Icon $icons[$id] $s
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
        $pngs += , @($s, $ms.ToArray())
    }
    $ico = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $ico
    $w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$pngs.Count)
    $offset = 6 + 16 * $pngs.Count
    foreach ($p in $pngs) {
        $w.Write([byte]$p[0]); $w.Write([byte]$p[0]); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]$p[1].Length); $w.Write([UInt32]$offset)
        $offset += $p[1].Length
    }
    foreach ($p in $pngs) { $w.Write([byte[]]$p[1]) }
    $w.Flush()
    [System.IO.File]::WriteAllBytes((Join-Path $outDir "$id.ico"), $ico.ToArray())
}

if ($Preview) {
    # contact sheet: 16px and 32px on light and dark menu backgrounds
    $ids = @($icons.Keys); $cols = 4; $rowH = 40; $colW = 330
    $sheet = New-Object System.Drawing.Bitmap ($cols * $colW), ([Math]::Ceiling($ids.Count / $cols) * $rowH)
    $g = [System.Drawing.Graphics]::FromImage($sheet)
    $label = New-Object System.Drawing.Font 'Segoe UI', 9
    for ($i = 0; $i -lt $ids.Count; $i++) {
        $x = ($i % $cols) * $colW; $y = [Math]::Floor($i / $cols) * $rowH
        $g.FillRectangle((New-Object System.Drawing.SolidBrush (C '#F9F9F9')), $x, $y, 150, $rowH)
        $g.FillRectangle((New-Object System.Drawing.SolidBrush (C '#2C2C2C')), $x + 150, $y, 80, $rowH)
        foreach ($p in @(@(16, 6, $false), @(32, 30, $false), @(16, 156, $true), @(32, 180, $true))) {
            $b = Draw-Icon $icons[$ids[$i]] $p[0]; $g.DrawImage($b, $x + $p[1], $y + ($rowH - $p[0]) / 2); $b.Dispose()
        }
        $g.DrawString($ids[$i], $label, [System.Drawing.Brushes]::Black, $x + 70, $y + 12)
        $g.DrawString($ids[$i], $label, [System.Drawing.Brushes]::DimGray, $x + 235, $y + 12)
    }
    $sheet.Save($Preview)
}
Write-Output "wrote $($icons.Count) icons to $outDir"
