#requires -Version 5.1
<#
.SYNOPSIS
Generate assets/PiggyBank.ico from primitives — pink rounded square with white "PB" text.

.DESCRIPTION
Run from the repo root. Produces a multi-resolution Windows .ico file with sizes
16/24/32/48/64/128/256, suitable for the WPF window icon, taskbar, installer, and
Add/Remove Programs entry.

The PNG-encoded approach keeps the file small (~30 KB total) while still rendering
the rounded background and bold lettering with anti-aliasing on every size. Replace
this generated icon with a designer-supplied one whenever you have something nicer —
the surrounding wiring (csproj ApplicationIcon, vpk pack -i) doesn't care.
#>

param(
    [string]$OutputPath = "assets/PiggyBank.ico"
)

Add-Type -AssemblyName System.Drawing

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$pngBytes = New-Object 'System.Collections.Generic.List[byte[]]'

foreach ($size in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

    # Pink rounded-square background. Slightly darker than vanilla pink so
    # the white text has decent contrast.
    $bg = [System.Drawing.Color]::FromArgb(255, 233, 92, 156)  # #E95C9C
    $brush = New-Object System.Drawing.SolidBrush($bg)

    # Build a rounded-rect path. Corner radius scales with the size so the
    # 16x16 sees a smaller relative radius than the 256x256.
    $r = [int]($size / 6)
    if ($r -lt 2) { $r = 2 }
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    [void]$path.AddArc(0, 0, $r * 2, $r * 2, 180, 90)
    [void]$path.AddArc($size - $r * 2, 0, $r * 2, $r * 2, 270, 90)
    [void]$path.AddArc($size - $r * 2, $size - $r * 2, $r * 2, $r * 2, 0, 90)
    [void]$path.AddArc(0, $size - $r * 2, $r * 2, $r * 2, 90, 90)
    [void]$path.CloseFigure()
    $g.FillPath($brush, $path)

    # White "PB" letters. SizeF gives us the rendered bounding box so we
    # can centre exactly. At 16x16 we drop down to a single "P" — two
    # letters at that scale render as illegible smears.
    $text = if ($size -le 16) { "P" } else { "PB" }
    $fontPx = if ($size -le 16) { $size * 0.7 } else { $size * 0.5 }
    $font = New-Object System.Drawing.Font(
        "Segoe UI",
        $fontPx,
        [System.Drawing.FontStyle]::Bold,
        [System.Drawing.GraphicsUnit]::Pixel)
    $whiteBrush = [System.Drawing.Brushes]::White
    $textSize = $g.MeasureString($text, $font)
    $x = ($size - $textSize.Width) / 2
    # Visual-centre nudge: typographic baselines push perceived centre
    # slightly low; pulling up by ~5% of font height looks right.
    $y = (($size - $textSize.Height) / 2) - ($fontPx * 0.05)
    $g.DrawString($text, $font, $whiteBrush, $x, $y)

    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray()
    $pngBytes.Add($bytes)
    $bmp.Dispose()
    $ms.Dispose()
    Write-Host "Rendered ${size}x${size}: $($bytes.Length) bytes"
}

# Assemble the .ico container:
#   ICONDIR (6 bytes) + ICONDIRENTRY (16 bytes per image) + image payloads.
# 0 in the size byte means "256" — Windows interprets it that way for the
# largest sizes. Each entry's offset is computed cumulatively from the end
# of the directory section.
$count = $pngBytes.Count
$headerSize = 6 + (16 * $count)

# Ensure the output directory exists.
$outputDir = Split-Path -Parent $OutputPath
if ($outputDir -and -not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir | Out-Null
}

$fs = [System.IO.File]::Open($OutputPath, [System.IO.FileMode]::Create)
$bw = New-Object System.IO.BinaryWriter($fs)
try {
    # ICONDIR
    $bw.Write([UInt16]0)        # reserved
    $bw.Write([UInt16]1)        # 1 = .ico
    $bw.Write([UInt16]$count)

    # ICONDIRENTRY array
    $offset = $headerSize
    for ($i = 0; $i -lt $count; $i++) {
        $size = $sizes[$i]
        $sizeByte = if ($size -ge 256) { 0 } else { $size }
        $bw.Write([Byte]$sizeByte)            # width
        $bw.Write([Byte]$sizeByte)            # height
        $bw.Write([Byte]0)                    # palette colours (0 = none)
        $bw.Write([Byte]0)                    # reserved
        $bw.Write([UInt16]1)                  # planes
        $bw.Write([UInt16]32)                 # bits per pixel
        $bw.Write([UInt32]$pngBytes[$i].Length)
        $bw.Write([UInt32]$offset)
        $offset += $pngBytes[$i].Length
    }

    foreach ($bytes in $pngBytes) {
        $bw.Write($bytes)
    }
} finally {
    $bw.Flush()
    $bw.Close()
    $fs.Close()
}

Write-Host "`nWrote $OutputPath (${count} images, $((Get-Item $OutputPath).Length) bytes total)"
