Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "Window not found"; exit 1 }

# 导航到首页再回设置, 触发可能的日志; 再找播放按钮触发播放
$navHome = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "首页")))
if ($navHome) {
    try { $navHome.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select() } catch { Write-Output "home select failed" }
    Write-Output "Home selected"
} else { Write-Output "Home nav not found" }
Start-Sleep -Seconds 2

# 点击播放栏的播放按钮 (PlayPauseButton)
$playBtn = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "PlayPauseButton")))
if ($playBtn) {
    $playBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Write-Output "PlayPause invoked"
} else { Write-Output "PlayPauseButton not found" }
Start-Sleep -Seconds 8

# 检查日志文件是否重新生成并有内容
$dir = "$env:USERPROFILE\Documents\XYMusic\Logs"
$files = Get-ChildItem $dir -Force -ErrorAction SilentlyContinue
if ($files) {
    $files | ForEach-Object { Write-Output "$($_.Name) $($_.Length) $($_.LastWriteTime)" }
    $latest = $files | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    Write-Output "--- tail of $($latest.Name) ---"
    Get-Content $latest.FullName -Tail 5
} else { Write-Output "(log dir still empty)" }
