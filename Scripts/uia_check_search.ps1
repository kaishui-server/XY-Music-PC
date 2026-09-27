Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "Window not found"; exit 1 }

# nav to online search (0x5728 0x7EBF 0x641C 0x7D22)
$name = -join @(0x5728, 0x7EBF, 0x641C, 0x7D22 | ForEach-Object { [char]$_ })
$ncond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $name)
$nav = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $ncond)
if (-not $nav) { Write-Output "NAV NOT FOUND"; exit 2 }
$sel = $null
if ($nav.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$sel)) { $sel.Select() }
Start-Sleep -Seconds 3

$texts = @()
foreach ($el in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
    try { if ($el.Current.Name) { $texts += $el.Current.Name } } catch {}
}
$unique = $texts | Select-Object -Unique
Write-Output "=== online search page texts ==="
Write-Output ($unique -join " | ")

# netease = 0x7F51 0x6613 0x4E91 ; noPluginToast = 0x5C1A 0x65E0 0x53EF 0x7528 0x63D2 0x4EF6
$netease = -join @(0x7F51, 0x6613, 0x4E91 | ForEach-Object { [char]$_ })
$noPlugin = -join @(0x5C1A, 0x65E0, 0x53EF, 0x7528, 0x63D2, 0x4EF6 | ForEach-Object { [char]$_ })
$foundTab = @($unique | Where-Object { $_ -like "*$netease*" }).Count
$foundToast = @($unique | Where-Object { $_ -like "*$noPlugin*" }).Count
if ($foundTab -gt 0) { Write-Output "PASS: netease tab visible" } else { Write-Output "WARN: netease tab not found" }
if ($foundToast -gt 0) { Write-Output "FAIL: still showing no-plugin toast" } else { Write-Output "PASS: no no-plugin toast" }
