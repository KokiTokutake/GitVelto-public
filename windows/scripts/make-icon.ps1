# Builds windows/src/GitVelto/Assets/AppIcon.ico (and AppIcon.png) from Resources/AppIcon.png
# (the same artwork as the macOS version). Run again after replacing Resources/AppIcon.png.
#   powershell -ExecutionPolicy Bypass -File windows/scripts/make-icon.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$src = Join-Path $root 'Resources\AppIcon.png'
$outDir = Join-Path $root 'windows\src\GitVelto\Assets'
New-Item -ItemType Directory -Force $outDir | Out-Null

$master = [System.Drawing.Image]::FromFile($src)
try {
    $sizes = 256, 64, 48, 40, 32, 24, 20, 16
    $pngs = @()
    foreach ($s in $sizes) {
        $bmp = New-Object System.Drawing.Bitmap $s, $s
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.DrawImage($master, 0, 0, $s, $s)
        $g.Dispose()
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        $pngs += , @($s, $ms.ToArray())
        if ($s -eq 256) { [System.IO.File]::WriteAllBytes((Join-Path $outDir 'AppIcon.png'), $ms.ToArray()) }
    }

    # ICO container with PNG-compressed images
    $ico = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $ico
    $w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$pngs.Count)
    $offset = 6 + 16 * $pngs.Count
    foreach ($p in $pngs) {
        $s = $p[0]; $data = $p[1]
        $dim = if ($s -ge 256) { 0 } else { $s }
        $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([UInt16]1); $w.Write([UInt16]32)
        $w.Write([UInt32]$data.Length); $w.Write([UInt32]$offset)
        $offset += $data.Length
    }
    foreach ($p in $pngs) { $w.Write([byte[]]$p[1]) }
    $w.Flush()
    [System.IO.File]::WriteAllBytes((Join-Path $outDir 'AppIcon.ico'), $ico.ToArray())
    Write-Output "wrote $outDir\AppIcon.ico"
}
finally { $master.Dispose() }
