param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectDir
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$themeRoot = Join-Path $ProjectDir 'Assets\Themes'
$exactRoot = Join-Path $themeRoot 'StalkerExact'
New-Item -ItemType Directory -Force -Path $themeRoot, $exactRoot | Out-Null

function New-ArgbColor([int]$a, [int]$r, [int]$g, [int]$b) {
    return [System.Drawing.Color]::FromArgb($a, $r, $g, $b)
}

function Save-Png([System.Drawing.Bitmap]$bitmap, [string]$path) {
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
}

function New-MetalTexture([string]$path) {
    $width = 512; $height = 512
    $bitmap = [System.Drawing.Bitmap]::new($width, $height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $random = [System.Random]::new(1948)
    for ($y = 0; $y -lt $height; $y++) {
        for ($x = 0; $x -lt $width; $x++) {
            $noise = $random.Next(-13, 14)
            $wave = [int](5 * [Math]::Sin(($x + $y) / 31.0))
            $r = [Math]::Clamp(29 + $noise + $wave, 0, 255)
            $g = [Math]::Clamp(29 + [int]($noise * 0.55) + $wave, 0, 255)
            $b = [Math]::Clamp(22 + [int]($noise * 0.35), 0, 255)
            $bitmap.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(255, $r, $g, $b))
        }
    }

    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        for ($index = 0; $index -lt 95; $index++) {
            $x = $random.Next(-40, $width); $y = $random.Next(-30, $height)
            $rw = $random.Next(18, 105); $rh = $random.Next(10, 62)
            $rust = if (($index % 3) -eq 0) { New-ArgbColor 56 156 79 26 } elseif (($index % 3) -eq 1) { New-ArgbColor 42 105 53 20 } else { New-ArgbColor 32 190 100 34 }
            $brush = [System.Drawing.SolidBrush]::new($rust)
            try { $graphics.FillEllipse($brush, $x, $y, $rw, $rh) } finally { $brush.Dispose() }
        }
        for ($index = 0; $index -lt 70; $index++) {
            $x1 = $random.Next(0, $width); $y1 = $random.Next(0, $height)
            $x2 = [Math]::Clamp($x1 + $random.Next(-130, 131), 0, $width - 1)
            $y2 = [Math]::Clamp($y1 + $random.Next(-30, 31), 0, $height - 1)
            $pen = [System.Drawing.Pen]::new((New-ArgbColor ($random.Next(28, 82)) 8 7 5), $(if (($index % 8) -eq 0) { 2 } else { 1 }))
            try { $graphics.DrawLine($pen, $x1, $y1, $x2, $y2) } finally { $pen.Dispose() }
        }
        for ($x = 64; $x -lt $width; $x += 128) {
            for ($y = 64; $y -lt $height; $y += 128) {
                $graphics.FillEllipse([System.Drawing.Brushes]::Black, $x - 4, $y - 4, 8, 8)
                $pen = [System.Drawing.Pen]::new((New-ArgbColor 155 113 82 43), 1)
                try { $graphics.DrawEllipse($pen, $x - 5, $y - 5, 10, 10) } finally { $pen.Dispose() }
            }
        }
    }
    finally { $graphics.Dispose() }
    try { Save-Png $bitmap $path } finally { $bitmap.Dispose() }
}

function New-Scratches([string]$path) {
    $bitmap = [System.Drawing.Bitmap]::new(512, 512, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $random = [System.Random]::new(2012)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        for ($index = 0; $index -lt 260; $index++) {
            $x = $random.Next(0, 512); $y = $random.Next(0, 512)
            $length = $random.Next(8, 96)
            $x2 = [Math]::Clamp($x + $length, 0, 511)
            $y2 = [Math]::Clamp($y + $random.Next(-8, 9), 0, 511)
            $bright = ($index % 4) -eq 0
            $color = if ($bright) { New-ArgbColor ($random.Next(20, 58)) 232 219 180 } else { New-ArgbColor ($random.Next(18, 72)) 18 13 7 }
            $pen = [System.Drawing.Pen]::new($color, $(if (($index % 11) -eq 0) { 2 } else { 1 }))
            try { $graphics.DrawLine($pen, $x, $y, $x2, $y2) } finally { $pen.Dispose() }
        }
    }
    finally { $graphics.Dispose() }
    try { Save-Png $bitmap $path } finally { $bitmap.Dispose() }
}

function New-HazardCorner([string]$path) {
    $bitmap = [System.Drawing.Bitmap]::new(256, 256, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $plate = [System.Drawing.SolidBrush]::new((New-ArgbColor 224 34 31 23))
        try {
            $graphics.FillPolygon($plate, [System.Drawing.Point[]]@(
                [System.Drawing.Point]::new(0,0), [System.Drawing.Point]::new(256,0),
                [System.Drawing.Point]::new(256,46), [System.Drawing.Point]::new(46,46),
                [System.Drawing.Point]::new(46,256), [System.Drawing.Point]::new(0,256)))
        } finally { $plate.Dispose() }
        $yellow = [System.Drawing.SolidBrush]::new((New-ArgbColor 215 205 139 35))
        try {
            for ($i = -256; $i -lt 512; $i += 58) {
                $graphics.FillPolygon($yellow, [System.Drawing.Point[]]@(
                    [System.Drawing.Point]::new($i,0), [System.Drawing.Point]::new($i+28,0),
                    [System.Drawing.Point]::new($i+74,46), [System.Drawing.Point]::new($i+46,46)))
                $graphics.FillPolygon($yellow, [System.Drawing.Point[]]@(
                    [System.Drawing.Point]::new(0,$i), [System.Drawing.Point]::new(0,$i+28),
                    [System.Drawing.Point]::new(46,$i+74), [System.Drawing.Point]::new(46,$i+46)))
            }
        } finally { $yellow.Dispose() }
        $edge = [System.Drawing.Pen]::new((New-ArgbColor 230 150 91 29), 3)
        try { $graphics.DrawLines($edge, [System.Drawing.Point[]]@([System.Drawing.Point]::new(0,46),[System.Drawing.Point]::new(46,46),[System.Drawing.Point]::new(46,256))) } finally { $edge.Dispose() }
    }
    finally { $graphics.Dispose() }
    try { Save-Png $bitmap $path } finally { $bitmap.Dispose() }
}

function New-StalkerSymbol([string]$path) {
    $bitmap = [System.Drawing.Bitmap]::new(320, 360, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $shield = [System.Drawing.Drawing2D.GraphicsPath]::new()
        $shield.AddPolygon([System.Drawing.Point[]]@(
            [System.Drawing.Point]::new(160,12), [System.Drawing.Point]::new(294,58),
            [System.Drawing.Point]::new(274,260), [System.Drawing.Point]::new(160,344),
            [System.Drawing.Point]::new(46,260), [System.Drawing.Point]::new(26,58)))
        $shieldBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new([System.Drawing.Point]::new(0,0), [System.Drawing.Point]::new(320,360), (New-ArgbColor 245 43 42 31), (New-ArgbColor 245 10 11 8))
        $outline = [System.Drawing.Pen]::new((New-ArgbColor 245 190 125 38), 8)
        try { $graphics.FillPath($shieldBrush, $shield); $graphics.DrawPath($outline, $shield) } finally { $outline.Dispose(); $shieldBrush.Dispose(); $shield.Dispose() }

        $hood = [System.Drawing.SolidBrush]::new((New-ArgbColor 255 30 31 22))
        $mask = [System.Drawing.SolidBrush]::new((New-ArgbColor 255 70 67 49))
        $green = [System.Drawing.SolidBrush]::new((New-ArgbColor 255 109 205 65))
        try {
            $graphics.FillPolygon($hood, [System.Drawing.Point[]]@([System.Drawing.Point]::new(160,66),[System.Drawing.Point]::new(72,215),[System.Drawing.Point]::new(248,215)))
            $graphics.FillEllipse($mask, 92,112,136,128)
            $graphics.FillEllipse($green, 109,143,40,30)
            $graphics.FillEllipse($green, 171,143,40,30)
            $graphics.FillRectangle($mask, 126,198,68,72)
            $graphics.FillEllipse($green, 132,216,56,40)
        } finally { $green.Dispose(); $mask.Dispose(); $hood.Dispose() }
        $radiation = [System.Drawing.Pen]::new((New-ArgbColor 235 213 153 46), 5)
        try {
            $graphics.DrawEllipse($radiation, 126,270,68,68)
            for ($i = 0; $i -lt 3; $i++) {
                $angle = (-90 + $i * 120) * [Math]::PI / 180
                $x = 160 + [int](30 * [Math]::Cos($angle)); $y = 304 + [int](30 * [Math]::Sin($angle))
                $graphics.DrawLine($radiation, 160,304,$x,$y)
            }
        } finally { $radiation.Dispose() }
    }
    finally { $graphics.Dispose() }
    try { Save-Png $bitmap $path } finally { $bitmap.Dispose() }
}

function New-ZoneBanner([string]$path, [int]$width, [int]$height) {
    $bitmap = [System.Drawing.Bitmap]::new($width, $height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $random = [System.Random]::new(1986)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $background = [System.Drawing.Drawing2D.LinearGradientBrush]::new([System.Drawing.Point]::new(0,0), [System.Drawing.Point]::new(0,$height), (New-ArgbColor 255 37 39 31), (New-ArgbColor 255 5 7 5))
        try { $graphics.FillRectangle($background, 0,0,$width,$height) } finally { $background.Dispose() }

        $fog = [System.Drawing.SolidBrush]::new((New-ArgbColor 35 155 149 119))
        try {
            for ($i = 0; $i -lt 18; $i++) {
                $x = $random.Next(-100,$width); $y = $random.Next([int]($height*0.35),$height)
                $graphics.FillEllipse($fog,$x,$y,$random.Next(180,420),$random.Next(35,100))
            }
        } finally { $fog.Dispose() }

        $silhouette = [System.Drawing.SolidBrush]::new((New-ArgbColor 235 7 9 7))
        try {
            $ground = [System.Drawing.Point[]]@([System.Drawing.Point]::new(0,$height),[System.Drawing.Point]::new(0,[int]($height*0.72)),[System.Drawing.Point]::new([int]($width*0.18),[int]($height*0.61)),[System.Drawing.Point]::new([int]($width*0.42),[int]($height*0.76)),[System.Drawing.Point]::new([int]($width*0.66),[int]($height*0.63)),[System.Drawing.Point]::new($width,[int]($height*0.73)),[System.Drawing.Point]::new($width,$height))
            $graphics.FillPolygon($silhouette,$ground)
            for ($i=0;$i -lt 24;$i++) {
                $x=$random.Next(0,$width); $trunk=$random.Next(35,[Math]::Max(36,[int]($height*0.45)))
                $graphics.FillRectangle($silhouette,$x,[int]($height*0.72)-$trunk,3,$trunk)
                $branchPen=[System.Drawing.Pen]::new((New-ArgbColor 220 5 7 5),2)
                try { $graphics.DrawLine($branchPen,$x,[int]($height*0.72)-[int]($trunk*0.55),$x+$random.Next(-25,26),[int]($height*0.72)-[int]($trunk*0.72)) } finally { $branchPen.Dispose() }
            }
            $graphics.FillRectangle($silhouette,[int]($width*0.77),[int]($height*0.27),[int]($width*0.12),[int]($height*0.45))
            $graphics.FillRectangle($silhouette,[int]($width*0.805),[int]($height*0.13),[int]($width*0.05),[int]($height*0.18))
        } finally { $silhouette.Dispose() }

        $glowPath = [System.Drawing.Drawing2D.GraphicsPath]::new()
        $glowPath.AddEllipse([int]($width*0.39),[int]($height*0.37),[int]($width*0.22),[int]($height*0.40))
        $glow = [System.Drawing.Drawing2D.PathGradientBrush]::new($glowPath)
        try {
            $glow.CenterColor = New-ArgbColor 210 105 236 50
            $glow.SurroundColors = [System.Drawing.Color[]]@((New-ArgbColor 0 60 100 30))
            $graphics.FillPath($glow,$glowPath)
        } finally { $glow.Dispose(); $glowPath.Dispose() }

        $fontSize = [Math]::Max(18,[int]($height*0.105))
        $font = [System.Drawing.Font]::new('Impact',$fontSize,[System.Drawing.FontStyle]::Bold,[System.Drawing.GraphicsUnit]::Pixel)
        $small = [System.Drawing.Font]::new('Consolas',[Math]::Max(11,[int]($height*0.045)),[System.Drawing.FontStyle]::Bold,[System.Drawing.GraphicsUnit]::Pixel)
        $shadow = [System.Drawing.SolidBrush]::new((New-ArgbColor 210 0 0 0))
        $textBrush = [System.Drawing.SolidBrush]::new((New-ArgbColor 245 215 205 177))
        $greenText = [System.Drawing.SolidBrush]::new((New-ArgbColor 245 127 211 73))
        try {
            $title='ZONE DOES NOT FORGIVE'
            $subtitle='STALKER  •  STAY ALERT'
            $titleSize=$graphics.MeasureString($title,$font); $subSize=$graphics.MeasureString($subtitle,$small)
            $tx=($width-$titleSize.Width)/2; $ty=$height-$titleSize.Height-$subSize.Height-18
            $graphics.DrawString($title,$font,$shadow,$tx+3,$ty+3)
            $graphics.DrawString($title,$font,$textBrush,$tx,$ty)
            $graphics.DrawString($subtitle,$small,$greenText,($width-$subSize.Width)/2,$ty+$titleSize.Height-4)
        } finally { $greenText.Dispose(); $textBrush.Dispose(); $shadow.Dispose(); $small.Dispose(); $font.Dispose() }
    }
    finally { $graphics.Dispose() }
    try { Save-Png $bitmap $path } finally { $bitmap.Dispose() }
}

New-MetalTexture (Join-Path $exactRoot 'rusted-metal.png')
New-Scratches (Join-Path $exactRoot 'scratches.png')
New-HazardCorner (Join-Path $exactRoot 'hazard-corner.png')
New-StalkerSymbol (Join-Path $exactRoot 'stalker-symbol.png')
New-ZoneBanner (Join-Path $exactRoot 'zone-banner.png') 1000 460
New-ZoneBanner (Join-Path $exactRoot 'zone-header.png') 1200 220
New-ZoneBanner (Join-Path $themeRoot 'stalker_zone.png') 1000 640
Copy-Item (Join-Path $exactRoot 'stalker-symbol.png') (Join-Path $themeRoot 'stalker_symbol.png') -Force

Write-Host 'STALKER theme assets generated.'
