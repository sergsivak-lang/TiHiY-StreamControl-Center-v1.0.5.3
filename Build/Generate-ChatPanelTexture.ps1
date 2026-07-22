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
$clean = New-Object Drawing.Bitmap 512,254,[Drawing.Imaging.PixelFormat]::Format24bppRgb
$graphics = [Drawing.Graphics]::FromImage($clean)
$graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
$graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::HighQuality
$graphics.DrawImage($source, 0, 0, 512, 254)

function Copy-CleanPatch([Drawing.Rectangle]$target, [Drawing.Rectangle]$patch) {
    $graphics.DrawImage($source, $target, $patch, [Drawing.GraphicsUnit]::Pixel)
}

# Remove painted counter contents while preserving their frames.
Copy-CleanPatch ([Drawing.Rectangle]::new(400,11,27,10)) ([Drawing.Rectangle]::new(292,31,27,10))
Copy-CleanPatch ([Drawing.Rectangle]::new(433,11,27,10)) ([Drawing.Rectangle]::new(292,31,27,10))
Copy-CleanPatch ([Drawing.Rectangle]::new(465,11,26,10)) ([Drawing.Rectangle]::new(292,31,26,10))

# Remove painted viewer/like labels and the painted empty-state message.
Copy-CleanPatch ([Drawing.Rectangle]::new(18,106,145,12)) ([Drawing.Rectangle]::new(186,32,145,12))
Copy-CleanPatch ([Drawing.Rectangle]::new(172,109,180,25)) ([Drawing.Rectangle]::new(172,49,180,25))

# Remove painted button icons/captions from the bottom strip, leaving only frames.
Copy-CleanPatch ([Drawing.Rectangle]::new(182,221,86,13)) ([Drawing.Rectangle]::new(182,49,86,13))
Copy-CleanPatch ([Drawing.Rectangle]::new(278,221,81,13)) ([Drawing.Rectangle]::new(278,49,81,13))
Copy-CleanPatch ([Drawing.Rectangle]::new(369,221,73,13)) ([Drawing.Rectangle]::new(369,49,73,13))
Copy-CleanPatch ([Drawing.Rectangle]::new(450,221,12,13)) ([Drawing.Rectangle]::new(450,49,12,13))
Copy-CleanPatch ([Drawing.Rectangle]::new(470,221,13,13)) ([Drawing.Rectangle]::new(470,49,13,13))

$encoder = [Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() |
    Where-Object { $_.MimeType -eq 'image/jpeg' } |
    Select-Object -First 1
$parameters = New-Object Drawing.Imaging.EncoderParameters 1
$parameters.Param[0] = New-Object Drawing.Imaging.EncoderParameter([Drawing.Imaging.Encoder]::Quality, [long]95)
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

Write-Host "Generated cleaned STALKER multichat texture: $targetPath"
