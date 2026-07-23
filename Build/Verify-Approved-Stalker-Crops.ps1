$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

Add-Type -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class ExactCropVerifier
{
    private static Bitmap ToArgb(Bitmap source)
    {
        var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(result);
        graphics.DrawImageUnscaled(source, 0, 0);
        return result;
    }

    public static bool EqualPixels(Bitmap firstSource, Bitmap secondSource)
    {
        if (firstSource.Width != secondSource.Width || firstSource.Height != secondSource.Height)
            return false;

        using var first = ToArgb(firstSource);
        using var second = ToArgb(secondSource);
        var rectangle = new Rectangle(0, 0, first.Width, first.Height);
        var firstData = first.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var secondData = second.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var firstBytes = new byte[Math.Abs(firstData.Stride) * first.Height];
            var secondBytes = new byte[Math.Abs(secondData.Stride) * second.Height];
            Marshal.Copy(firstData.Scan0, firstBytes, 0, firstBytes.Length);
            Marshal.Copy(secondData.Scan0, secondBytes, 0, secondBytes.Length);
            return firstBytes.AsSpan().SequenceEqual(secondBytes);
        }
        finally
        {
            first.UnlockBits(firstData);
            second.UnlockBits(secondData);
        }
    }
}
'@

$patchRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$assetRoot = Join-Path $patchRoot 'Assets\Themes\StalkerApproved'
$manifestPath = Join-Path $patchRoot 'approved-crop-manifest.json'
$manifest = Get-Content $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$sourcePath = Join-Path $assetRoot $manifest.source
$source = [System.Drawing.Bitmap]::new($sourcePath)

try {
    if ($source.Width -ne $manifest.source_size[0] -or $source.Height -ne $manifest.source_size[1]) {
        throw "Approved reference size mismatch: $($source.Width)x$($source.Height)"
    }

    $verified = 0
    foreach ($property in $manifest.assets.PSObject.Properties) {
        $name = $property.Name
        $item = $property.Value
        $path = Join-Path $assetRoot $name
        if (-not (Test-Path $path)) { throw "Missing asset: $name" }

        $hash = (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($hash -ne $item.sha256) { throw "SHA-256 mismatch: $name" }

        $asset = [System.Drawing.Bitmap]::new($path)
        try {
            $box = $item.crop_box
            $rectangle = [System.Drawing.Rectangle]::new(
                [int]$box[0], [int]$box[1],
                [int]($box[2] - $box[0]), [int]($box[3] - $box[1]))
            $expected = $source.Clone($rectangle, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            try {
                if (-not [ExactCropVerifier]::EqualPixels($expected, $asset)) {
                    throw "Asset is not an exact pixel crop: $name"
                }
            }
            finally { $expected.Dispose() }
        }
        finally { $asset.Dispose() }

        $verified++
        Write-Host "OK  $name"
    }

    Write-Host "Verified $verified exact crops from the approved 1672x941 reference." -ForegroundColor Green
}
finally {
    $source.Dispose()
}
