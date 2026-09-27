Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "ERR: window not found"; exit 1 }
$r = $win.Current.BoundingRectangle
Write-Output ("window rect: " + [math]::Round($r.X) + "," + [math]::Round($r.Y) + " " + [math]::Round($r.Width) + "x" + [math]::Round($r.Height))

$bmp = New-Object System.Drawing.Bitmap([int]$r.Width, [int]$r.Height)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen([int]$r.X, [int]$r.Y, 0, 0, $bmp.Size)
$out = "D:\GITHUB项目\XY-Music-PC\Scripts\detail_capture.png"
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose()

# sample luminance in several regions (WCAG relative luminance approx via sRGB luma)
function Get-RegionLum($b, $x0, $y0, $x1, $y1) {
    $sum = 0.0; $n = 0
    for ($x = $x0; $x -lt $x1; $x += 4) {
        for ($y = $y0; $y -lt $y1; $y += 4) {
            $c = $b.GetPixel($x, $y)
            $lum = (0.2126 * $c.R + 0.7152 * $c.G + 0.0722 * $c.B) / 255.0
            $sum += $lum; $n++
        }
    }
    return [math]::Round($sum / [math]::Max($n, 1), 3)
}
$w = $bmp.Width; $h = $bmp.Height
Write-Output ("full size: " + $w + "x" + $h)
Write-Output ("luma top-left area: " + (Get-RegionLum $bmp 0 0 ([int]($w*0.25)) ([int]($h*0.2))))
Write-Output ("luma center: " + (Get-RegionLum $bmp ([int]($w*0.3)) ([int]($h*0.35)) ([int]($w*0.7)) ([int]($h*0.65))))
Write-Output ("luma top-center(title area): " + (Get-RegionLum $bmp ([int]($w*0.3)) ([int]($h*0.08)) ([int]($w*0.7)) ([int]($h*0.2))))
Write-Output ("luma overall: " + (Get-RegionLum $bmp 0 0 $w $h))
$bmp.Dispose()
Write-Output ("saved: " + $out)
