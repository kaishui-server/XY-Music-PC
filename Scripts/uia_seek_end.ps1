Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "Window not found"; exit 1 }

$log = "$env:USERPROFILE\Documents\XYMusic\Logs\WinUIMusicPlayer-$(Get-Date -Format yyyyMMdd).log"
function Get-Title {
    $t = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "MusicTitleTextBlock")))
    if ($t) { return $t.Current.Name }
    return "(no title)"
}

$baseLines = (Get-Content $log | Measure-Object -Line).Lines
Write-Output "title before seek: '$(Get-Title)'"

# find progress slider
$slider = $null
foreach ($el in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
    if ($el.Current.ControlType -eq [System.Windows.Automation.ControlType]::Slider) { $slider = $el; break }
}
if (-not $slider) { Write-Output "slider not found"; exit 2 }
$vp = $null
if (-not $slider.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$vp)) { Write-Output "no value pattern"; exit 3 }
$max = $vp.Current.Maximum
Write-Output "slider max: $max, current: $($vp.Current.Value)"
$t0 = Get-Date
$vp.SetValue([Math]::Max(0, $max - 1.5))
Write-Output "seeked to end"

# wait for auto-advance
$deadline = (Get-Date).AddSeconds(30)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 2
    $new = Get-Content $log | Select-Object -Skip $baseLines
    if ($new | Where-Object { $_ -match "PlayEnded|自动切歌" }) { break }
}
$elapsed = ((Get-Date) - $t0).TotalMilliseconds
Write-Output "after ${elapsed}ms, title now: '$(Get-Title)'"
Write-Output "=== log lines since seek ==="
$after = Get-Content $log | Select-Object -Skip $baseLines | Where-Object { $_ -match "\[播放计时\]|播放会话|自动切歌" }
$after | ForEach-Object { Write-Output "  $_" }
if (-not $after) { Write-Output "  (no timing lines)" }
