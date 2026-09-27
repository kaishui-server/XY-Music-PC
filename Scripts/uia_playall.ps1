Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "Window not found"; exit 1 }

$btn = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "PlayAllButton")))
if (-not $btn) { Write-Output "PlayAllButton not found"; exit 1 }
try {
    $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Write-Output "PlayAll invoked"
} catch { Write-Output "PlayAll failed: $($_.Exception.Message)"; exit 1 }

Start-Sleep -Seconds 15

# 检查播放状态与日志
$title = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "MusicTitleTextBlock")))
Write-Output "Now playing: '$($title.Current.Name)'"

$dir = "$env:USERPROFILE\Documents\XYMusic\Logs"
$files = Get-ChildItem $dir -Force -ErrorAction SilentlyContinue
if ($files) {
    $files | ForEach-Object { Write-Output "$($_.Name) $($_.Length) $($_.LastWriteTime)" }
    $latest = $files | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    Write-Output "--- tail of $($latest.Name) ---"
    Get-Content $latest.FullName -Tail 6
} else { Write-Output "(log dir still empty)" }
