# Generates Assets\stressced.ico from stressced-logov1.png.
# Crops to the "S" logo mark (drops the wordmark so it stays legible when tiny),
# knocks out the white background to transparency, and packs several PNG-encoded
# sizes into a single multi-resolution .ico (16..256).

Add-Type -AssemblyName System.Drawing

$root    = Split-Path -Parent $MyInvocation.MyCommand.Path
$srcPath = Join-Path $root 'stressced-logov1.png'
$outPath = Join-Path $root 'Assets\stressced.ico'

$src = New-Object System.Drawing.Bitmap($srcPath)
$W = $src.Width; $H = $src.Height

# The wordmark lives in the lower ~30%. Only scan the top region for the S mark.
$scanBottom = [int]($H * 0.70)

# --- Pass 1: find the bounding box of non-white "ink" in the scan region -------
$minX = $W; $minY = $H; $maxX = 0; $maxY = 0
$rect = New-Object System.Drawing.Rectangle(0, 0, $W, $scanBottom)
$data = $src.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
                      [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$stride = $data.Stride
$bytes  = New-Object byte[] ($stride * $scanBottom)
[System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
$src.UnlockBits($data)

# 32bppArgb byte order in memory is B,G,R,A
for ($y = 0; $y -lt $scanBottom; $y++) {
    $row = $y * $stride
    for ($x = 0; $x -lt $W; $x++) {
        $i = $row + $x * 4
        $b = $bytes[$i]; $g = $bytes[$i + 1]; $r = $bytes[$i + 2]
        $mn = [Math]::Min($r, [Math]::Min($g, $b))
        if ($mn -lt 235) {              # noticeably darker than the white paper => ink
            if ($x -lt $minX) { $minX = $x }
            if ($x -gt $maxX) { $maxX = $x }
            if ($y -lt $minY) { $minY = $y }
            if ($y -gt $maxY) { $maxY = $y }
        }
    }
}

$bw = $maxX - $minX + 1
$bh = $maxY - $minY + 1
Write-Host ("Logo mark bbox: x={0} y={1} w={2} h={3}" -f $minX, $minY, $bw, $bh)

# --- Build a transparent, square master bitmap of the mark ---------------------
$side   = [Math]::Max($bw, $bh)
$margin = [int]($side * 0.14)
$canvas = $side + 2 * $margin
$offX   = $margin + [int](($side - $bw) / 2)
$offY   = $margin + [int](($side - $bh) / 2)

$master = New-Object System.Drawing.Bitmap($canvas, $canvas, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
for ($y = 0; $y -lt $bh; $y++) {
    $row = ($minY + $y) * $stride
    for ($x = 0; $x -lt $bw; $x++) {
        $i = $row + ($minX + $x) * 4
        $b = $bytes[$i]; $g = $bytes[$i + 1]; $r = $bytes[$i + 2]
        $mn = [Math]::Min($r, [Math]::Min($g, $b))
        # Alpha from "how far from white": white paper -> 0, solid ink -> 255,
        # anti-aliased edges keep a soft alpha so the mark stays crisp.
        $a = [int]((255 - $mn) * 1.45)
        if ($a -gt 255) { $a = 255 }
        if ($a -lt 22)  { $a = 0 }
        if ($a -gt 0) {
            $master.SetPixel($offX + $x, $offY + $y,
                [System.Drawing.Color]::FromArgb($a, $r, $g, $b))
        }
    }
}

# --- Render each size, then encode a frame -------------------------------------
# Small sizes go in as classic uncompressed BMP/DIB so System.Drawing.Icon (used
# by the WPF tray icon) can decode them; 256 goes in as PNG (the standard, and
# far smaller). Each frame is the raw bytes stored inside the .ico.
$sizes  = 16, 24, 32, 48, 64, 128, 256
$frames = @()
foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode  = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode    = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.SmoothingMode      = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.DrawImage($master, (New-Object System.Drawing.Rectangle(0, 0, $s, $s)))
    $g.Dispose()

    if ($s -ge 256) {
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $frames += ,(@{ png = $true; data = $ms.ToArray() })
    } else {
        # Read pixels (memory order is B,G,R,A for 32bppArgb).
        $r = New-Object System.Drawing.Rectangle(0, 0, $s, $s)
        $d = $bmp.LockBits($r, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
                           [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $px = New-Object byte[] ($d.Stride * $s)
        [System.Runtime.InteropServices.Marshal]::Copy($d.Scan0, $px, 0, $px.Length)
        $bmp.UnlockBits($d)
        $st = $d.Stride

        $ms = New-Object System.IO.MemoryStream
        $w  = New-Object System.IO.BinaryWriter($ms)
        $w.Write([UInt32]40); $w.Write([Int32]$s); $w.Write([Int32]($s * 2))   # BITMAPINFOHEADER
        $w.Write([UInt16]1);  $w.Write([UInt16]32); $w.Write([UInt32]0); $w.Write([UInt32]0)
        $w.Write([Int32]0);   $w.Write([Int32]0);  $w.Write([UInt32]0); $w.Write([UInt32]0)
        for ($y = $s - 1; $y -ge 0; $y--) {          # XOR bitmap, bottom-up BGRA
            $w.Write($px, $y * $st, $s * 4)
        }
        $maskStride = [int]([Math]::Floor(($s + 31) / 32) * 4)   # AND mask: all opaque (zeros)
        $w.Write((New-Object byte[] ($maskStride * $s)))
        $w.Flush()
        $frames += ,(@{ png = $false; data = $ms.ToArray() })
    }
    $bmp.Dispose()
}

$fs  = New-Object System.IO.FileStream($outPath, [System.IO.FileMode]::Create)
$bw2 = New-Object System.IO.BinaryWriter($fs)
$bw2.Write([UInt16]0)                 # reserved
$bw2.Write([UInt16]1)                 # type: icon
$bw2.Write([UInt16]$sizes.Count)      # image count
$offset = 6 + 16 * $sizes.Count
for ($k = 0; $k -lt $sizes.Count; $k++) {
    $s   = $sizes[$k]
    $len = $frames[$k].data.Length
    $bw2.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))  # width  (0 => 256)
    $bw2.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))  # height (0 => 256)
    $bw2.Write([byte]0)               # palette count
    $bw2.Write([byte]0)               # reserved
    $bw2.Write([UInt16]1)             # color planes
    $bw2.Write([UInt16]32)            # bits per pixel
    $bw2.Write([UInt32]$len)          # bytes in resource
    $bw2.Write([UInt32]$offset)       # offset
    $offset += $len
}
foreach ($f in $frames) { $bw2.Write($f.data) }
$bw2.Flush(); $fs.Close()

$master.Dispose(); $src.Dispose()
Write-Host ("Wrote {0} ({1:N0} bytes)" -f $outPath, (Get-Item $outPath).Length)
