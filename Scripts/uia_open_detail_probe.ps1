Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "ERR: window not found"; exit 1 }

# open playing detail page via bottom bar cover button
$cover = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "AlbumCoverImageBtn")))
if ($cover) {
    $inv = $null
    if ($cover.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$inv)) { $inv.Invoke(); Write-Output "detail page opened" }
    else { Write-Output "ERR: no invoke pattern"; exit 2 }
} else { Write-Output "ERR: cover button not found"; exit 3 }
Start-Sleep -Seconds 6
Write-Output "DONE"
