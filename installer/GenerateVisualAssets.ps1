param(
    [string]$LogoPath = (Join-Path $PSScriptRoot '..\src\Vanta.App\Assets\Vanta Logo.png')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$resolvedLogoPath = (Resolve-Path $LogoPath).Path
$appIconPath = Join-Path $PSScriptRoot '..\src\Vanta.App\Assets\Vanta.ico'
$installerAssetDirectory = Join-Path $PSScriptRoot 'Assets'
$dialogBitmapPath = Join-Path $installerAssetDirectory 'VantaDialog.bmp'
$bannerBitmapPath = Join-Path $installerAssetDirectory 'VantaBanner.bmp'
[IO.Directory]::CreateDirectory($installerAssetDirectory) | Out-Null

$source = [Drawing.Bitmap]::FromFile($resolvedLogoPath)
try {
    $left = $source.Width
    $top = $source.Height
    $right = -1
    $bottom = -1
    for ($y = 0; $y -lt $source.Height; $y++) {
        for ($x = 0; $x -lt $source.Width; $x++) {
            if ($source.GetPixel($x, $y).A -gt 0) {
                $left = [Math]::Min($left, $x)
                $top = [Math]::Min($top, $y)
                $right = [Math]::Max($right, $x)
                $bottom = [Math]::Max($bottom, $y)
            }
        }
    }

    if ($right -lt $left -or $bottom -lt $top) {
        throw 'The logo does not contain any visible pixels.'
    }

    $sourceBounds = [Drawing.Rectangle]::FromLTRB($left, $top, $right + 1, $bottom + 1)

    function Draw-Logo {
        param(
            [Drawing.Graphics]$Graphics,
            [Drawing.Bitmap]$Image,
            [Drawing.Rectangle]$SourceBounds,
            [Drawing.Rectangle]$TargetBounds
        )

        $Graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $Graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $Graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
        $Graphics.DrawImage($Image, $TargetBounds, $SourceBounds, [Drawing.GraphicsUnit]::Pixel)
    }

    $frames = @()
    foreach ($size in @(16, 20, 24, 32, 40, 48, 64, 128, 256)) {
        $bitmap = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([Drawing.Color]::Transparent)
            $padding = [Math]::Max(1, [int][Math]::Round($size * 0.08))
            $available = $size - ($padding * 2)
            $scale = [Math]::Min($available / $sourceBounds.Width, $available / $sourceBounds.Height)
            $width = [Math]::Max(1, [int][Math]::Round($sourceBounds.Width * $scale))
            $height = [Math]::Max(1, [int][Math]::Round($sourceBounds.Height * $scale))
            $target = [Drawing.Rectangle]::new(
                [int](($size - $width) / 2),
                [int](($size - $height) / 2),
                $width,
                $height)
            Draw-Logo $graphics $source $sourceBounds $target

            $stream = [IO.MemoryStream]::new()
            $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
            $frames += [PSCustomObject]@{ Size = $size; Data = $stream.ToArray() }
            $stream.Dispose()
        }
        finally {
            $graphics.Dispose()
            $bitmap.Dispose()
        }
    }

    $iconStream = [IO.File]::Open($appIconPath, [IO.FileMode]::Create, [IO.FileAccess]::Write)
    $writer = [IO.BinaryWriter]::new($iconStream)
    try {
        $writer.Write([UInt16]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]$frames.Count)
        $offset = 6 + (16 * $frames.Count)
        foreach ($frame in $frames) {
            $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
            $writer.Write([byte]$dimension)
            $writer.Write([byte]$dimension)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([UInt16]1)
            $writer.Write([UInt16]32)
            $writer.Write([UInt32]$frame.Data.Length)
            $writer.Write([UInt32]$offset)
            $offset += $frame.Data.Length
        }
        foreach ($frame in $frames) {
            $writer.Write([byte[]]$frame.Data)
        }
    }
    finally {
        $writer.Dispose()
        $iconStream.Dispose()
    }

    $dialog = [Drawing.Bitmap]::new(493, 312, [Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $dialogGraphics = [Drawing.Graphics]::FromImage($dialog)
    try {
        $dialogGraphics.Clear([Drawing.Color]::White)
        $dialogGraphics.FillRectangle([Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(8, 8, 10)), 0, 0, 164, 312)
        $accentPen = [Drawing.Pen]::new([Drawing.Color]::FromArgb(0, 100, 190), 2)
        $subtlePen = [Drawing.Pen]::new([Drawing.Color]::FromArgb(10, 40, 68), 1)
        try {
            for ($offset = -140; $offset -lt 300; $offset += 24) {
                $dialogGraphics.DrawLine($subtlePen, 0, $offset, 164, $offset + 164)
            }
            $dialogGraphics.DrawLine($accentPen, 163, 0, 163, 312)
        }
        finally {
            $accentPen.Dispose()
            $subtlePen.Dispose()
        }
        Draw-Logo $dialogGraphics $source $sourceBounds ([Drawing.Rectangle]::new(25, 103, 114, 96))
        $dialog.Save($dialogBitmapPath, [Drawing.Imaging.ImageFormat]::Bmp)
    }
    finally {
        $dialogGraphics.Dispose()
        $dialog.Dispose()
    }

    $banner = [Drawing.Bitmap]::new(493, 58, [Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $bannerGraphics = [Drawing.Graphics]::FromImage($banner)
    try {
        $bannerGraphics.Clear([Drawing.Color]::White)
        $bannerGraphics.FillRectangle([Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(8, 8, 10)), 435, 7, 44, 44)
        Draw-Logo $bannerGraphics $source $sourceBounds ([Drawing.Rectangle]::new(442, 17, 30, 25))
        $banner.Save($bannerBitmapPath, [Drawing.Imaging.ImageFormat]::Bmp)
    }
    finally {
        $bannerGraphics.Dispose()
        $banner.Dispose()
    }
}
finally {
    $source.Dispose()
}

Write-Output "Generated $appIconPath"
Write-Output "Generated $dialogBitmapPath"
Write-Output "Generated $bannerBitmapPath"
