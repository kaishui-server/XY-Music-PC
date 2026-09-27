param([int]$WaitSec = 150)
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$log = "$env:USERPROFILE\Documents\XYMusic\Logs\WinUIMusicPlayer-$(Get-Date -Format yyyyMMdd).log"
$baseLines = (Get-Content $log | Measure-Object -Line).Lines
Write-Output "waiting up to ${WaitSec}s for auto-advance..."

$deadline = (Get-Date).AddSeconds($WaitSec)
$advanced = $false
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 5
    $new = Get-Content $log | Select-Object -Skip $baseLines
    if ($new | Where-Object { $_ -match "预取开始|播放会话|自动切歌失败" }) { $advanced = $true; break }
}

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
$title = "(window gone)"
if ($win) {
    $t = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "MusicTitleTextBlock")))
    if ($t) { $title = $t.Current.Name }
}
Write-Output "auto-advance observed: $advanced"
Write-Output "title now: '$title'"
Write-Output "=== log lines since wait start ==="
Get-Content $log | Select-Object -Skip $baseLines | Where-Object { $_ -match "\[播放计时\]|播放会话|自动切歌" } | ForEach-Object { Write-Output "  $_" }
