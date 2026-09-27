Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "Window not found"; exit 1 }

$log = "$env:USERPROFILE\Documents\XYMusic\Logs\WinUIMusicPlayer-$(Get-Date -Format yyyyMMdd).log"
$baseLines = (Get-Content $log | Measure-Object -Line).Lines

$slider = $null
foreach ($el in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
    if ($el.Current.ControlType -eq [System.Windows.Automation.ControlType]::Slider) { $slider = $el; break }
}
if (-not $slider) { Write-Output "slider not found"; exit 2 }

$rv = $null
if (-not $slider.TryGetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern, [ref]$rv)) {
    Write-Output "no rangevalue pattern either; patterns:"
    $slider.GetSupportedPatterns() | ForEach-Object { Write-Output "  $($_.ProgrammaticName)" }
    exit 3
}
$max = $rv.Current.Maximum
$min = $rv.Current.Minimum
Write-Output "range: $min - $max, current: $($rv.Current.Value)"
$t0 = Get-Date
$rv.SetValue([Math]::Max($min, $max - 2))
Write-Output "seeked near end"

$deadline = (Get-Date).AddSeconds(40)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 2
    $new = Get-Content $log | Select-Object -Skip $baseLines
    if ($new | Where-Object { $_ -match "预取开始|自动切歌失败" }) { break }
}
$elapsed = ((Get-Date) - $t0).TotalMilliseconds
$t = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "MusicTitleTextBlock")))
Write-Output "after ${elapsed}ms, title now: '$($t.Current.Name)'"
Write-Output "=== log lines since seek ==="
Get-Content $log | Select-Object -Skip $baseLines | Where-Object { $_ -match "\[播放计时\]|播放会话|自动切歌" } | ForEach-Object { Write-Output "  $_" }
