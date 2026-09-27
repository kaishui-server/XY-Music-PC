Add-Type -AssemblyName System.Drawing

$dir = "D:\GITHUB项目\XY-Music-PC\Assets"
$files = @(
    "icon_preview.png",
    "Square44x44Logo.scale-100.png",
    "Square44x44Logo.scale-200.png",
    "Square44x44Logo.targetsize-16.png",
    "Square44x44Logo.targetsize-48.png",
    "Square44x44Logo.targetsize-256.png",
    "Square44x44Logo.altform-unplated_targetsize-256.png",
    "Square44x44Logo.altform-lightunplated_targetsize-256.png",
    "Square44x44Logo.targetsize-24_altform-unplated.png",
    "Square150x150Logo.scale-100.png",
    "Square150x150Logo.scale-200.png",
    "StoreLogo.scale-100.png",
    "StoreLogo.scale-200.png",
    "LargeTile.scale-100.png",
    "LargeTile.scale-200.png",
    "SmallTile.scale-100.png",
    "SmallTile.scale-200.png",
    "Wide310x150Logo.scale-100.png",
    "Wide310x150Logo.scale-200.png",
    "SplashScreen.scale-100.png",
    "SplashScreen.scale-200.png",
    "LockScreenLogo.scale-200.png"
)

foreach ($name in $files) {
    $path = Join-Path $dir $name
    if (-not (Test-Path $path)) { Write-Output ("MISSING: " + $name); continue }
    $bmp = [System.Drawing.Bitmap]::FromFile($path)
    $w = $bmp.Width; $h = $bmp.Height
    $rect = New-Object System.Drawing.Rectangle(0, 0, $w, $h)
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stride = [Math]::Abs($data.Stride)
    $bytes = New-Object byte[] ($stride * $h)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
    $bmp.UnlockBits($data)

    function GetPx([int]$x, [int]$y) {
        $o = $y * $script:stride + $x * 4
        return @($script:bytes[$o+2], $script:bytes[$o+1], $script:bytes[$o], $script:bytes[$o+3])
    }

    # corner radius: first opaque pixel in row 0
    $radius = -1
    for ($x = 0; $x -lt [int]($w/2); $x++) {
        if ((GetPx $x 0)[3] -ge 128) { $radius = $x; break }
    }

    # corner radius from both row 0 and column 0 (avg)
    $radiusY = -1
    for ($y = 0; $y -lt [int]($h/2); $y++) {
        if ((GetPx 0 $y)[3] -ge 128) { $radiusY = $y; break }
    }

    # background style: sample interior center + quarter points
    $c = GetPx ([int]($w/2)) ([int]($h/2))
    $bgStyle = "centerA=" + $c[3] + " RGB=" + $c[0] + "," + $c[1] + "," + $c[2]

    # corner area style: pixel at (2,2)
    $corner = GetPx 2 2
    $cornerStyle = "cornerA=" + $corner[3]

    # content bbox: fully-opaque DARK pixels only (alpha>=200, luminance<128)
    $minX = $w; $minY = $h; $maxX = -1; $maxY = -1
    $edgeMargin = [int]([Math]::Max(1, $w * 0.03))
    for ($y = 0; $y -lt $h; $y++) {
        for ($x = 0; $x -lt $w; $x++) {
            $p = GetPx $x $y
            if ($p[3] -ge 200 -and ($p[0] + $p[1] + $p[2]) -lt 384) {
                if ($x -lt $minX) { $minX = $x }
                if ($x -gt $maxX) { $maxX = $x }
                if ($y -lt $minY) { $minY = $y }
                if ($y -gt $maxY) { $maxY = $y }
            }
        }
    }
    $bbox = "none"
    $occ = ""
    if ($maxX -ge 0) {
        $cw = $maxX - $minX + 1; $ch = $maxY - $minY + 1
        $bbox = "(" + $minX + "," + $minY + ")-(" + $maxX + "," + $maxY + ") " + $cw + "x" + $ch
        $occ = " occW=" + [Math]::Round($cw*100.0/$w,1) + "% occH=" + [Math]::Round($ch*100.0/$h,1) + "%"
        $cx0 = [Math]::Round(($minX + $maxX) / 2.0, 1); $cy0 = [Math]::Round(($minY + $maxY) / 2.0, 1)
        $occ += " center=(" + $cx0 + "," + $cy0 + ") canvasCenter=(" + ($w/2.0) + "," + ($h/2.0) + ")"
    }

    Write-Output ($name + " | " + $w + "x" + $h + " | radius=" + $radius + "," + $radiusY + " | " + $bgStyle + " | " + $cornerStyle + " | bbox=" + $bbox + $occ)
    $bmp.Dispose()
}
