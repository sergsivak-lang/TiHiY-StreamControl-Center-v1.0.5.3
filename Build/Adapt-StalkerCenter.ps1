param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectDir
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sourcePath = Join-Path $ProjectDir 'Assets\Themes\StalkerApproved\center-zone-banner.png'
$panelPath = Join-Path $ProjectDir 'Assets\Themes\StalkerApproved\center-zone-panel-exact.png'
if (-not (Test-Path $sourcePath)) {
    throw "STALKER center texture not found: $sourcePath"
}

$targetWidth = 846
$targetHeight = 422
$source = [System.Drawing.Bitmap]::FromFile($sourcePath)
try {
    if ($source.Width -eq $targetWidth -and $source.Height -eq $targetHeight) {
        $source.Dispose()
        $source = $null
        Copy-Item -Force $sourcePath $panelPath
        Write-Host 'STALKER center texture already adapted and assigned to the real panel.'
        return
    }

    $canvas = [System.Drawing.Bitmap]::new($targetWidth, $targetHeight, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($canvas)
    try {
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

        # Full-bleed background: crop to the panel ratio instead of leaving empty sides.
        $sourceRatio = $source.Width / [double]$source.Height
        $targetRatio = $targetWidth / [double]$targetHeight
        if ($sourceRatio -gt $targetRatio) {
            $cropHeight = $source.Height
            $cropWidth = [int][Math]::Round($cropHeight * $targetRatio)
            $cropX = [int](($source.Width - $cropWidth) / 2)
            $cropY = 0
        }
        else {
            $cropWidth = $source.Width
            $cropHeight = [int][Math]::Round($cropWidth / $targetRatio)
            $cropX = 0
            $cropY = [int](($source.Height - $cropHeight) / 2)
        }

        $graphics.DrawImage(
            $source,
            [System.Drawing.Rectangle]::new(0, 0, $targetWidth, $targetHeight),
            $cropX, $cropY, $cropWidth, $cropHeight,
            [System.Drawing.GraphicsUnit]::Pixel)
    }
    finally {
        $graphics.Dispose()
    }

    $tempPath = "$sourcePath.adapted.png"
    $canvas.Save($tempPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $canvas.Dispose()
    $source.Dispose()
    $source = $null

    Move-Item -Force $tempPath $sourcePath
    Copy-Item -Force $sourcePath $panelPath
    Write-Host "STALKER center artwork cropped full-bleed to ${targetWidth}x${targetHeight} and assigned to the real panel."
}
finally {
    if ($null -ne $source) {
        try { $source.Dispose() } catch { }
    }
}
