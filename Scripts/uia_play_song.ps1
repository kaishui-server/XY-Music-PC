param([string]$Title = "Other Side", [int]$MaxPages = 20)
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
    $inv0 = $null
    if ($cancel.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$inv0)) { $inv0.Invoke(); Write-Output "detail closed" }
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
} else { Write-Output "fav tab not found"; exit 3 }
Start-Sleep -Seconds 4

$walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
$btnCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "CoverThumbnail")
$listType = [System.Windows.Automation.ControlType]::List
$liTypeCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)

function Find-RowButton($window, $title) {
    $btns = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
    foreach ($b in $btns) {
        # walk up to row (ListItem)
        $p = $b
        $row = $null
        while ($p -ne $null) {
            $p = $walker.GetParent($p)
            if ($p -eq $null) { break }
            if ($p.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem) { $row = $p; break }
            if ($p.Current.ControlType -eq $listType) { break }
        }
        if ($row -eq $null) { continue }
        $nameEls = $row.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)))
        foreach ($t in $nameEls) {
            $n = $t.Current.Name
            if ($n -ne $null -and $n.ToLowerInvariant().Contains($title.ToLowerInvariant())) {
                return @{ Row = $row; Text = $n; Btn = $b }
            }
        }
    }
    return $null
}

$found = Find-RowButton $win $Title
$page = 0
while ($found -eq $null -and $page -lt $MaxPages) {
    # scroll the favorites list one page down
    $btns = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
    if ($btns.Count -eq 0) { break }
    $listEl = $null
    $p = $btns[0]
    while ($p -ne $null) {
        $p = $walker.GetParent($p)
        if ($p -eq $null) { break }
        if ($p.Current.ControlType -eq $listType) { $listEl = $p; break }
    }
    if ($listEl -eq $null) { break }
    $sp = $null
    if (-not $listEl.TryGetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern, [ref]$sp)) { break }
    if ($sp.Current.VerticalScrollPercent -ge 99.5) { Write-Output "bottom reached"; break }
    $sp.Scroll([System.Windows.Automation.ScrollAmount]::NoAmount, [System.Windows.Automation.ScrollAmount]::LargeIncrement)
    Start-Sleep -Milliseconds 900
    $page++
    $found = Find-RowButton $win $Title
}
if ($found -eq $null) { Write-Output "ERR: row not found for '$Title'"; exit 4 }
Write-Output ("FOUND row text: " + $found.Text)

$inv = $null
if ($found.Btn.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$inv)) {
    $inv.Invoke(); Write-Output "play invoked"
} else { Write-Output "ERR: no invoke pattern"; exit 5 }
Write-Output "PLAYING"
