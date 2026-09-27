param([int]$PrefetchWaitSec = 90)
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "Window not found"; exit 1 }

$log = "$env:USERPROFILE\Documents\XYMusic\Logs\WinUIMusicPlayer-$(Get-Date -Format yyyyMMdd).log"
function Get-Timing {
    $lines = @()
    if (Test-Path $log) {
        $lines = @(Get-Content $log | Where-Object { $_ -match "\[播放计时\]|播放会话|自动切歌" } | Select-Object -Last 30)
    }
    return $lines
}

function Get-Title {
    $t = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "MusicTitleTextBlock")))
    if ($t) { return $t.Current.Name }
    return "(no title)"
}

function Invoke-ById([string]$id) {
    $el = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)))
    if (-not $el) { Write-Output "id not found: $id"; return $false }
    $inv = $null
    if ($el.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$inv)) { $inv.Invoke(); return $true }
    return $false
}

$baseline = Get-Content $log | Measure-Object -Line
$baseLines = $baseline.Lines

Write-Output "=== click PlayAll ==="
Invoke-ById "PlayAllButton" | Out-Null
Start-Sleep -Seconds 6
Write-Output "title now: '$(Get-Title)'"

Write-Output "=== waiting for prefetch log (up to ${PrefetchWaitSec}s) ==="
$deadline = (Get-Date).AddSeconds($PrefetchWaitSec)
$prefetched = $false
while ((Get-Date) -lt $deadline) {
    $new = Get-Content $log | Select-Object -Skip $baseLines | Where-Object { $_ -match "播放计时" }
    if ($new | Where-Object { $_ -match "预取完成" }) { $prefetched = $true; break }
    if ($new | Where-Object { $_ -match "预取" }) { Write-Output "  (prefetch started, still running...)" }
    Start-Sleep -Seconds 5
}

Write-Output "=== timing log after play ==="
$lines = Get-Content $log | Select-Object -Skip $baseLines | Where-Object { $_ -match "\[播放计时\]|播放会话" }
$lines | ForEach-Object { Write-Output "  $_" }
Write-Output "prefetch completed: $prefetched"

if ($prefetched) {
    $beforeNext = (Get-Content $log | Measure-Object -Line).Lines
    $t0 = Get-Date
    Write-Output "=== click Next ==="
    Invoke-ById "NextMusicButton" | Out-Null
    Start-Sleep -Seconds 4
    $elapsed = ((Get-Date) - $t0).TotalMilliseconds
    Write-Output "next clicked, title now: '$(Get-Title)' (${elapsed}ms after click)"
    Write-Output "=== new log lines after next click ==="
    $after = Get-Content $log | Select-Object -Skip $beforeNext | Where-Object { $_ -match "\[播放计时\]|播放会话|自动切歌" }
    if ($after) { $after | ForEach-Object { Write-Output "  $_" } }
    else { Write-Output "  (none - cache hit, zero resolution)" }
}
