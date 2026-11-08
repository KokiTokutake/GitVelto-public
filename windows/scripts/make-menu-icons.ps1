# Draws the context menu icons into windows/src/GitVelto/Assets/menuicons/<action>.ico.
# Glyphs: Fluent UI System Icons (Filled), MIT License, Copyright (c) 2020 Microsoft Corporation
# (windows/third_party/fluentui-system-icons). Style: colorful pictograms where a small "action" glyph
# (arrow, plus, check...) may be layered over a "subject" glyph (repository, document...), in the spirit of
# classic Explorer Git clients. Works on light and dark menus.
#   powershell -ExecutionPolicy Bypass -File windows/scripts/make-menu-icons.ps1 [-Preview <png>]
param([string]$Preview)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$outDir = Join-Path $root 'src\GitVelto\Assets\menuicons'
New-Item -ItemType Directory -Force $outDir | Out-Null
. (Join-Path $PSScriptRoot 'fluent-icons.ps1')

function C($hex) { [System.Drawing.ColorTranslator]::FromHtml($hex) }
$green = C '#2E9E3E'; $blue = C '#1F6FD1'; $red = C '#D9302C'; $gold = C '#E0A21B'; $orange = C '#E87F12'
$purple = C '#8B55D6'; $teal = C '#12998A'; $gray = C '#6E7787'; $brown = C '#A86F32'; $navy = C '#2A4FA8'

# layer = @(icon name, color, scale, dx, dy) — scale/offsets are fractions of the icon size.
function One($name, $color) { , @(, @($name, $color, 1.0, 0, 0)) }
function Two($subject, $sc, $badge, $bc, [switch]$Top) {
    $dy = if ($Top) { -0.2 } else { 0.2 }
    , @(@($subject, $sc, 0.74, -0.12, (-$dy * 0.6)), @($badge, $bc, 0.56, 0.22, $dy))
}

$icons = [ordered]@{
    sync            = One 'arrow_sync' $green
    commit          = Two 'database' $gold 'arrow_up' $green -Top
    log             = One 'document_text_clock' $navy
    diff            = One 'document_search' $orange
    diffPrevious    = Two 'document_search' $orange 'arrow_counterclockwise' $blue
    status          = One 'search' $blue
    repoBrowser     = One 'database_search' $gold
    reflog          = One 'history' $purple
    refBrowser      = One 'tag_multiple' $gold
    pull            = One 'arrow_download' $green
    fetch           = One 'cloud_arrow_down' $blue
    push            = One 'arrow_upload' $red
    switch          = One 'arrow_swap' $navy
    branch          = Two 'branch' $navy 'add' $green -Top
    tag             = One 'tag' $gold
    merge           = One 'merge' $navy
    rebase          = One 'arrow_routing' $teal
    stashSave       = Two 'archive' $brown 'arrow_down' $green -Top
    stashPop        = Two 'archive' $brown 'arrow_up' $blue -Top
    stashList       = One 'archive_multiple' $brown
    bisectStart     = Two 'flag' $red 'search' $blue
    bisectGood      = One 'thumb_like' $green
    bisectBad       = One 'thumb_dislike' $red
    bisectSkip      = One 'next' $gray
    bisectReset     = Two 'flag' $red 'dismiss' $gray
    add             = One 'add' $blue
    revert          = One 'arrow_undo' $blue
    resolve         = Two 'warning' $orange 'checkmark' $green
    editConflict    = Two 'warning' $orange 'edit' $blue
    delete          = One 'delete' $red
    deleteKeep      = One 'document_dismiss' $red
    rename          = One 'rename' $orange
    ignore          = One 'eye_off' $gray
    blame           = Two 'document' $gray 'person' $purple
    clean           = One 'broom' $orange
    submoduleAdd    = Two 'cube' $teal 'add' $blue -Top
    submoduleUpdate = One 'cube_sync' $teal
    submoduleSync   = Two 'cube' $teal 'arrow_sync' $blue
    createPatch     = Two 'patch' $blue 'add' $green -Top
    applyPatch      = Two 'patch' $blue 'checkmark' $green
    export          = One 'folder_zip' $brown
    remotes         = One 'globe' $blue
    repoSettings    = One 'database_settings' $gold
    clone           = Two 'database' $gold 'arrow_down' $green -Top
    init            = Two 'database' $gold 'add' $green -Top
    terminal        = One 'window_dev_tools' $gray
    settings        = One 'settings' $gray
    glossary        = One 'book' $teal
    commandHistory  = One 'document_bullet_list_clock' $gray
    help            = One 'question_circle' $blue
    about           = One 'info' $blue
}

function Draw-Icon($layers, [int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.Clear([System.Drawing.Color]::Transparent)
    $n = 0
    foreach ($l in $layers) {
        $path = Get-FluentPath $l[0] ($size * $l[2]) ($size / 2 + $l[3] * $size) ($size / 2 + $l[4] * $size)
        if ($n -gt 0) {
            # badge: cut a transparent gap around it so it stays distinct from the subject
            $gap = New-Object System.Drawing.Pen ([System.Drawing.Color]::Transparent), ([single][Math]::Max(1.5, $size * 0.12))
            $gap.LineJoin = 'Round'
            $g.CompositingMode = 'SourceCopy'
            $g.DrawPath($gap, $path)
            $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::Transparent)), $path)
            $g.CompositingMode = 'SourceOver'
        }
        $g.FillPath((New-Object System.Drawing.SolidBrush $l[1]), $path)
        $n++
    }
    $g.Dispose()
    return $bmp
}

foreach ($id in $icons.Keys) {
    Save-Ico (Join-Path $outDir "$id.ico") @(16, 20, 24, 32, 40, 48) { param($s) Draw-Icon $icons[$id] $s }
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
        foreach ($p in @(@(16, 6), @(32, 30), @(16, 156), @(32, 180))) {
            $b = Draw-Icon $icons[$ids[$i]] $p[0]; $g.DrawImage($b, $x + $p[1], $y + ($rowH - $p[0]) / 2); $b.Dispose()
        }
        $g.DrawString($ids[$i], $label, [System.Drawing.Brushes]::Black, $x + 70, $y + 12)
        $g.DrawString($ids[$i], $label, [System.Drawing.Brushes]::DimGray, $x + 235, $y + 12)
    }
    $sheet.Save($Preview)
}
Write-Output "wrote $($icons.Count) icons to $outDir"
