param([int]$FromRow = 2, [int]$ToRow = 16, [int]$WaitSec = 8)
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
} else { Write-Output "detail not open" }

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

$log = "$env:USERPROFILE\Documents\XYMusic\Logs\WinUIMusicPlayer-20260927.log"
function Read-LogLines {
    $fs = [System.IO.FileStream]::new($log, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    $sr = New-Object System.IO.StreamReader($fs, [System.Text.Encoding]::UTF8)
    $txt = $sr.ReadToEnd(); $sr.Close(); $fs.Close()
    return ($txt -split "`r?`n" | Where-Object { $_ -ne "" })
}
$baseline = (Read-LogLines).Count

$adaptivePat = -join @(0x80CC, 0x666F, 0x81EA, 0x9002, 0x5E94 | ForEach-Object { [char]$_ })
$startPat = -join @(0x64AD, 0x653E, 0x8BA1, 0x65F6, 0x5D, 0x20, 0x5F00, 0x59CB | ForEach-Object { [char]$_ })

for ($row = $FromRow; $row -le $ToRow; $row++) {
    $btnCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "CoverThumbnail")
    $btns = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
    $idx = $row - 1
    if ($idx -ge $btns.Count) { Write-Output "[$row] ERR: only $($btns.Count) rows realized"; break }
    $inv = $null
    if ($btns[$idx].TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$inv)) {
        $inv.Invoke(); Write-Output "[$row] invoked"
    } else { Write-Output "[$row] ERR: no InvokePattern"; continue }
    Start-Sleep -Seconds $WaitSec
    $lines = Read-LogLines
    $new = $lines | Select-Object -Skip $baseline
    $baseline = $lines.Count
    $start = $new | Where-Object { $_ -match $startPat } | Select-Object -First 1
    $adaptive = $new | Where-Object { $_ -match $adaptivePat }
    if ($start) { $song = ($start -replace '^.*' + $startPat + ' ', '') } else { $song = "(no start)" }
    Write-Output ("[$row] " + $song)
    foreach ($a in $adaptive) { Write-Output ("    ADAPTIVE: " + ($a -replace '^.*' + $adaptivePat + ': ', '')) }
}
Write-Output "ROWS DONE"
