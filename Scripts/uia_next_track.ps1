Add-Type -AssemblyName UIAutomationClient
Add-Type -System.Configuration
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "Window not found"; exit 1 }

$next = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "NextMusicButton")))
if ($next) {
    $next.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Write-Output "Next invoked"
} else { Write-Output "NextMusicButton not found"; exit 1 }

Start-Sleep -Seconds 12

$title = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "MusicTitleTextBlock")))
Write-Output "Now playing: '$($title.Current.Name)'"

$log = "$env:USERPROFILE\Documents\XYMusic\Logs\WinUIMusicPlayer-20260927.log"
$item = Get-Item $log
Write-Output "Log size: $($item.Length) bytes (logical), LastWrite: $($item.LastWriteTime)"
# 磁盘实际占用
$dsk = (Get-ChildItem $log | ForEach-Object { $_.Length })
Write-Output "--- raw tail (hex positions matter) ---"
$bytes = [System.IO.File]::ReadAllBytes($log)
Write-Output "Total bytes: $($bytes.Length), NUL count before first non-NUL: $([Array]::IndexOf($bytes, [byte]0)) position of first NUL"
$nonNull = @($bytes | Where-Object { $_ -ne 0 }).Count
Write-Output "Non-NUL bytes: $nonNull"
