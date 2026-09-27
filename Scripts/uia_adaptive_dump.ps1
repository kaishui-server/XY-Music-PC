Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "ERR: window not found"; exit 1 }

$ids = @{}
foreach ($el in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
    try {
        $id = $el.Current.AutomationId
        if ($id) { $ids[$id] = ($ids[$id] + 1) }
    } catch {}
}
Write-Output "=== automation ids ==="
foreach ($kv in $ids.GetEnumerator() | Sort-Object Name) { Write-Output ("  " + $kv.Key + " x" + $kv.Value) }
