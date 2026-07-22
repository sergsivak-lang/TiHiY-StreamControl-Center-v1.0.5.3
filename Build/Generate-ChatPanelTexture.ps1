param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectDir
)

$ErrorActionPreference = 'Stop'

$sourceDirectory = Join-Path $ProjectDir 'Assets\Themes\StalkerApproved'
$targetPath = Join-Path $sourceDirectory 'chat-multichat-panel-exact.jpg'
$partPattern = Join-Path $sourceDirectory 'chat-multichat-panel-exact.b64.*'
$expectedSha256 = 'e9e7597a28d1830e1dc0e813e4069d6719b92d771f8889d20aea33f366cdf60d'

$parts = Get-ChildItem -LiteralPath $sourceDirectory -Filter 'chat-multichat-panel-exact.b64.*' |
    Where-Object { $_.Name -match '\.b64\.\d{3}$' } |
    Sort-Object Name

if ($parts.Count -ne 4) {
    throw "Expected 4 multichat texture chunks, found $($parts.Count) in $sourceDirectory"
}

$builder = [Text.StringBuilder]::new()
foreach ($part in $parts) {
    $chunk = (Get-Content -Raw -LiteralPath $part.FullName) -replace '\s', ''
    if ([string]::IsNullOrWhiteSpace($chunk)) {
        throw "Multichat texture chunk is empty: $($part.FullName)"
    }
    [void]$builder.Append($chunk)
}

$base64 = $builder.ToString()
if (($base64.Length % 4) -ne 0) {
    throw "Combined multichat Base64 length is invalid: $($base64.Length)"
}

$bytes = [Convert]::FromBase64String($base64)
[IO.File]::WriteAllBytes($targetPath, $bytes)

if (-not (Test-Path $targetPath) -or (Get-Item $targetPath).Length -ne 9951) {
    throw "Generated multichat texture has invalid size: $targetPath"
}

$actualSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $targetPath).Hash.ToLowerInvariant()
if ($actualSha256 -ne $expectedSha256) {
    throw "Generated multichat texture hash mismatch. Expected $expectedSha256, got $actualSha256"
}

Write-Host "Generated and verified STALKER multichat texture: $targetPath ($($bytes.Length) bytes)"
