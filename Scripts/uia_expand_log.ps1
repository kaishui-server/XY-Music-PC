param([string]$Target = "日志设置")
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "Window not found"; exit 1 }

$txtCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Target)
$txt = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $txtCond)
if (-not $txt) { Write-Output "Target text not found"; exit 1 }

# 向上找支持 ExpandCollapsePattern 的祖先
$walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
$node = $txt
$expander = $null
for ($i = 0; $i -lt 10; $i++) {
    $node = $walker.GetParent($node)
    if (-not $node -or $node.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window) { break }
    if ($node.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$null)) { $expander = $node; break }
}
if (-not $expander) { Write-Output "No expandable ancestor found"; exit 1 }

$ecPat = $expander.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
$state = $ecPat.Current.ExpandCollapseState
Write-Output "Expander state: $state"
if ($state -eq [System.Windows.Automation.ExpandCollapseState]::Collapsed) {
    $ecPat.Expand()
    Write-Output "Expanded"
}
Start-Sleep -Seconds 2
Write-Output "Final state: $($ecPat.Current.ExpandCollapseState)"
