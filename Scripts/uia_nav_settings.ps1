Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "Window not found"; exit 1 }

$aidCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "SettingsItem")
$item = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $aidCond)
if (-not $item) { Write-Output "SettingsItem not found"; exit 1 }

$selPat = $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
$selPat.Select()
Write-Output "Settings selected"
Start-Sleep -Seconds 3

# 验证设置页已加载：查找设置页标题
$txtCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "设置")
$titles = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $txtCond)
Write-Output "Elements named '设置': $($titles.Count)"
