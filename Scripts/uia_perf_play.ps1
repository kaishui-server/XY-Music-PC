Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "Window not found"; exit 1 }

# nav to music library (0x97F3 0x4E50 0x5E93)
$name = -join @(0x97F3, 0x4E50, 0x5E93 | ForEach-Object { [char]$_ })
$ncond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $name)
$nav = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $ncond)
if (-not $nav) { Write-Output "NAV NOT FOUND: $name"; exit 2 }
$sel = $null
if ($nav.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$sel)) { $sel.Select(); Write-Output "nav OK" }
Start-Sleep -Seconds 3

# find favourite tab (0x6536 0x85CF) and click if present
$favName = -join @(0x6536, 0x85CF | ForEach-Object { [char]$_ })
$fcond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $favName)
$fav = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $fcond)
if ($fav) {
    $fsel = $null
    if ($fav.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$fsel)) { $fsel.Select(); Write-Output "fav tab selected" }
    else {
        $inv = $null
        if ($fav.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$inv)) { $inv.Invoke(); Write-Output "fav tab invoked" }
    }
    Start-Sleep -Seconds 3
} else { Write-Output "fav tab not found (maybe already on it)" }

# dump buttons with automation ids (play buttons on rows)
$ids = @{}
foreach ($el in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
    try {
        $id = $el.Current.AutomationId
        if ($id) { $ids[$id] = ($ids[$id] + 1) }
    } catch {}
}
Write-Output "=== automation ids on page ==="
foreach ($kv in $ids.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 25) {
    Write-Output "  $($kv.Key) x$($kv.Value)"
}
