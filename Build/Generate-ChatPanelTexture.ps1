param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectDir
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sourceDirectory = Join-Path $ProjectDir 'Assets\Themes\StalkerApproved'
$targetPath = Join-Path $sourceDirectory 'chat-multichat-panel-exact.jpg'
$expectedSourceSha256 = 'e9e7597a28d1830e1dc0e813e4069d6719b92d771f8889d20aea33f366cdf60d'

$parts = Get-ChildItem -LiteralPath $sourceDirectory -Filter 'chat-multichat-panel-exact.b64.*' |
    Where-Object { $_.Name -match '\.b64\.00[1-4]$' } |
    Sort-Object Name

if ($parts.Count -ne 4) {
    throw "Expected 4 multichat texture chunks, found $($parts.Count) in $sourceDirectory"
}

$builder = New-Object Text.StringBuilder
foreach ($part in $parts) {
    $chunk = (Get-Content -Raw -LiteralPath $part.FullName) -replace '\s', ''
    if ([string]::IsNullOrWhiteSpace($chunk)) {
        throw "Multichat texture chunk is empty: $($part.FullName)"
    }
    [void]$builder.Append($chunk)
}

$sourceBytes = [Convert]::FromBase64String($builder.ToString())
$sha256 = [Security.Cryptography.SHA256]::Create()
try {
    $hashBytes = $sha256.ComputeHash($sourceBytes)
    $actualSourceSha256 = ([BitConverter]::ToString($hashBytes)).Replace('-', '').ToLowerInvariant()
}
finally {
    $sha256.Dispose()
}

if ($actualSourceSha256 -ne $expectedSourceSha256) {
    throw "Multichat source hash mismatch. Expected $expectedSourceSha256, got $actualSourceSha256"
}

$stream = New-Object IO.MemoryStream(,$sourceBytes)
$source = [Drawing.Image]::FromStream($stream)
$clean = [Drawing.Bitmap]::new(512, 254, [Drawing.Imaging.PixelFormat]::Format24bppRgb)
$graphics = [Drawing.Graphics]::FromImage($clean)
$graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
$graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::HighQuality
$graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$graphics.DrawImage($source, 0, 0, 512, 254)

# Create a seamless dark-metal tile from the clean center of the chat history.
$patch = [Drawing.Bitmap]::new(64, 64, [Drawing.Imaging.PixelFormat]::Format24bppRgb)
$patchGraphics = [Drawing.Graphics]::FromImage($patch)
$patchGraphics.DrawImage(
    $source,
    [Drawing.Rectangle]::new(0, 0, 64, 64),
    [Drawing.Rectangle]::new(250, 80, 64, 64),
    [Drawing.GraphicsUnit]::Pixel)
$patchGraphics.Dispose()

$textureBrush = [Drawing.TextureBrush]::new(
    $patch,
    [Drawing.Drawing2D.WrapMode]::TileFlipXY)

# Remove the complete legacy viewer/game-overlay rectangle and all painted empty text.
# Preserve only the outer frame and the main chat-window border.
$graphics.FillRectangle($textureBrush, [Drawing.Rectangle]::new(16, 30, 480, 188))

# Remove every painted input/button frame from the footer. The real WPF controls are
# rendered here, so the texture can never drift above or below the clickable controls.
$graphics.FillRectangle($textureBrush, [Drawing.Rectangle]::new(15, 219, 482, 28))

# Keep the three painted counter frames, but erase their old icons and values.
$graphics.FillRectangle($textureBrush, [Drawing.Rectangle]::new(401, 9, 27, 14))
$graphics.FillRectangle($textureBrush, [Drawing.Rectangle]::new(433, 9, 29, 14))
$graphics.FillRectangle($textureBrush, [Drawing.Rectangle]::new(465, 9, 28, 14))

$textureBrush.Dispose()
$patch.Dispose()

$encoder = [Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() |
    Where-Object { $_.MimeType -eq 'image/jpeg' } |
    Select-Object -First 1
$parameters = [Drawing.Imaging.EncoderParameters]::new(1)
$parameters.Param[0] = [Drawing.Imaging.EncoderParameter]::new([Drawing.Imaging.Encoder]::Quality, [long]95)
$clean.Save($targetPath, $encoder, $parameters)

$parameters.Dispose()
$graphics.Dispose()
$clean.Dispose()
$source.Dispose()
$stream.Dispose()

$check = [Drawing.Image]::FromFile($targetPath)
try {
    if ($check.Width -ne 512 -or $check.Height -ne 254) {
        throw "Generated multichat texture has invalid dimensions: $($check.Width)x$($check.Height)"
    }
}
finally {
    $check.Dispose()
}

if ((Get-Item $targetPath).Length -lt 7000) {
    throw "Generated multichat texture is unexpectedly small: $targetPath"
}

Write-Host "Generated clean STALKER multichat shell without legacy overlay or painted footer controls: $targetPath"
