param()
Add-Type -AssemblyName System.Drawing

Write-Output "=== source hash compare ==="
$h1 = (Get-FileHash "D:\GITHUB项目\logo.png" -Algorithm SHA256).Hash
$h2 = (Get-FileHash "D:\GITHUB项目\XY-Music-Mobile\build\icon_work\logo_src.png" -Algorithm SHA256).Hash
Write-Output ("root logo.png    : " + $h1)
Write-Output ("mobile logo_src  : " + $h2)
Write-Output ("same: " + ($h1 -eq $h2))

$src = [System.Drawing.Image]::FromFile("D:\GITHUB项目\logo.png")
Write-Output ("root logo size: " + $src.Width + "x" + $src.Height + " pf=" + $src.PixelFormat)
$src.Dispose()

Write-Output ""
Write-Output "=== measure existing PC icons (radius & content bbox) ==="
function Measure-Icon($path) {
    $bmp = [System.Drawing.Bitmap]::FromFile($path)
    $w = $bmp.Width; $h = $bmp.Height
    Write-Output ("-- " + (Split-Path $path -Leaf) + "  ${w}x${h}")
    $rect = New-Object System.Drawing.Rectangle(0, 0, $w, $h)
    $fmt = [System.Drawing.Imaging.PixelFormat]::Format32bppArgb
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, $fmt)
    $stride = [Math]::Abs($data.Stride)
    $bytes = New-Object byte[] ($stride * $h)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
    $bmp.UnlockBits($data)

    function GetPx($x, $y) {
        $o = $y * $stride + $x * 4
        return @($bytes[$o+2], $bytes[$o+1], $bytes[$o], $bytes[$o+3])  # R,G,B,A
    }

    # rounded corner radius: scan top-left corner along row 0 until alpha drops below 128
    $radiusX = -1
    for ($x = 0; $x -lt [int]($w/2); $x++) {
        $p = GetPx $x 0
        if ($p[3] -ge 128) { $radiusX = $x; break }
    }
    Write-Output ("   corner alpha>=128 starts at x=" + $radiusX + " (radius ~" + $radiusX + "px, " + [Math]::Round($radiusX * 100.0 / $w, 1) + "% of width)")

    # content bbox: pixels with alpha>=128 and luminance < 160 (black strokes)
    $minX = $w; $minY = $h; $maxX = -1; $maxY = -1
    for ($y = 0; $y -lt $h; $y++) {
        for ($x = 0; $x -lt $w; $x++) {
            $p = GetPx $x $y
            if ($p[3] -ge 128 -and ($p[0] + $p[1] + $p[2]) -lt 480) {
                if ($x -lt $minX) { $minX = $x }
                if ($x -gt $maxX) { $maxX = $x }
                if ($y -lt $minY) { $minY = $y }
                if ($y -gt $maxY) { $maxY = $y }
            }
        }
    }
    if ($maxX -ge 0) {
        $cw = $maxX - $minX + 1; $ch = $maxY - $minY + 1
        Write-Output ("   black content bbox: (" + $minX + "," + $minY + ")-(" + $maxX + "," + $maxY + ")  size=" + $cw + "x" + $ch + "  contentW%=" + [Math]::Round($cw*100.0/$w,1) + " contentH%=" + [Math]::Round($ch*100.0/$h,1))
    } else {
        Write-Output "   no black content found"
    }
    $bmp.Dispose()
}

Measure-Icon "D:\GITHUB项目\XY-Music-PC\Assets\icon_preview.png"
Measure-Icon "D:\GITHUB项目\XY-Music-PC\Assets\Square44x44Logo.targetsize-256.png"
Measure-Icon "D:\GITHUB项目\XY-Music-PC\Assets\Square44x44Logo.targetsize-48.png"
Measure-Icon "D:\GITHUB项目\XY-Music-PC\Assets\StoreLogo.scale-200.png"
Measure-Icon "D:\GITHUB项目\XY-Music-PC\Assets\LockScreenLogo.scale-200.png"
Measure-Icon "D:\GITHUB项目\XY-Music-PC\Assets\SplashScreen.scale-200.png"
Measure-Icon "D:\GITHUB项目\XY-Music-PC\Assets\SmallTile.scale-200.png"
