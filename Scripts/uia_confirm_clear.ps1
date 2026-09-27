Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "Window not found"; exit 1 }

$dlgName = [char]0x6E05 + [char]0x7A7A + [char]0x65E5 + [char]0x5FD7   # 清空日志
$btnClear = [char]0x6E05 + [char]0x7A7A                                 # 清空
$logDir = "$env:USERPROFILE\Documents\XYMusic\Logs"

$before = Get-ChildItem $logDir -Filter "*.log" -ErrorAction SilentlyContinue
Write-Output "Before: $($before.Count) file(s), $((($before | Measure-Object Length -Sum).Sum)) bytes"

# 找 ContentDialog 窗口(名为 清空日志 的 Window)
$dlgWin = $null
foreach ($el in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
    if ($el.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window -and $el.Current.Name -eq $dlgName) { $dlgWin = $el; break }
}
if (-not $dlgWin) { Write-Output "Dialog window not found (no dialog open)"; exit 1 }
Write-Output "Dialog window found"

# 在对话框内找 清空 按钮
$confirm = $null
foreach ($el in $dlgWin.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $btnClear)))) {
    if ($el.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button) { $confirm = $el; break }
}
if (-not $confirm) { Write-Output "Confirm button not found in dialog"; exit 1 }
$confirm.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Write-Output "Confirm clicked"
Start-Sleep -Seconds 6

$after = Get-ChildItem $logDir -Filter "*.log" -ErrorAction SilentlyContinue
$total = ($after | Measure-Object Length -Sum).Sum
Write-Output "After: $($after.Count) file(s), $total bytes"
foreach ($f in $after) { Write-Output "  $($f.Name): $($f.Length) bytes" }

# 触发切歌验证日志恢复写入
$next = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "NextMusicButton")))
if ($next) { $next.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Write-Output "Next invoked" }
Start-Sleep -Seconds 12

$log = Join-Path $logDir "WinUIMusicPlayer-$(Get-Date -Format yyyyMMdd).log"
if (Test-Path $log) {
    $fs = [System.IO.FileStream]::new($log, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    $sr = New-Object System.IO.StreamReader($fs, [System.Text.Encoding]::UTF8)
    $txt = $sr.ReadToEnd(); $sr.Close()
    $lines = ($txt -split "`r?`n" | Where-Object { $_ -ne "" })
    Write-Output "Recovery check: $((Get-Item $log).Length) bytes, $($lines.Count) lines"
    $lines | Select-Object -First 3
} else { Write-Output "Active log file MISSING after clear" }
