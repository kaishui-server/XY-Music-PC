Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "ERR: window not found"; exit 1 }

# close detail page if open
$cancel = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "CancelPlayingDetailButton")))
if ($cancel) {
    $inv = $null
    if ($cancel.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$inv)) { $inv.Invoke(); Write-Output "detail closed" }
    Start-Sleep -Seconds 3
}

# nav to music library
$navName = -join @(0x97F3, 0x4E50, 0x5E93 | ForEach-Object { [char]$_ })
$nav = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $navName)))
if ($nav) {
    $sel = $null
    if ($nav.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$sel)) { $sel.Select(); Write-Output "nav OK" }
} else { Write-Output "nav not found"; exit 2 }
Start-Sleep -Seconds 3

# select favourite tab
$favName = -join @(0x6536, 0x85CF | ForEach-Object { [char]$_ })
$fav = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $favName)))
if ($fav) {
    $fsel = $null
    if ($fav.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$fsel)) { $fsel.Select(); Write-Output "fav tab selected" }
    else {
        $finv = $null
        if ($fav.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$finv)) { $finv.Invoke(); Write-Output "fav tab invoked" }
    }
} else { Write-Output "fav tab not found" }
Start-Sleep -Seconds 4

# dump ListItem names (first 40)
$liType = [System.Windows.Automation.ControlType]::ListItem
$liCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, $liType)
$items = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $liCond)
Write-Output ("ListItem count: " + $items.Count)
$i = 0
foreach ($it in $items) {
    if ($i -ge 40) { break }
    $n = $it.Current.Name
    if ($n.Length -gt 60) { $n = $n.Substring(0, 60) }
    Write-Output ("[$i] " + $n)
    $i++
}
Write-Output "DUMP DONE"
