Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "ERR: main window not found"; exit 1 }

$slider = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "ProgressSlider")))
if (-not $slider) { Write-Output "ERR: ProgressSlider not found"; exit 2 }
$rvp = $null
if ($slider.TryGetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern, [ref]$rvp)) {
    $max = $rvp.Current.Maximum
    $val = $rvp.Current.Value
    Write-Output ("PROGRESS: value=" + [math]::Round($val,1) + "s max=" + [math]::Round($max,1) + "s")
    if ($max -gt 30) { Write-Output "VERDICT: real song (full audio)" }
    elseif ($max -gt 0) { Write-Output "VERDICT: SHORT AUDIO (~11s) - likely ad!" }
    else { Write-Output "VERDICT: slider empty" }
} else { Write-Output "ERR: no RangeValuePattern" }

# also read current song title in bottom bar area (first text near slider)
$kids = $slider.FindAll([System.Windows.Automation.TreeScope]::Subtree, [System.Windows.Automation.Condition]::TrueCondition)
foreach ($k in $kids) {
    if ($k.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and $k.Current.Name) {
        Write-Output ("slider-area text: " + $k.Current.Name)
    }
}
