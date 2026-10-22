# Renders Assets\tinyfan.svg into a transparent multi-resolution Windows icon.
# The vector keeps the original angular T and uses Silicon Tuner's purple palette.
# Render the deliberately small polygon/gradient SVG vocabulary with System.Drawing;
# no image editor, network dependency or background-color removal is required.
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$root    = Split-Path -Parent $MyInvocation.MyCommand.Path
$srcPath = Join-Path $root 'Assets\tinyfan.svg'
$outPath = Join-Path $root 'Assets\stressced.ico'
$rootOut = Join-Path $root 'stressced.ico'

if (-not (Test-Path $srcPath)) {
    throw "Source image not found: $srcPath"
}

Write-Host ("Using vector: {0}" -f $srcPath)
[xml]$svg = Get-Content -LiteralPath $srcPath -Raw
$ns = New-Object System.Xml.XmlNamespaceManager($svg.NameTable)
$ns.AddNamespace('s', 'http://www.w3.org/2000/svg')
$culture = [Globalization.CultureInfo]::InvariantCulture
function Get-Polygon($node) {
    [System.Drawing.PointF[]]@(($node.points -split '\s+' | Where-Object { $_ }) | ForEach-Object {
        $xy = $_ -split ','
        New-Object System.Drawing.PointF([float]::Parse($xy[0], $culture), [float]::Parse($xy[1], $culture))
    })
}

$canvas = 1024
$master = New-Object System.Drawing.Bitmap($canvas, $canvas, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($master)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.ScaleTransform(4, 4)
$mark = New-Object System.Drawing.Drawing2D.GraphicsPath
$mark.AddPolygon([System.Drawing.PointF[]](Get-Polygon ($svg.SelectSingleNode('//s:polygon[@id="mark"]', $ns))))
$gradient = $svg.SelectSingleNode('//s:linearGradient', $ns)
$brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    (New-Object System.Drawing.PointF(16, 35)),
    (New-Object System.Drawing.PointF(195.2, 221)),
    [System.Drawing.Color]::Black, [System.Drawing.Color]::White)
$stops = @($gradient.SelectNodes('s:stop', $ns))
$blend = New-Object System.Drawing.Drawing2D.ColorBlend($stops.Count)
$blend.Colors = [System.Drawing.Color[]]@($stops | ForEach-Object { [System.Drawing.ColorTranslator]::FromHtml($_.'stop-color') })
$blend.Positions = [single[]]@($stops | ForEach-Object { if ($_.offset) { [single]::Parse($_.offset, $culture) } else { 0 } })
$brush.InterpolationColors = $blend
$graphics.FillPath($brush, $mark)
$brush.Dispose()
$graphics.SetClip($mark)
foreach ($group in $svg.SelectNodes('/s:svg/s:g', $ns)) {
    foreach ($polygon in $group.SelectNodes('s:polygon', $ns)) {
        $color = [System.Drawing.ColorTranslator]::FromHtml($(if ($polygon.fill) { $polygon.fill } else { $group.fill }))
        $alpha = if ($polygon.opacity) { [int][Math]::Round(255 * [double]::Parse($polygon.opacity, $culture)) } else { 255 }
        $fill = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb($alpha, $color))
        $graphics.FillPolygon($fill, [System.Drawing.PointF[]](Get-Polygon $polygon))
        $fill.Dispose()
    }
}
$graphics.Dispose()
$mark.Dispose()

# --- Pass 3: Render each multi-resolution size and encode frames ---
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
        # Full-resolution header image; avoid enlarging the ICO's 16px first frame.
        $bmp.Save((Join-Path $root 'Assets\tinyfan.png'), [System.Drawing.Imaging.ImageFormat]::Png)
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $frames += ,(@{ png = $true; data = $ms.ToArray() })
    } else {
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

# --- Pass 4: Write .ico file binary stream ---
function Write-IcoFile($targetPath) {
    $fs  = New-Object System.IO.FileStream($targetPath, [System.IO.FileMode]::Create)
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
}

Write-IcoFile $outPath
Write-IcoFile $rootOut

$master.Dispose()
Write-Host ("Wrote {0} ({1:N0} bytes)" -f $outPath, (Get-Item $outPath).Length)
Write-Host ("Wrote {0} ({1:N0} bytes)" -f $rootOut, (Get-Item $rootOut).Length)
