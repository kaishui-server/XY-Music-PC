Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "Window not found"; exit 1 }

$all = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

# 所有按钮: 名字(可空)/AutomationId/IsEnabled
Write-Output "=== All buttons ==="
$i = 0
foreach ($el in $all) {
    if ($el.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button) {
        Write-Output ("btn#{0} Name='{1}' AId='{2}' Enabled={3}" -f $i, $el.Current.Name, $el.Current.AutomationId, $el.Current.IsEnabled)
        $i++
    }
}

# 文本元素前 40 个, 看页面内容
Write-Output "=== Text elements (first 40) ==="
$i = 0
foreach ($el in $all) {
    if ($el.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text) {
        Write-Output ("txt#{0} '{1}'" -f $i, $el.Current.Name)
        $i++
        if ($i -ge 40) { break }
    }
}
