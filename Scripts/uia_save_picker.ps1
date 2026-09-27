param([string]$FileName = "XYMusic-export-test.log")
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "Window not found"; exit 1 }

# 找应用内的"另存为"窗口(可能多个, 取最后一个=最顶层)
$dialogs = @()
foreach ($el in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
    if ($el.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window -and $el.Current.Name -eq "另存为") { $dialogs += $el }
}
Write-Output "Save-as dialogs: $($dialogs.Count)"
if ($dialogs.Count -eq 0) { Write-Output "No save dialog"; exit 1 }
$dlg = $dialogs[$dialogs.Count - 1]

# 文件名 Edit (在 FileNameControlHost 内)
$edit = $null
foreach ($e in $dlg.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)))) {
    $edit = $e
}
if (-not $edit) { Write-Output "Filename edit not found"; exit 1 }
$vp = $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
$vp.SetValue($FileName)
Write-Output "Filename set"
Start-Sleep -Seconds 1

# 保存按钮 "保存(S)"
$save = $null
foreach ($b in $dlg.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "1")))) {
    if ($b.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button) { $save = $b; break }
}
if (-not $save) {
    # 兜底: 按名字找
    foreach ($b in $dlg.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "保存(S)")))) {
        if ($b.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button) { $save = $b; break }
    }
}
if (-not $save) { Write-Output "Save button not found"; exit 1 }
$save.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Write-Output "Save clicked"
Start-Sleep -Seconds 5

# 验证导出文件 (SuggestedStartLocation=DocumentsLibrary)
$target = "$env:USERPROFILE\Documents\$FileName"
if (Test-Path $target) {
    Write-Output "EXPORTED: $target ($((Get-Item $target).Length) bytes)"
    Write-Output "--- head ---"
    Get-Content $target -TotalCount 4 -Encoding UTF8
    Write-Output "--- tail ---"
    Get-Content $target -Tail 3 -Encoding UTF8
} else { Write-Output "Export file NOT found at $target" }

# 若还有第二个对话框, 关闭它(按 ESC 不可行, 找取消按钮)
$rest = 0
foreach ($el in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "另存为")))) {
    if ($el.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window) { $rest++ }
}
Write-Output "Remaining save dialogs: $rest"
