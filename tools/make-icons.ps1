# Builds b0r3d DeRes's icon, assets\b0r3dDeRes.ico, from the b0r3d cup: art\favicon.ico (qr.b0r3d.org's
# favicon, with hand-made 16, 32, 48 and 64 px pictures) and art\cup.png (the 512 px cup) for the
# sizes the favicon doesn't have. Run it again after changing the art:
#   & tools\make-icons.ps1
#   -Sheet <file.png>   also draws every size on a dark and a light background
# .NET Framework's icon loader turns PNG-packed pictures under 256 px into static (LoafLock's
# lesson), so the 16-64 px pictures are stored as plain bitmaps, plus a 256 px PNG.
param(
    [string]$Sheet = '',
    [string]$OutDir = (Join-Path $PSScriptRoot '..\assets')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# 16-32: small icons at 100-200 % scaling; 32-64: the taskbar and Alt+Tab; 256: Explorer
$Sizes = 16, 20, 24, 32, 40, 48, 64, 256
$Art = Join-Path $PSScriptRoot '..\art'

# the favicon's own pictures, by size
function Read-Frames([string]$path) {
    $b = [IO.File]::ReadAllBytes($path)
    $frames = @{}
    for ($i = 0; $i -lt [BitConverter]::ToUInt16($b, 4); $i++) {
        $o = 6 + 16 * $i
        $size = if ($b[$o] -eq 0) { 256 } else { [int]$b[$o] }
        $len = [int][BitConverter]::ToUInt32($b, $o + 8)
        $off = [int][BitConverter]::ToUInt32($b, $o + 12)
        if ($b[$off] -ne 0x89) { throw "the $size px picture in $path isn't a PNG; this script only reads PNG pictures" }
        $img = [Drawing.Image]::FromStream([IO.MemoryStream]::new($b, $off, $len))
        $frames[$size] = [Drawing.Bitmap]::new($img)
        $img.Dispose()
    }
    return $frames
}
$Frames = Read-Frames (Join-Path $Art 'favicon.ico')
$Cup = [Drawing.Bitmap]::new((Join-Path $Art 'cup.png'))

function Scale([Drawing.Bitmap]$src, [int]$size) {
    $bmp = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.PixelOffsetMode = 'HighQuality'
    $g.CompositingQuality = 'HighQuality'
    $wrap = [Drawing.Imaging.ImageAttributes]::new()
    $wrap.SetWrapMode([Drawing.Drawing2D.WrapMode]::TileFlipXY)  # no faint border pulled in from past the edge
    $g.DrawImage($src, [Drawing.Rectangle]::new(0, 0, $size, $size), 0, 0, $src.Width, $src.Height, [Drawing.GraphicsUnit]::Pixel, $wrap)
    $g.Dispose()
    return $bmp
}

# the cup at $size: the favicon's own picture when it has that size, half of one when it has
# twice the size (so the pixel look survives), otherwise scaled down from the 512 px cup
function New-Cup([int]$size) {
    if ($Frames.ContainsKey($size)) { return [Drawing.Bitmap]::new($Frames[$size]) }
    if ($Frames.ContainsKey($size * 2)) { return Scale $Frames[$size * 2] $size }
    return Scale $Cup $size
}

# A 32-bit bitmap frame: header, bottom-up BGRA rows, then an all-clear AND mask.
function Bmp-Frame([Drawing.Bitmap]$bmp) {
    $n = $bmp.Width
    $ms = New-Object IO.MemoryStream
    $w = New-Object IO.BinaryWriter $ms
    $maskRow = [int]([math]::Ceiling($n / 32.0) * 4)
    $w.Write([UInt32]40); $w.Write([Int32]$n); $w.Write([Int32]($n * 2))
    $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]0)
    $w.Write([UInt32]($n * $n * 4 + $maskRow * $n))
    $w.Write([Int32]0); $w.Write([Int32]0); $w.Write([UInt32]0); $w.Write([UInt32]0)
    $data = $bmp.LockBits((New-Object Drawing.Rectangle 0, 0, $n, $n), 'ReadOnly', ([Drawing.Imaging.PixelFormat]::Format32bppArgb))
    $row = New-Object byte[] ($n * 4)
    for ($y = $n - 1; $y -ge 0; $y--) {
        [Runtime.InteropServices.Marshal]::Copy([IntPtr]($data.Scan0.ToInt64() + $y * $data.Stride), $row, 0, $n * 4)
        $w.Write($row)
    }
    $bmp.UnlockBits($data)
    $w.Write((New-Object byte[] ($maskRow * $n)))
    $w.Flush()
    return , $ms.ToArray()  # the comma keeps PowerShell from unrolling the bytes
}

function Png-Frame([Drawing.Bitmap]$bmp) {
    $ms = New-Object IO.MemoryStream
    $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
    return , $ms.ToArray()
}

function Save-Icon([string]$path) {
    $frames = foreach ($s in $Sizes) {
        $bmp = New-Cup $s
        if ($s -ge 256) { , (Png-Frame $bmp) } else { , (Bmp-Frame $bmp) }
        $bmp.Dispose()
    }
    $fs = [IO.File]::Create($path)
    $w = New-Object IO.BinaryWriter $fs
    $w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$Sizes.Count)
    $offset = 6 + 16 * $Sizes.Count
    for ($i = 0; $i -lt $Sizes.Count; $i++) {
        $s = $Sizes[$i]
        $w.Write([byte]($s % 256)); $w.Write([byte]($s % 256)); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([UInt16]1); $w.Write([UInt16]32)
        $w.Write([UInt32]([byte[]]$frames[$i]).Length); $w.Write([UInt32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($f in $frames) { $w.Write([byte[]]$f) }
    $w.Close()
    "wrote $path"
}

# every size on a dark and a light background, plus 16 px and 32 px blown up
function Save-Sheet([string]$path) {
    $dark = [Drawing.ColorTranslator]::FromHtml('#202020')
    $light = [Drawing.ColorTranslator]::FromHtml('#EEEEEE')
    $small = $Sizes | Where-Object { $_ -le 64 }
    $stripW = 20 + ($small | Measure-Object -Sum).Sum + 12 * $small.Count
    $sheet = [Drawing.Bitmap]::new([int]($stripW + 2 * 128 + 60), 2 * 96 + 40)
    $g = [Drawing.Graphics]::FromImage($sheet)
    $g.Clear([Drawing.Color]::White)
    $g.InterpolationMode = 'NearestNeighbor'
    $g.PixelOffsetMode = 'Half'
    $pics = @{}
    foreach ($s in $small) { $pics[$s] = New-Cup $s }
    $y = 20
    foreach ($bg in $dark, $light) {
        $g.FillRectangle([Drawing.SolidBrush]::new($bg), 10, $y, $stripW, 80)
        $x = 20
        foreach ($s in $small) {
            $g.DrawImage($pics[$s], [int]$x, [int]($y + (80 - $s) / 2), [int]$s, [int]$s)
            $x += $s + 12
        }
        $y += 96
    }
    $x = $stripW + 30
    $g.FillRectangle([Drawing.SolidBrush]::new($dark), $x, 20, 128, 128)
    $g.DrawImage($pics[16], [int]$x, 20, 128, 128)
    $g.FillRectangle([Drawing.SolidBrush]::new($light), $x + 138, 20, 128, 128)
    $g.DrawImage($pics[32], [int]($x + 138), 20, 128, 128)
    foreach ($p in $pics.Values) { $p.Dispose() }
    $g.Dispose()
    $sheet.Save($path, [Drawing.Imaging.ImageFormat]::Png)
    $sheet.Dispose()
    "wrote $path"
}

New-Item -ItemType Directory -Force $OutDir | Out-Null
Save-Icon (Join-Path $OutDir 'b0r3dDeRes.ico')
if ($Sheet) { Save-Sheet $Sheet }
