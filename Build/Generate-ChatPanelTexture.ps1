param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectDir
)

$ErrorActionPreference = 'Stop'

$sourcePath = Join-Path $ProjectDir 'Assets\Themes\StalkerApproved\chat-multichat-panel-exact.b64'
$targetPath = Join-Path $ProjectDir 'Assets\Themes\StalkerApproved\chat-multichat-panel-exact.jpg'

if (-not (Test-Path $sourcePath)) {
    throw "Multichat texture source not found: $sourcePath"
}

$base64 = (Get-Content -Raw -LiteralPath $sourcePath) -replace '\s', ''
if ([string]::IsNullOrWhiteSpace($base64)) {
    throw "Multichat texture source is empty: $sourcePath"
}

$bytes = [Convert]::FromBase64String($base64)
[IO.File]::WriteAllBytes($targetPath, $bytes)

if (-not (Test-Path $targetPath) -or (Get-Item $targetPath).Length -lt 4096) {
    throw "Generated multichat texture is invalid: $targetPath"
}

Write-Host "Generated STALKER multichat texture: $targetPath ($($bytes.Length) bytes)"
