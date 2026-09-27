param(
    [string]$Keyword = "日志设置",
    [string]$PickValue = "500"
)
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "Window not found"; exit 1 }

$script:panel = $null
function Walk($el) {
    if ($script:panel) { return }
    $p = $null
    if ($el.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$p)) {
        $kw = $el.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Keyword)))
        if ($kw) { $script:panel = $el; return }
    }
    $children = $el.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($c in $children) { Walk $c }
}
Walk $win
if (-not $script:panel) { Write-Output "Expander not found"; exit 1 }

$desc = $script:panel.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
$combo = $null
foreach ($el in $desc) {
    if ($el.Current.ControlType -eq [System.Windows.Automation.ControlType]::ComboBox) { $combo = $el; break }
}
if (-not $combo) { Write-Output "ComboBox not found"; exit 1 }

$ec = $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
$ec.Expand()
Start-Sleep -Seconds 1

# 查找目标值的 ListItem
$item = $combo.FindFirst([System.Windows.Automation.TreeScope]::Subtree,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $PickValue)))
if (-not $item) {
    # 列出可选项
    $items = $combo.FindAll([System.Windows.Automation.TreeScope]::Subtree,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::IsSelectionItemProperty, $true)))
    foreach ($i in $items) { Write-Output "Option: '$($i.Current.Name)'" }
    $ec.Collapse()
    Write-Output "Item '$PickValue' not found"; exit 1
}
$sel = $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
$sel.Select()
Start-Sleep -Seconds 2
$ec2 = $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
try { if ($ec2.Current.ExpandCollapseState -eq [System.Windows.Automation.ExpandCollapseState]::Expanded) { $ec2.Collapse() } } catch {}
Start-Sleep -Seconds 1

# 读取持久化配置
$cfg = "C:\Users\admin\AppData\Local\XYMusic\LogSettings.json"
if (Test-Path $cfg) { Write-Output "LogSettings.json: $(Get-Content $cfg -Raw)" }
