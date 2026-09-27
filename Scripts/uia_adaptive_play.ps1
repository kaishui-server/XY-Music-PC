param([string]$Row = "1")
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "ERR: window not found"; exit 1 }

# 1. nav to music library (音乐库)
$name = -join @(0x97F3, 0x4E50, 0x5E93 | ForEach-Object { [char]$_ })
$ncond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $name)
$nav = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $ncond)
if ($nav) {
    $sel = $null
    if ($nav.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$sel)) { $sel.Select(); Write-Output "nav OK" }
} else { Write-Output "nav not found" }
Start-Sleep -Seconds 3

# 2. select favourite tab (收藏)
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
} else { Write-Output "fav tab not found" }
Start-Sleep -Seconds 4

# 3. click row play button (CoverThumbnail) - Nth occurrence
$btnCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "CoverThumbnail")
$btns = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
Write-Output ("CoverThumbnail count: " + $btns.Count)
$idx = [int]$Row - 1
if ($idx -ge $btns.Count) { $idx = 0 }
if ($btns.Count -gt 0) {
    $inv = $null
    if ($btns[$idx].TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$inv)) {
        $inv.Invoke(); Write-Output ("playing row " + ($idx + 1))
    } else { Write-Output "ERR: no invoke pattern on CoverThumbnail" }
} else { Write-Output "ERR: no CoverThumbnail buttons"; exit 2 }
Start-Sleep -Seconds 10

# 4. open playing detail page via bottom bar cover button
$coverCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "AlbumCoverImageBtn")
$cover = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $coverCond)
if ($cover) {
    $inv2 = $null
    if ($cover.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$inv2)) { $inv2.Invoke(); Write-Output "detail page opened" }
    else { Write-Output "ERR: AlbumCoverImageBtn has no InvokePattern" }
} else { Write-Output "ERR: AlbumCoverImageBtn not found" }
Start-Sleep -Seconds 6
Write-Output "DONE"
