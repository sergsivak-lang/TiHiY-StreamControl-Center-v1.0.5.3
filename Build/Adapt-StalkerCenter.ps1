param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectDir
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sourcePath = Join-Path $ProjectDir 'Assets\Themes\StalkerApproved\center-zone-banner.png'
if (-not (Test-Path $sourcePath)) {
    throw "STALKER center texture not found: $sourcePath"
}

$targetWidth = 846
$targetHeight = 422
$source = [System.Drawing.Bitmap]::FromFile($sourcePath)
try {
    if ($source.Width -eq $targetWidth -and $source.Height -eq $targetHeight) {
        Write-Host 'STALKER center texture already adapted.'
        return
    }

    $canvas = [System.Drawing.Bitmap]::new($targetWidth, $targetHeight, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($canvas)
    try {
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

        # Full-bleed darkened background fills the wide 2:1 panel without empty sides.
        $attributes = [System.Drawing.Imaging.ImageAttributes]::new()
        try {
            $matrix = [System.Drawing.Imaging.ColorMatrix]::new()
            $matrix.Matrix00 = 0.68
            $matrix.Matrix11 = 0.68
            $matrix.Matrix22 = 0.68
            $matrix.Matrix33 = 1.0
            $matrix.Matrix44 = 1.0
            $attributes.SetColorMatrix($matrix)
            $graphics.DrawImage($source, [System.Drawing.Rectangle]::new(0, 0, $targetWidth, $targetHeight), 0, 0, $source.Width, $source.Height, [System.Drawing.GraphicsUnit]::Pixel, $attributes)
        }
        finally {
            $attributes.Dispose()
        }

        # Keep the original artwork undistorted and centered above the background.
        $scale = [Math]::Min(($targetWidth - 28) / [double]$source.Width, ($targetHeight - 16) / [double]$source.Height)
        $drawWidth = [int][Math]::Round($source.Width * $scale)
        $drawHeight = [int][Math]::Round($source.Height * $scale)
        $x = [int](($targetWidth - $drawWidth) / 2)
        $y = [int](($targetHeight - $drawHeight) / 2)
        $graphics.DrawImage($source, $x, $y, $drawWidth, $drawHeight)
    }
    finally {
        $graphics.Dispose()
    }

    $tempPath = "$sourcePath.adapted.png"
    $canvas.Save($tempPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $canvas.Dispose()
    $source.Dispose()
    Move-Item -Force $tempPath $sourcePath
    Write-Host "STALKER center texture adapted to ${targetWidth}x${targetHeight}."
}
finally {
    if ($null -ne $source) {
        try { $source.Dispose() } catch { }
    }
}
