Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "Window not found"; exit 1 }

# 找到 ContentDialog 的 Window 元素 (Name='清空日志', ControlType=Window)
$dlgWin = $null
foreach ($el in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
    if ($el.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window -and $el.Current.Name -eq "清空日志") {
        $dlgWin = $el; break
    }
}
if (-not $dlgWin) { Write-Output "Dialog window not found"; exit 1 }
Write-Output "Dialog window found"

# 在对话框内找"清空"按钮
$btn = $dlgWin.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "清空")))
if (-not $btn) { Write-Output "Dialog button not found"; exit 1 }
Write-Output "Dialog button found: $($btn.Current.ControlType.ProgrammaticName)"

# 尝试多种触发方式
$invoked = $false
$ip = $null
if ($btn.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$ip)) {
    $ip.Invoke()
    $invoked = $true
    Write-Output "InvokePattern done"
}
if (-not $invoked) {
    $lap = $null
    if ($btn.TryGetCurrentPattern([System.Windows.Automation.LegacyIAccessiblePattern]::Pattern, [ref]$lap)) {
        $lap.DoDefaultAction()
        Write-Output "LegacyIAccessible DoDefaultAction done"
    } else { Write-Output "No actionable pattern"; exit 1 }
}
Start-Sleep -Seconds 4

# 验证
$dir = "$env:USERPROFILE\Documents\XYMusic\Logs"
Write-Output "--- Log dir after clear ---"
$files = Get-ChildItem $dir -Force -ErrorAction SilentlyContinue
if ($files) { $files | ForEach-Object { Write-Output "$($_.Name) $($_.Length) $($_.LastWriteTime)" } }
else { Write-Output "(empty)" }

# 检查 Toast 结果提示
foreach ($el in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "已清空")))) {
    Write-Output "TOAST: $($el.Current.Name)"
}
