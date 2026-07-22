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

# Replace the entire live-chat interior with a clean part of the same dark texture.
# This completely removes the old painted viewer overlay and painted empty-state text.
$graphics.DrawImage(
    $source,
    [Drawing.Rectangle]::new(15, 30, 482, 189),
    [Drawing.Rectangle]::new(175, 30, 322, 189),
    [Drawing.GraphicsUnit]::Pixel)

# Clear the old footer position using clean interior texture.
$graphics.DrawImage(
    $source,
    [Drawing.Rectangle]::new(15, 218, 482, 36),
    [Drawing.Rectangle]::new(175, 183, 322, 36),
    [Drawing.GraphicsUnit]::Pixel)

# Move the painted footer frames down by seven pixels so they sit against the
# lower edge of the block instead of floating above the live controls.
$graphics.DrawImage(
    $source,
    [Drawing.Rectangle]::new(15, 226, 482, 28),
    [Drawing.Rectangle]::new(15, 219, 482, 28),
    [Drawing.GraphicsUnit]::Pixel)

function Blank-FrameInterior([Drawing.Rectangle]$target) {
    $graphics.DrawImage(
        $source,
        $target,
        [Drawing.Rectangle]::new(255, 160, 80, 20),
        [Drawing.GraphicsUnit]::Pixel)
}

# Remove all painted icons, captions and values. The real WPF controls are drawn here.
Blank-FrameInterior ([Drawing.Rectangle]::new(402, 10, 24, 12))
Blank-FrameInterior ([Drawing.Rectangle]::new(435, 10, 24, 12))
Blank-FrameInterior ([Drawing.Rectangle]::new(468, 10, 22, 12))
Blank-FrameInterior ([Drawing.Rectangle]::new(184, 231, 90, 17))
Blank-FrameInterior ([Drawing.Rectangle]::new(281, 231, 86, 17))
Blank-FrameInterior ([Drawing.Rectangle]::new(373, 231, 68, 17))
Blank-FrameInterior ([Drawing.Rectangle]::new(449, 231, 14, 17))
Blank-FrameInterior ([Drawing.Rectangle]::new(470, 231, 17, 17))

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

if ((Get-Item $targetPath).Length -lt 8000) {
    throw "Generated multichat texture is unexpectedly small: $targetPath"
}

Write-Host "Generated STALKER multichat texture without old overlay and with lowered footer: $targetPath"
