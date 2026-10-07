$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

function New-RoundedRectanglePath {
    param(
        [System.Drawing.RectangleF]$Rectangle,
        [float]$Radius
    )

    $diameter = $Radius * 2
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc($Rectangle.Left, $Rectangle.Top, $diameter, $diameter, 180, 90)
    $path.AddArc($Rectangle.Right - $diameter, $Rectangle.Top, $diameter, $diameter, 270, 90)
    $path.AddArc($Rectangle.Right - $diameter, $Rectangle.Bottom - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($Rectangle.Left, $Rectangle.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-BrandBitmap {
    param([int]$Size)

    $bitmap = [System.Drawing.Bitmap]::new(
        $Size,
        $Size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bitmap.SetResolution(96, 96)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

        $margin = [float]($Size * 0.045)
        $bounds = [System.Drawing.RectangleF]::new(
            $margin,
            $margin,
            $Size - ($margin * 2),
            $Size - ($margin * 2))
        $path = New-RoundedRectanglePath $bounds ([float]($Size * 0.22))
        $background = [System.Drawing.SolidBrush]::new(
            [System.Drawing.Color]::FromArgb(255, 10, 15, 28))
        try {
            $graphics.FillPath($background, $path)
        }
        finally {
            $background.Dispose()
            $path.Dispose()
        }

        $borderPath = New-RoundedRectanglePath (
            [System.Drawing.RectangleF]::new(
                [float]($margin * 1.5),
                [float]($margin * 1.5),
                [float]($Size - ($margin * 3)),
                [float]($Size - ($margin * 3)))) ([float]($Size * 0.205))
        $border = [System.Drawing.Pen]::new(
            [System.Drawing.Color]::FromArgb(180, 38, 52, 75),
            [float][Math]::Max(1, $Size * 0.012))
        try {
            $graphics.DrawPath($border, $borderPath)
        }
        finally {
            $border.Dispose()
            $borderPath.Dispose()
        }

        $strokeWidth = [float][Math]::Max(1.5, $Size * 0.085)
        $leftRibbon = [System.Drawing.Drawing2D.GraphicsPath]::new()
        $leftRibbon.AddLines([System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new([float]($Size * 0.21), [float]($Size * 0.31)),
            [System.Drawing.PointF]::new([float]($Size * 0.365), [float]($Size * 0.70)),
            [System.Drawing.PointF]::new([float]($Size * 0.50), [float]($Size * 0.455))))
        $rightRibbon = [System.Drawing.Drawing2D.GraphicsPath]::new()
        $rightRibbon.AddLines([System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new([float]($Size * 0.50), [float]($Size * 0.455)),
            [System.Drawing.PointF]::new([float]($Size * 0.635), [float]($Size * 0.70)),
            [System.Drawing.PointF]::new([float]($Size * 0.79), [float]($Size * 0.31))))
        $whiteRibbon = [System.Drawing.Pen]::new(
            [System.Drawing.Color]::FromArgb(255, 248, 250, 252),
            $strokeWidth)
        $blueRibbon = [System.Drawing.Pen]::new(
            [System.Drawing.Color]::FromArgb(255, 180, 188, 201),
            $strokeWidth)
        foreach ($pen in @($whiteRibbon, $blueRibbon)) {
            $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
            $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
            $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        }
        try {
            $graphics.DrawPath($whiteRibbon, $leftRibbon)
            $graphics.DrawPath($blueRibbon, $rightRibbon)
        }
        finally {
            $whiteRibbon.Dispose()
            $blueRibbon.Dispose()
            $leftRibbon.Dispose()
            $rightRibbon.Dispose()
        }
    }
    finally {
        $graphics.Dispose()
    }
    return $bitmap
}

function Convert-BitmapToPngBytes {
    param([System.Drawing.Bitmap]$Bitmap)

    $stream = [System.IO.MemoryStream]::new()
    try {
        $Bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        return ,$stream.ToArray()
    }
    finally {
        $stream.Dispose()
    }
}

function Write-MultiSizeIcon {
    param(
        [string]$Path,
        [int[]]$Sizes
    )

    $images = [System.Collections.Generic.List[byte[]]]::new()
    foreach ($size in $Sizes) {
        $bitmap = New-BrandBitmap $size
        try {
            $images.Add([byte[]](Convert-BitmapToPngBytes $bitmap))
        }
        finally {
            $bitmap.Dispose()
        }
    }

    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Create)
    $writer = [System.IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$Sizes.Count)
        $offset = 6 + (16 * $Sizes.Count)
        for ($index = 0; $index -lt $Sizes.Count; $index++) {
            $size = $Sizes[$index]
            $writer.Write([byte]$(if ($size -ge 256) { 0 } else { $size }))
            $writer.Write([byte]$(if ($size -ge 256) { 0 } else { $size }))
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$images[$index].Length)
            $writer.Write([uint32]$offset)
            $offset += $images[$index].Length
        }
        foreach ($image in $images) {
            $writer.Write($image)
        }
    }
    finally {
        $writer.Dispose()
        $stream.Dispose()
    }
}

$assetDirectory = $PSScriptRoot
$icon128 = New-BrandBitmap 128
try {
    $icon128.Save(
        (Join-Path $assetDirectory 'icon128.png'),
        [System.Drawing.Imaging.ImageFormat]::Png)
}
finally {
    $icon128.Dispose()
}

$iconMini = New-BrandBitmap 256
try {
    $iconMini.Save(
        (Join-Path $assetDirectory 'icon-mini.png'),
        [System.Drawing.Imaging.ImageFormat]::Png)
}
finally {
    $iconMini.Dispose()
}

Write-MultiSizeIcon (
    Join-Path $assetDirectory 'WBToolbox.ico') @(16, 20, 24, 32, 40, 48, 64, 128, 256)
