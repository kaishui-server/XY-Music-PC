param([int]$Count = 12, [int]$WaitSec = 8)
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "ERR: window not found"; exit 1 }

$log = "$env:USERPROFILE\Documents\XYMusic\Logs\WinUIMusicPlayer-20260927.log"
$adaptivePat = -join @(0x80CC, 0x666F, 0x81EA, 0x9002, 0x5E94 | ForEach-Object { [char]$_ })
$startPat = -join @(0x64AD, 0x653E, 0x8BA1, 0x65F6, 0x5D, 0x20, 0x5F00, 0x59CB | ForEach-Object { [char]$_ })

function Read-LogLines {
    $fs = [System.IO.FileStream]::new($log, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    $sr = New-Object System.IO.StreamReader($fs, [System.Text.Encoding]::UTF8)
    $txt = $sr.ReadToEnd(); $sr.Close(); $fs.Close()
    return ($txt -split "`r?`n" | Where-Object { $_ -ne "" })
}

# 1. open detail page via bottom bar cover button
$cover = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "AlbumCoverImageBtn")))
if ($cover) {
    $inv = $null
    if ($cover.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$inv)) { $inv.Invoke(); Write-Output "detail opened" }
    else { Write-Output "ERR: no invoke"; exit 2 }
} else { Write-Output "ERR: AlbumCoverImageBtn not found"; exit 2 }
Start-Sleep -Seconds 5
$lines = Read-LogLines
$baseline = $lines.Count
$adapt = $lines | Where-Object { $_ -match $adaptivePat } | Select-Object -Last 5
Write-Output "=== after open ==="
foreach ($a in $adapt) { Write-Output ("  " + $a) }
if (-not $adapt) { Write-Output "  (no adaptive entries yet)" }

# 2. next-track from detail page, watch flips
for ($i = 1; $i -le $Count; $i++) {
    $next = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "NextMusicButtonPlayingDetail")))
    if ($next) {
        $inv2 = $null
        if ($next.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$inv2)) { $inv2.Invoke() }
        else { Write-Output "[$i] ERR: next no invoke"; break }
    } else { Write-Output "[$i] ERR: next button gone (page closed?)"; break }
    Start-Sleep -Seconds $WaitSec
    $lines = Read-LogLines
    $new = $lines | Select-Object -Skip $baseline
    $baseline = $lines.Count
    $start = $new | Where-Object { $_ -match $startPat } | Select-Object -First 1
    if ($start) { $song = ($start -replace '^.*' + $startPat + ' ', '') } else { $song = "(no start)" }
    Write-Output ("[$i] " + $song)
    foreach ($a in ($new | Where-Object { $_ -match $adaptivePat })) { Write-Output ("    ADAPTIVE: " + $a) }
}
Write-Output "DONE"
