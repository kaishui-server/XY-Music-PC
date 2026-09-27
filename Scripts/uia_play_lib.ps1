Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "Window not found"; exit 1 }

# 导航到音乐库
$nav = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "音乐库")))
if (-not $nav) { Write-Output "音乐库 nav not found"; exit 1 }
$nav.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Write-Output "音乐库 selected"
Start-Sleep -Seconds 4

# 点击播放全部
$btn = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "PlayAllButton")))
if (-not $btn) { Write-Output "PlayAllButton not found"; exit 1 }
$btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Write-Output "PlayAll invoked"
Start-Sleep -Seconds 15

$title = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "MusicTitleTextBlock")))
if ($title) { Write-Output "Now playing: '$($title.Current.Name)'" }

# 共享读日志, 看是否有 09:xx 条目
$log = "$env:USERPROFILE\Documents\XYMusic\Logs\WinUIMusicPlayer-20260927.log"
$fs = [System.IO.FileStream]::new($log, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
$sr = New-Object System.IO.StreamReader($fs, [System.Text.Encoding]::UTF8)
$txt = $sr.ReadToEnd(); $sr.Close()
$lines = $txt -split "`r?`n" | Where-Object { $_ -ne "" }
Write-Output "Log lines total: $($lines.Count) (file $((Get-Item $log).Length) bytes)"
$new = $lines | Where-Object { $_ -match "^2026-09-27 (09|1\d):" }
Write-Output "Entries after 09:00: $($new.Count)"
$new | Select-Object -First 15
