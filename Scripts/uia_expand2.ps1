param([string]$Keyword = "日志设置")
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "Window not found"; exit 1 }

# 找到所有支持 ExpandCollapsePattern 的后代
$all = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
Write-Output "Total descendants: $($all.Count)"
foreach ($el in $all) {
    $p = $null
    if ($el.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$p)) {
        # 检查该元素的子孙是否包含关键词
        $kw = $el.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Keyword)))
        $mark = if ($kw) { " <<< MATCH" } else { "" }
        Write-Output ("Expander: Name='{0}' AId='{1}' State={2}{3}" -f $el.Current.Name, $el.Current.AutomationId, $p.Current.ExpandCollapseState, $mark)
    }
}
