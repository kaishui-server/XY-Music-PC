Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "Window not found"; exit 1 }

$name = -join @(0x63D2, 0x4EF6, 0x7BA1, 0x7406 | ForEach-Object { [char]$_ })
$ncond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $name)
$nav = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $ncond)
if (-not $nav) { Write-Output "NAV NOT FOUND: $name"; exit 2 }

$sel = $null
if ($nav.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$sel)) {
    $sel.Select(); Write-Output "nav OK (SelectionItem)"
} else {
    $inv = $null
    if ($nav.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$inv)) {
        $inv.Invoke(); Write-Output "nav OK (Invoke)"
    } else { Write-Output "NO PATTERN"; exit 3 }
}
Start-Sleep -Seconds 2
