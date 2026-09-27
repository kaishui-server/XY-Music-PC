param([int]$AIdx = 3, [int]$BIdx = 5)
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "XY Music")
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "ERR: window not found"; exit 1 }

$s_empty = -join @(0x6682, 0x65E0, 0x6B4C, 0x8BCD | ForEach-Object { [char]$_ })
$p_timing = -join @(0x64AD, 0x653E, 0x8BA1, 0x65F6 | ForEach-Object { [char]$_ })
$p_plugin = -join @(0x63D2, 0x4EF6, 0x6B4C, 0x8BCD | ForEach-Object { [char]$_ })
$p_search = -join @(0x8054, 0x7F51, 0x641C, 0x7D22 | ForEach-Object { [char]$_ })

$log = "$env:USERPROFILE\Documents\XYMusic\Logs\WinUIMusicPlayer-$(Get-Date -Format yyyyMMdd).log"
function Read-LogLines {
    $fs = [System.IO.FileStream]::new($log, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    $sr = New-Object System.IO.StreamReader($fs, [System.Text.Encoding]::UTF8)
    $txt = $sr.ReadToEnd(); $sr.Close(); $fs.Close()
    return ($txt -split "`r?`n" | Where-Object { $_ -ne "" })
}

function Find-ById([string]$id) {
    return $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)))
}
function Invoke-ById([string]$id) {
    $el = Find-ById $id
    if (-not $el) { return $false }
    $inv = $null
    if ($el.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$inv)) { $inv.Invoke(); return $true }
    return $false
}
function Is-DetailOpen { return ($null -ne (Find-ById "CancelPlayingDetailButton")) }
function Close-Detail {
    if (Is-DetailOpen) { Invoke-ById "CancelPlayingDetailButton" | Out-Null; Start-Sleep -Seconds 3 }
    if (Is-DetailOpen) { Write-Output "WARN: detail still open after close" } else { Write-Output "detail closed" }
}
function Open-Detail {
    if (-not (Invoke-ById "AlbumCoverImageBtn")) { Write-Output "ERR: cannot open detail"; return $false }
    Start-Sleep -Seconds 4
    return $true
}
function Get-QueueItems {
    $lv = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "CurrentPlayListView")))
    if (-not $lv) { return $null }
    return $lv.FindAll([System.Windows.Automation.TreeScope]::Children,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)))
}
function Open-Queue {
    $items = Get-QueueItems
    if ($items) { return $items }
    Invoke-ById "CurrentPlayListButton" | Out-Null
    Start-Sleep -Seconds 3
    return (Get-QueueItems)
}
function Select-QueueIndex([int]$idx) {
    $items = Open-Queue
    if (-not $items) { Write-Output "ERR: queue unavailable"; return $false }
    if ($idx -ge $items.Count) { Write-Output "ERR: index $idx out of range ($($items.Count))"; return $false }
    $tcond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    $texts = $items[$idx].FindAll([System.Windows.Automation.TreeScope]::Descendants, $tcond)
    $tname = if ($texts.Count -gt 0) { $texts[0].Current.Name } else { "?" }
    $sel = $null
    if (-not $items[$idx].TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$sel)) { Write-Output "ERR: no SelectionItemPattern on queue item"; return $false }
    $sel.Select()
    Write-Output "queue[$idx] selected: '$tname'"
    return $true
}
function Get-LyricState {
    $state = @{ Lines = @(); EmptyState = $false; Blank = $false }
    $tb = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "LyricTextBlock")))
    foreach ($el in $tb) {
        try { if ($el.Current.Name) { $state.Lines += $el.Current.Name } } catch {}
    }
    $emptyEl = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $s_empty)))
    if ($emptyEl) { $state.EmptyState = $true }
    if ($state.Lines.Count -eq 0 -and -not $state.EmptyState) { $state.Blank = $true }
    return $state
}

# ---- 0. 初始状态 ----
Close-Detail
$base0 = (Read-LogLines).Count

# ---- 1. 播放 song A (queue[$AIdx]) ----
Write-Output "=== STEP 1: play song A ==="
if (-not (Select-QueueIndex $AIdx)) { exit 2 }
Start-Sleep -Seconds 10
$seg = (Read-LogLines) | Select-Object -Skip $base0
$doneA = $seg | Where-Object { $_ -match $p_timing -and $_ -match "完成" }
if (-not $doneA) { Write-Output "ERR: song A play did not complete"; $seg | Where-Object { $_ -match $p_timing } | Select-Object -Last 6 | ForEach-Object { Write-Output "  $_" }; exit 3 }
Write-Output "song A playing OK"

# ---- 2. baseline: 首次播放应有歌词 ----
Write-Output "=== STEP 2: baseline lyrics check ==="
Start-Sleep -Seconds 8
if (-not (Open-Detail)) { exit 4 }
$state1 = Get-LyricState
Write-Output ("baseline: lines=" + $state1.Lines.Count + " emptyState=" + $state1.EmptyState + " blank=" + $state1.Blank)
if ($state1.Lines.Count -gt 0) { Write-Output "  sample: $($state1.Lines[0])" }
Close-Detail

# ---- 3. 队列切到 song B ----
Write-Output "=== STEP 3: switch to song B ==="
$baseB = (Read-LogLines).Count
if (-not (Select-QueueIndex $BIdx)) { exit 5 }
Start-Sleep -Seconds 8
$segB = (Read-LogLines) | Select-Object -Skip $baseB
$doneB = $segB | Where-Object { $_ -match $p_timing -and $_ -match "完成" }
Write-Output "song B completed: $($null -ne $doneB)"

# ---- 4. 队列点回 song A (重播!) ----
Write-Output "=== STEP 4: replay song A ==="
$baseR = (Read-LogLines).Count
if (-not (Select-QueueIndex $AIdx)) { exit 6 }
Start-Sleep -Seconds 10
$segR = (Read-LogLines) | Select-Object -Skip $baseR
$doneR = $segR | Where-Object { $_ -match $p_timing -and $_ -match "完成" }
Write-Output "song A replay completed: $($null -ne $doneR)"

# ---- 5. 打开详情页验证歌词 ----
Write-Output "=== STEP 5: verify lyrics after replay ==="
Start-Sleep -Seconds 4
if (-not (Open-Detail)) { exit 7 }
$state2 = Get-LyricState
Write-Output ("replay: lines=" + $state2.Lines.Count + " emptyState=" + $state2.EmptyState + " blank=" + $state2.Blank)
if ($state2.Lines.Count -gt 0) { Write-Output "  sample: $($state2.Lines[0])" }

# ---- 6. 重播段日志增量 ----
Write-Output "=== replay log segment (search activity?) ==="
$segR | Where-Object { $_ -match $p_timing -or $_ -match $p_plugin -or $_ -match $p_search -or $_ -match "GetKrc|GetMixed" } | ForEach-Object { if ($_.Length -gt 170) { Write-Output ("  " + $_.Substring(0,170)) } else { Write-Output ("  " + $_) } }

# ---- 7. 结论 ----
if ($state2.Lines.Count -gt 0) { Write-Output "RESULT: PASS - lyrics restored after replay" }
elseif ($state2.EmptyState) { Write-Output "RESULT: FAIL - empty state shown (cache miss)" }
else { Write-Output "RESULT: FAIL - blank lyric region" }
