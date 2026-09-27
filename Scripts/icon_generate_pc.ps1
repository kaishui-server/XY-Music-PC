param([switch]$KeepBackup)
Add-Type -AssemblyName System.Drawing

$srcPath = "D:\GITHUB项目\logo.png"
$assetsDir = "D:\GITHUB项目\XY-Music-PC\Assets"

# logo artwork bbox measured by pixel scan (same as mobile icon session)
$cx = 166.0; $cy = 166.0; $cw = 692.0; $ch = 692.0

# squircle corner radius ratio of existing icon_preview.png (56px on 256)
$radiusRatio = 56.0 / 256.0

$src = [System.Drawing.Image]::FromFile($srcPath)
Write-Output ("source: " + $src.Width + "x" + $src.Height)

function New-Graphics($bmp) {
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    return $g
}

function Save-Png($bmp, $name) {
    $path = Join-Path $assetsDir $name
    if ($KeepBackup -and (Test-Path $path)) {
        Copy-Item $path "$path.bak" -Force
    }
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Output ("saved: " + $name + " (" + $bmp.Width + "x" + $bmp.Height + ")")
}

# draw logo artwork bbox into a centered square of side $size*$ratio
function Draw-Content($g, [int]$size, [double]$ratio) {
    $d = [double]$size * $ratio
    $off = ([double]$size - $d) / 2.0
    $g.DrawImage($script:src,
        (New-Object System.Drawing.RectangleF([float]$off, [float]$off, [float]$d, [float]$d)),
        (New-Object System.Drawing.RectangleF([float]$cx, [float]$cy, [float]$cw, [float]$ch)),
        [System.Drawing.GraphicsUnit]::Pixel)
}

# white squircle (rounded rect, transparent outside) + logo strokes
function New-SquircleIcon([int]$size, [double]$ratio) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = New-Graphics $bmp
    $r = [double]$size * $radiusRatio
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, 2*$r, 2*$r, 180, 90)
    $path.AddArc($size - 2*$r, 0, 2*$r, 2*$r, 270, 90)
    $path.AddArc($size - 2*$r, $size - 2*$r, 2*$r, 2*$r, 0, 90)
    $path.AddArc(0, $size - 2*$r, 2*$r, 2*$r, 90, 90)
    $path.CloseFigure()
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $g.FillPath($white, $path)
    # clip content drawing inside rounded rect so strokes never cross the corner arc
    $g.SetClip($path)
    Draw-Content $g $size $ratio
    $g.ResetClip()
    $path.Dispose(); $white.Dispose(); $g.Dispose()
    return $bmp
}

# full square white icon (no transparent corners)
function New-PlainIcon([int]$size, [double]$ratio) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = New-Graphics $bmp
    $g.Clear([System.Drawing.Color]::White)
    Draw-Content $g $size $ratio
    $g.Dispose()
    return $bmp
}

# wide canvas: white bg + centered logo square of side = height * ratio
function New-WideIcon([int]$w, [int]$h, [double]$ratio) {
    $bmp = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = New-Graphics $bmp
    $g.Clear([System.Drawing.Color]::White)
    $d = [double]$h * $ratio
    $offX = ([double]$w - $d) / 2.0
    $offY = ([double]$h - $d) / 2.0
    $g.DrawImage($script:src,
        (New-Object System.Drawing.RectangleF([float]$offX, [float]$offY, [float]$d, [float]$d)),
        (New-Object System.Drawing.RectangleF([float]$cx, [float]$cy, [float]$cw, [float]$ch)),
        [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose()
    return $bmp
}

# ---------- main app icon: squircle style ----------
Write-Output "--- main squircle icon set ---"
$preview = New-SquircleIcon 256 0.80
Save-Png $preview "icon_preview.png"
$icoSizes = @(16, 32, 48, 64, 128, 256)
$icoImages = @{}
foreach ($s in $icoSizes) {
    $icoImages[$s] = New-SquircleIcon $s 0.80
    Write-Output ("built ico frame: " + $s + "x" + $s)
}

# ---------- packaged square icons: plain white style ----------
Write-Output "--- packaged square icons ---"
$squareSets = @(
    @{ base = "Square44x44Logo.scale-";     sizes = @(44, 55, 66, 88, 176) },
    @{ base = "Square150x150Logo.scale-";   sizes = @(150, 188, 225, 300, 600) },
    @{ base = "StoreLogo.scale-";           sizes = @(50, 63, 75, 100, 200) },
    @{ base = "SmallTile.scale-";           sizes = @(71, 89, 107, 142, 284) },
    @{ base = "LargeTile.scale-";           sizes = @(310, 388, 465, 620, 1240) }
)
foreach ($set in $squareSets) {
    foreach ($s in $set.sizes) {
        $bmp = New-PlainIcon $s 0.80
        Save-Png $bmp ($set.base + $s + ".png")
        $bmp.Dispose()
    }
}

Write-Output "--- targetsize / altform square icons ---"
$plain44 = @(
    "Square44x44Logo.targetsize-16", "Square44x44Logo.targetsize-24",
    "Square44x44Logo.targetsize-32", "Square44x44Logo.targetsize-48",
    "Square44x44Logo.targetsize-256",
    "Square44x44Logo.targetsize-24_altform-unplated",
    "Square44x44Logo.altform-unplated_targetsize-16",
    "Square44x44Logo.altform-unplated_targetsize-24",
    "Square44x44Logo.altform-unplated_targetsize-32",
    "Square44x44Logo.altform-unplated_targetsize-48",
    "Square44x44Logo.altform-unplated_targetsize-256",
    "Square44x44Logo.altform-lightunplated_targetsize-16",
    "Square44x44Logo.altform-lightunplated_targetsize-24",
    "Square44x44Logo.altform-lightunplated_targetsize-32",
    "Square44x44Logo.altform-lightunplated_targetsize-48",
    "Square44x44Logo.altform-lightunplated_targetsize-256"
)
foreach ($name in $plain44) {
    $s = [int][System.Text.RegularExpressions.Regex]::Match($name, "targetsize-(\d+)").Groups[1].Value
    $bmp = New-PlainIcon $s 0.80
    Save-Png $bmp ($name + ".png")
    $bmp.Dispose()
}

Write-Output "--- wide + splash + lockscreen ---"
$wideSets = @(
    @{ base = "Wide310x150Logo.scale-"; sizes = @(@(310,150), @(388,188), @(465,225), @(620,300), @(1240,600)) },
    @{ base = "SplashScreen.scale-";     sizes = @(@(620,300), @(775,375), @(930,450), @(1240,600), @(2480,1200)) }
)
foreach ($set in $wideSets) {
    foreach ($wh in $set.sizes) {
        $bmp = New-WideIcon $wh[0] $wh[1] 0.40
        Save-Png $bmp ($set.base + ($wh[0]) + ".png")
        $bmp.Dispose()
    }
}
$lock = New-PlainIcon 48 0.80
Save-Png $lock "LockScreenLogo.scale-200.png"
$lock.Dispose()

# ---------- build icon.ico (PNG-in-ICO, same structure as before) ----------
Write-Output "--- assembling icon.ico ---"
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)
$bw.Write([UInt16]0)   # reserved
$bw.Write([UInt16]1)   # type icon
$bw.Write([UInt16]$icoSizes.Count)
$offset = 6 + 16 * $icoSizes.Count
$pngs = New-Object System.Collections.Generic.List[byte[]]
foreach ($s in $icoSizes) {
    $pms = New-Object System.IO.MemoryStream
    $icoImages[$s].Save($pms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs.Add($pms.ToArray())
    $pms.Dispose()
}
for ($i = 0; $i -lt $icoSizes.Count; $i++) {
    $s = $icoSizes[$i]
    $data = $pngs[$i]
    $wbyte = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$wbyte)
    $bw.Write([byte]$wbyte)
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]$data.Length)
    $bw.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($data in $pngs) { $bw.Write($data) }
$bw.Flush()
$icoPath = Join-Path $assetsDir "icon.ico"
if ($KeepBackup -and (Test-Path $icoPath)) { Copy-Item $icoPath "$icoPath.bak" -Force }
[System.IO.File]::WriteAllBytes($icoPath, $ms.ToArray())
$bw.Dispose()
Write-Output ("icon.ico written: " + (Get-Item $icoPath).Length + " bytes (" + $icoSizes.Count + " frames)")

foreach ($s in $icoSizes) { $icoImages[$s].Dispose() }
$preview.Dispose()
$src.Dispose()
Write-Output "ALL DONE"
