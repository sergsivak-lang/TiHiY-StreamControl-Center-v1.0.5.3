param([Parameter(Mandatory=$true)][string]$ProjectDir)

Add-Type -AssemblyName System.Drawing
$outDir = Join-Path $ProjectDir 'Lite\Assets'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$png = Join-Path $outDir 'TiHiYMiniLite.png'
$ico = Join-Path $outDir 'TiHiYMiniLite.ico'

$size = 256
$bmp = New-Object System.Drawing.Bitmap $size,$size,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::Transparent)

$path = New-Object System.Drawing.Drawing2D.GraphicsPath
$pts = @(
  (New-Object System.Drawing.PointF 128,10),(New-Object System.Drawing.PointF 222,49),(New-Object System.Drawing.PointF 244,139),(New-Object System.Drawing.PointF 194,222),
  (New-Object System.Drawing.PointF 128,246),(New-Object System.Drawing.PointF 62,222),(New-Object System.Drawing.PointF 12,139),(New-Object System.Drawing.PointF 34,49)
)
$path.AddPolygon($pts)
$bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.Point 20,20),(New-Object System.Drawing.Point 236,236),[System.Drawing.Color]::FromArgb(255,4,25,45),[System.Drawing.Color]::FromArgb(255,8,58,91))
$g.FillPath($bg,$path)
$outline = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255,255,211,41),8)
$g.DrawPath($outline,$path)

$inner = New-Object System.Drawing.Drawing2D.GraphicsPath
$innerPts = @(
  (New-Object System.Drawing.PointF 128,26),(New-Object System.Drawing.PointF 210,60),(New-Object System.Drawing.PointF 228,137),(New-Object System.Drawing.PointF 184,208),
  (New-Object System.Drawing.PointF 128,229),(New-Object System.Drawing.PointF 72,208),(New-Object System.Drawing.PointF 28,137),(New-Object System.Drawing.PointF 46,60)
)
$inner.AddPolygon($innerPts)
$cyanPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(215,31,185,255),3)
$g.DrawPath($cyanPen,$inner)

$yellow = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255,255,211,41))
$cyan = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255,67,205,255))
$g.FillRectangle($yellow,75,72,91,20)
$g.FillRectangle($yellow,111,72,20,108)
$g.FillPolygon($yellow,@((New-Object System.Drawing.Point 96,174),(New-Object System.Drawing.Point 146,174),(New-Object System.Drawing.Point 121,202)))

$arcPen1 = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255,67,205,255),10)
$arcPen1.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$arcPen1.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$g.DrawArc($arcPen1,137,101,52,52,-65,130)
$arcPen2 = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(210,67,205,255),9)
$arcPen2.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$arcPen2.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$g.DrawArc($arcPen2,137,82,91,91,-65,130)
$g.FillEllipse($cyan,143,123,16,16)

$bmp.Save($png,[System.Drawing.Imaging.ImageFormat]::Png)
$hIcon = $bmp.GetHicon()
$icon = [System.Drawing.Icon]::FromHandle($hIcon)
$fs = [System.IO.File]::Create($ico)
$icon.Save($fs)
$fs.Close()

$icon.Dispose(); $bmp.Dispose(); $g.Dispose(); $bg.Dispose(); $outline.Dispose(); $cyanPen.Dispose(); $yellow.Dispose(); $cyan.Dispose(); $arcPen1.Dispose(); $arcPen2.Dispose(); $path.Dispose(); $inner.Dispose()
