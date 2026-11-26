<#
.SYNOPSIS
  Draws Loop's icon (a blue rounded square with a window snapped to its left half) at every size
  Windows uses, and packs them into a .ico.
.EXAMPLE
  .\tools\New-LoopIcon.ps1                      # writes src\Loop.App\Assets\loop.ico
#>
param([string]$OutFile = (Join-Path $PSScriptRoot '..\src\Loop.App\Assets\loop.ico'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-RoundedRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    $p
}

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = [float]$size
    # Background: rounded square with a subtle top-to-bottom gradient.
    $bg = New-RoundedRect 0 0 $s $s ($s * 0.22)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF 0, $s),
        ([System.Drawing.Color]::FromArgb(255, 0x4F, 0x93, 0xF8)), ([System.Drawing.Color]::FromArgb(255, 0x25, 0x63, 0xEB))
    $g.FillPath($brush, $bg)

    # A window outline, with its left half filled: a window snapped into place.
    $stroke = [Math]::Max(1.0, $s * 0.07)
    $inset = $s * 0.20
    $w = $s - 2 * $inset
    $h = $w * 0.80
    $top = ($s - $h) / 2
    $outline = New-RoundedRect $inset $top $w $h ($s * 0.07)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), $stroke
    $pen.LineJoin = 'Round'
    $g.DrawPath($pen, $outline)

    $fill = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $gapPx = [Math]::Max(1.0, $s * 0.035)
    $half = New-RoundedRect ($inset + $stroke / 2 + $gapPx) ($top + $stroke / 2 + $gapPx) ($w / 2 - $stroke / 2 - 1.5 * $gapPx) ($h - $stroke - 2 * $gapPx) ($s * 0.035)
    $g.FillPath($fill, $half)

    $g.Dispose()
    $bmp
}

# The 256px image as PNG (keeps the file small; everything reads PNG at that size).
function ConvertTo-PngEntry([System.Drawing.Bitmap]$bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    , $ms.ToArray()
}

# Smaller images as classic 32-bit DIBs: System.Drawing.Icon (used for the tray icon) can't decode
# PNG entries below 256px. Layout: BITMAPINFOHEADER, bottom-up BGRA pixels, then a 1-bpp AND mask
# (all zero: the alpha channel already says what's transparent).
function ConvertTo-DibEntry([System.Drawing.Bitmap]$bmp) {
    $size = $bmp.Width
    $data = $bmp.LockBits((New-Object System.Drawing.Rectangle 0, 0, $size, $size),
        [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $pixels = New-Object byte[] ($size * $size * 4)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length) # top-down BGRA (stride = size*4)
    $bmp.UnlockBits($data)

    $maskStride = [int]([Math]::Ceiling($size / 32.0) * 4)
    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $ms
    $w.Write([uint32]40); $w.Write([int32]$size); $w.Write([int32]($size * 2))   # height covers XOR + AND
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]0)
    $w.Write([uint32]($pixels.Length + $maskStride * $size)); $w.Write([int32]0); $w.Write([int32]0)
    $w.Write([uint32]0); $w.Write([uint32]0)
    for ($row = $size - 1; $row -ge 0; $row--) { $w.Write($pixels, $row * $size * 4, $size * 4) }
    $w.Write((New-Object byte[] ($maskStride * $size)))
    $w.Flush()
    , $ms.ToArray()
}

# ICO = header + one directory entry per image + the images.
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = New-Object 'System.Collections.Generic.List[byte[]]'   # typed: PowerShell would unroll nested arrays
foreach ($size in $sizes) {
    $bmp = New-IconBitmap $size
    $images.Add([byte[]]$(if ($size -ge 256) { ConvertTo-PngEntry $bmp } else { ConvertTo-DibEntry $bmp }))
    $bmp.Dispose()
}

$out = New-Object System.IO.MemoryStream
$wr = New-Object System.IO.BinaryWriter $out
$wr.Write([uint16]0); $wr.Write([uint16]1); $wr.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }   # 0 means 256
    $wr.Write([byte]$dim); $wr.Write([byte]$dim); $wr.Write([byte]0); $wr.Write([byte]0)
    $wr.Write([uint16]1); $wr.Write([uint16]32)
    $wr.Write([uint32]$images[$i].Length); $wr.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $wr.Write($img) }
$wr.Flush()

New-Item -ItemType Directory -Force -Path (Split-Path $OutFile) | Out-Null
[System.IO.File]::WriteAllBytes((Resolve-Path -LiteralPath (Split-Path $OutFile)).Path + '\' + (Split-Path $OutFile -Leaf), $out.ToArray())
"Wrote $OutFile ($($out.Length) bytes, sizes $($sizes -join ', '))"
