# Builds src\DawnResourceExtractionTool.App\Assets\logo.ico from logo.png.
# Frames are stored as 32-bit BMP/DIB (not PNG) so that every icon reader -
# Explorer, the taskbar, System.Drawing.Icon and WPF - decodes them correctly.
# ASCII only: Windows PowerShell reads BOM-less scripts as ANSI.
param(
    [string]$Source = (Join-Path $PSScriptRoot '..\logo.png'),
    [string]$Destination = (Join-Path $PSScriptRoot '..\src\DawnResourceExtractionTool.App\Assets\logo.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = New-Object 'System.Collections.Generic.List[byte[]]'

$sourceImage = [System.Drawing.Image]::FromFile((Resolve-Path $Source))

try {
    foreach ($size in $sizes) {
        $bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.DrawImage($sourceImage, 0, 0, $size, $size)
        $graphics.Dispose()

        $rect = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
        $locked = $bitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $pixels = New-Object byte[] ($size * $size * 4)
        [System.Runtime.InteropServices.Marshal]::Copy($locked.Scan0, $pixels, 0, $pixels.Length)
        $bitmap.UnlockBits($locked)
        $bitmap.Dispose()

        $maskStride = [int]([math]::Ceiling($size / 32.0) * 4)
        $maskSize = $maskStride * $size

        $stream = New-Object System.IO.MemoryStream
        $writer = New-Object System.IO.BinaryWriter($stream)

        # BITMAPINFOHEADER: height covers the XOR (colour) and AND (transparency) parts.
        $writer.Write([uint32]40)
        $writer.Write([int32]$size)
        $writer.Write([int32]($size * 2))
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]0)
        $writer.Write([uint32]($size * $size * 4))
        $writer.Write([int32]0)
        $writer.Write([int32]0)
        $writer.Write([uint32]0)
        $writer.Write([uint32]0)

        # DIB rows are stored bottom-up.
        $stride = $size * 4
        for ($row = $size - 1; $row -ge 0; $row--) {
            $writer.Write($pixels, $row * $stride, $stride)
        }
        $writer.Write((New-Object byte[] $maskSize))

        $writer.Flush()
        $frames.Add($stream.ToArray())
        $writer.Dispose()
        $stream.Dispose()
    }
}
finally {
    $sourceImage.Dispose()
}

$out = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($out)
$writer.Write([uint16]0)             # reserved
$writer.Write([uint16]1)             # type: icon
$writer.Write([uint16]$frames.Count) # image count

$offset = 6 + 16 * $frames.Count
for ($i = 0; $i -lt $frames.Count; $i++) {
    $size = $sizes[$i]
    $dimension = if ($size -ge 256) { 0 } else { $size }
    $writer.Write([byte]$dimension)       # width
    $writer.Write([byte]$dimension)       # height
    $writer.Write([byte]0)                # palette colours
    $writer.Write([byte]0)                # reserved
    $writer.Write([uint16]1)              # colour planes
    $writer.Write([uint16]32)             # bits per pixel
    $writer.Write([uint32]$frames[$i].Length)
    $writer.Write([uint32]$offset)
    $offset += $frames[$i].Length
}

foreach ($frame in $frames) { $writer.Write($frame) }
$writer.Flush()

$directory = Split-Path -Parent $Destination
if (-not (Test-Path $directory)) { New-Item -ItemType Directory -Path $directory | Out-Null }
[System.IO.File]::WriteAllBytes($Destination, $out.ToArray())
$writer.Dispose()
$out.Dispose()

Write-Host ("icon written: {0} ({1} frames, {2} bytes)" -f $Destination, $frames.Count, (Get-Item $Destination).Length)
