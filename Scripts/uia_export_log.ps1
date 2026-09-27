param([string]$BtnName = "导出", [string]$TargetFile = "C:\Users\admin\Desktop\xymusic-export-test.log")
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "Window not found"; exit 1 }

# 找到"日志设置" expander 内的最后一个导出按钮之前的第三个(导出全部)
# expander 里按钮顺序: 导出(崩溃), 导出(错误), 导出(全部), 清空
$script:panel = $null
function Walk($el) {
    if ($script:panel) { return }
    $p = $null
    if ($el.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$p)) {
        $kw = $el.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "日志设置")))
        if ($kw) { $script:panel = $el; return }
    }
    $children = $el.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($c in $children) { Walk $c }
}
Walk $win
if (-not $script:panel) { Write-Output "Expander not found"; exit 1 }

$btns = @()
foreach ($el in $script:panel.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
    if ($el.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and $el.Current.Name -eq "导出") { $btns += $el }
}
if ($btns.Count -lt 3) { Write-Output "Export buttons not found (count=$($btns.Count))"; exit 1 }
$allBtn = $btns[2]  # 第三个 = 导出全部日志
Write-Output "Export-all button found, invoking..."
$allBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Seconds 4

# 查找系统保存对话框 (Win32 窗口, 标题含"另存为"或"保存")
$dlg = $null
for ($i = 0; $i -lt 10; $i++) {
    foreach ($w in $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
        $n = $w.Current.Name
        if ($n -match "另存为|保存为|Save As") { $dlg = $w; break }
    }
    if ($dlg) { break }
    Start-Sleep -Seconds 1
}
if (-not $dlg) { Write-Output "Save dialog not found"; exit 1 }
Write-Output "Save dialog found: '$($dlg.Current.Name)'"

# 文件名输入框
$name = [System.IO.Path]::GetFileName($TargetFile)
$edit = $null
foreach ($e in $dlg.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)))) {
    $edit = $e; break
}
if (-not $edit) { Write-Output "Filename edit not found"; exit 1 }
$vp = $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
$vp.SetValue($name)
Write-Output "Filename set: $name"
Start-Sleep -Seconds 1

# 保存按钮
$save = $null
foreach ($b in $dlg.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "保存")))) {
    if ($b.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button) { $save = $b; break }
}
if (-not $save) { Write-Output "Save button not found"; exit 1 }
$save.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Write-Output "Save clicked"
Start-Sleep -Seconds 4

# 验证导出文件
if (Test-Path $TargetFile) {
    $len = (Get-Item $TargetFile).Length
    Write-Output "EXPORTED: $TargetFile ($len bytes)"
    Get-Content $TargetFile -TotalCount 5 -Encoding UTF8
} else { Write-Output "Export file NOT found" }
