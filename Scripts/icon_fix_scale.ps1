param()
Add-Type -AssemblyName System.Drawing

$srcPath = "D:\GITHUB项目\logo.png"
$assetsDir = "D:\GITHUB项目\XY-Music-PC\Assets"
$cx = 166.0; $cy = 166.0; $cw = 692.0; $ch = 692.0
$src = [System.Drawing.Image]::FromFile($srcPath)

function New-Graphics($bmp) {
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    return $g
}

function Save-Png($bmp, $name) {
    $path = Join-Path $assetsDir $name
    if (-not (Test-Path "$path.bak")) { Copy-Item $path "$path.bak" -Force }
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Output ("saved: " + $name + " (" + $bmp.Width + "x" + $bmp.Height + ")")
}

function New-PlainIcon([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = New-Graphics $bmp
    $g.Clear([System.Drawing.Color]::White)
    $d = [double]$size * 0.80
    $off = ([double]$size - $d) / 2.0
    $g.DrawImage($script:src,
        (New-Object System.Drawing.RectangleF([float]$off, [float]$off, [float]$d, [float]$d)),
        (New-Object System.Drawing.RectangleF([float]$cx, [float]$cy, [float]$cw, [float]$ch)),
        [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose()
    return $bmp
}

function New-WideIcon([int]$w, [int]$h) {
    $bmp = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = New-Graphics $bmp
    $g.Clear([System.Drawing.Color]::White)
    $d = [double]$h * 0.40
    $offX = ([double]$w - $d) / 2.0
    $offY = ([double]$h - $d) / 2.0
    $g.DrawImage($script:src,
        (New-Object System.Drawing.RectangleF([float]$offX, [float]$offY, [float]$d, [float]$d)),
        (New-Object System.Drawing.RectangleF([float]$cx, [float]$cy, [float]$cw, [float]$ch)),
        [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose()
    return $bmp
}

# square scale sets: filename -> pixel size
$squareSets = @{
    "Square44x44Logo"   = @{ 100 = 44;   125 = 55;   150 = 66;   200 = 88;   400 = 176 }
    "Square150x150Logo" = @{ 100 = 150;  125 = 188;  150 = 225;  200 = 300;  400 = 600 }
    "StoreLogo"         = @{ 100 = 50;   125 = 63;   150 = 75;   200 = 100;  400 = 200 }
    "SmallTile"         = @{ 100 = 71;   125 = 89;   150 = 107;  200 = 142;  400 = 284 }
    "LargeTile"         = @{ 100 = 310;  125 = 388;  150 = 465;  200 = 620;  400 = 1240 }
}
foreach ($base in $squareSets.Keys) {
    $map = $squareSets[$base]
    foreach ($scale in @($map.Keys | Sort-Object)) {
        $bmp = New-PlainIcon $map[$scale]
        Save-Png $bmp ($base + ".scale-" + $scale + ".png")
        $bmp.Dispose()
    }
}

# wide sets: filename -> (w, h)
$wideSets = @{
    "Wide310x150Logo" = @{ 100 = @(310,150);   125 = @(388,188);   150 = @(465,225);   200 = @(620,300);   400 = @(1240,600) }
    "SplashScreen"    = @{ 100 = @(620,300);   125 = @(775,375);   150 = @(930,450);   200 = @(1240,600);  400 = @(2480,1200) }
}
foreach ($base in $wideSets.Keys) {
    $map = $wideSets[$base]
    foreach ($scale in @($map.Keys | Sort-Object)) {
        $wh = $map[$scale]
        $bmp = New-WideIcon $wh[0] $wh[1]
        Save-Png $bmp ($base + ".scale-" + $scale + ".png")
        $bmp.Dispose()
    }
}

$src.Dispose()
Write-Output "SCALE SETS DONE"
