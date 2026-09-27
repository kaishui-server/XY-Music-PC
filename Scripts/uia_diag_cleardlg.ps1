Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "Window not found"; exit 1 }

# 对话框标题: 清空日志
$dlgName = [char]0x6E05 + [char]0x7A7A + [char]0x65E5 + [char]0x5FD7
$btnClear = [char]0x6E05 + [char]0x7A7A

# 找到 ContentDialog (名为"清空日志"的元素, ContentDialog 暴露为 Pane/Custom)
$dlgEls = @()
foreach ($el in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $dlgName)))) {
    $dlgEls += $el
}
Write-Output "Elements named '$dlgName': $($dlgEls.Count)"

# 列出所有"清空"按钮及其父链特征
$btns = @()
foreach ($el in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $btnClear)))) {
    if ($el.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button) { $btns += $el }
}
Write-Output "'$btnClear' buttons: $($btns.Count)"
$i = 0
foreach ($b in $btns) {
    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    $chain = @()
    $n = $b
    for ($j = 0; $j -lt 6; $j++) {
        $n = $walker.GetParent($n)
        if (-not $n) { break }
        $chain += "$($n.Current.ControlType.ProgrammaticName -replace 'ControlType\.',''):'$($n.Current.Name)'"
    }
    Write-Output "btn#$i parents: $($chain -join ' < ')"
    $i++
}
