Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "Window not found"; exit 1 }

# 列出所有支持 SelectionItemPattern 的元素(导航项/标签项)
$items = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::IsSelectionItemPatternAvailableProperty, $true)))
Write-Output "Selection items: $($items.Count)"
foreach ($i in $items) {
    $sel = ""
    try { if ($i.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected) { $sel = " [SELECTED]" } } catch {}
    Write-Output "AId='$($i.Current.AutomationId)' Name='$($i.Current.Name)'$sel"
}
